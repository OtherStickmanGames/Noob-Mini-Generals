using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Один NavMesh на всю арену. Источники геометрии (меш формы на чанк) хранятся готовым списком:
/// при изменении чанка заменяется только его элемент, сбора геометрии со сцены (CollectSources) нет.
/// Плитки пересобираются только изменённые, но каждое обновление UpdateNavMeshDataAsync всё равно
/// обрабатывает источники всей арены (~30 мс главного потока плюс рабочие потоки), поэтому
/// пересборка редкая: срочная (пролом стены, новая карта) — не чаще раза в minRebuildInterval,
/// мелкие изменения (воронки, сколы стен — проходимость почти не меняют) копятся и уходят
/// раз в deferredRebuildInterval или вместе со срочной.
/// Два NavMesh из одних источников: пехота (тип агента из настроек) и техника (свой тип с большим
/// радиусом, создаётся в коде) — собираются по очереди, в разных кадрах.
/// У каждого чанка два источника: суша (ходить можно) и вода (Not Walkable) —
/// непроходимая поверхность воды закрывает и дно под ней.
/// </summary>
[RequireComponent(typeof(VoxelArena))]
public class ArenaNavMesh : MonoBehaviour
{
    [SerializeField] int agentTypeID = 0;
    [Tooltip("Сколько клеток навмеша приходится на один воксель по ширине")]
    [SerializeField] int navCellsPerVoxel = 3;
    [Tooltip("Высота уступа, на который агент заходит без линков, в вокселях")]
    [SerializeField] float stepHeightInVoxels = 1.05f;
    [Tooltip("Срочная пересборка (пролом стены, новая карта) — не чаще раза за столько секунд")]
    [SerializeField] float minRebuildInterval = 1f;
    [Tooltip("Мелкие изменения (воронки, сколы стен) копятся и уходят одной пересборкой не позже, " +
             "чем через столько секунд после первого из них (или раньше — вместе со срочной)")]
    [SerializeField] float deferredRebuildInterval = 12f;

    [Tooltip("Радиус агента техники, м (танк ~2 м шириной): технике — свой NavMesh, без узких мест")]
    [SerializeField] float vehicleRadius = 1.1f;
    [Tooltip("Высота агента техники, м")]
    [SerializeField] float vehicleHeight = 1.8f;

    VoxelArena arena;

    /// <summary>NavMesh одного типа агента: пехота (и строители) или техника</summary>
    class Layer
    {
        public NavMeshBuildSettings settings;
        public NavMeshData data;
        public NavMeshDataInstance instance;
        /// <summary>Тип агента создан этим компонентом (NavMesh.CreateSettings) — удалить при выходе</summary>
        public bool created;
    }

    // [0] — пехота, [1] — техника
    Layer[] layers;
    int vehicleAgentTypeID = -1;

    const int NotWalkableArea = 1;

    // Постоянный список: индекс источника <-> слот (чанк * 2 + 0 суша / 1 вода)
    readonly List<NavMeshBuildSource> sources = new();
    readonly List<int> sourceSlots = new();
    int[] sourceIndexBySlot;

    // Копия, которую получает сборка, чтобы правки во время сборки её не трогали
    readonly List<NavMeshBuildSource> buildSources = new();
    AsyncOperation running;
    // Какой слой сейчас собирается и какие ещё ждут в этой пересборке (слои — по очереди, в разных кадрах)
    int runningLayer = -1;
    int nextLayer = -1;
    bool dirty;
    bool urgent;
    float firstDirtyTime = float.PositiveInfinity;

    static readonly Unity.Profiling.ProfilerMarker StartMarker = new("ArenaNavMesh.StartUpdate");

    readonly Stopwatch timer = new();
    int startFrame;
    float lastStartTime = float.NegativeInfinity;

    /// <summary>NavMesh собран для всех типов агентов</summary>
    public bool HasNavMesh { get; private set; }
    public bool IsBusy => dirty || running != null || nextLayer >= 0;
    public double LastBuildMs { get; private set; }
    public int LastBuildFrames { get; private set; }
    public int TileSize => layers != null ? layers[0].settings.tileSize : 0;
    public int AgentTypeID => agentTypeID;
    /// <summary>Тип агента техники (создаётся при первой сборке; до неё — -1)</summary>
    public int VehicleAgentTypeID => vehicleAgentTypeID;

    void Awake()
    {
        arena = GetComponent<VoxelArena>();
        arena.ChunkMeshChanged += Chunk_MeshChanged;
        arena.Generated += RequestUrgentRebuild;
    }

    void OnDestroy()
    {
        arena.ChunkMeshChanged -= Chunk_MeshChanged;
        arena.Generated -= RequestUrgentRebuild;

        if (layers == null)
            return;
        foreach (var layer in layers)
        {
            layer.instance.Remove();
            if (layer.created)
                NavMesh.RemoveSettings(layer.settings.agentTypeID);
        }
    }

    // Ленивая инициализация: первые события приходят из VoxelArena.Start,
    // к этому моменту размеры арены уже известны
    void Init()
    {
        var infantry = NavMesh.GetSettingsByID(agentTypeID);
        Configure(ref infantry);

        // Тип агента техники создаётся в коде — в настройках проекта его заводить не нужно
        var vehicle = NavMesh.CreateSettings();
        vehicle.agentRadius = vehicleRadius;
        vehicle.agentHeight = vehicleHeight;
        vehicle.agentSlope = infantry.agentSlope;
        Configure(ref vehicle);
        vehicleAgentTypeID = vehicle.agentTypeID;

        layers = new[] { CreateLayer(infantry, false), CreateLayer(vehicle, true) };

        sourceIndexBySlot = new int[arena.ChunkTotal * 2];
        for (int i = 0; i < sourceIndexBySlot.Length; i++)
            sourceIndexBySlot[i] = -1;
    }

    void Configure(ref NavMeshBuildSettings settings)
    {
        settings.overrideVoxelSize = true;
        settings.voxelSize = arena.VoxelSize / navCellsPerVoxel;

        // Плитка размером с чанк: изменение в одном месте пересобирает 1-4 плитки
        settings.overrideTileSize = true;
        settings.tileSize = Mathf.Max(16, Mathf.RoundToInt(VoxelArena.ChunkSize * arena.VoxelSize / settings.voxelSize));

        settings.agentClimb = Mathf.Max(settings.agentClimb, arena.VoxelSize * stepHeightInVoxels);
    }

    static Layer CreateLayer(NavMeshBuildSettings settings, bool created)
    {
        var data = new NavMeshData(settings.agentTypeID);
        return new Layer
        {
            settings = settings,
            data = data,
            instance = NavMesh.AddNavMeshData(data),
            created = created,
        };
    }

    void Chunk_MeshChanged(int chunk, Mesh land, Mesh water, Matrix4x4 localToWorld)
    {
        if (sourceIndexBySlot == null)
            Init();

        UpdateSource(chunk * 2, land, localToWorld, 0);
        UpdateSource(chunk * 2 + 1, water, localToWorld, NotWalkableArea);
        if (!dirty)
            firstDirtyTime = Time.realtimeSinceStartup;
        dirty = true;
    }

    /// <summary>
    /// Изменилась проходимость (пролом стены, новая карта): пересобрать, как только можно,
    /// а не ждать накопления мелких изменений
    /// </summary>
    public void RequestUrgentRebuild() => urgent = true;

    void UpdateSource(int slot, Mesh mesh, Matrix4x4 localToWorld, int area)
    {
        if (mesh == null)
            RemoveSource(slot);
        else
            SetSource(slot, mesh, localToWorld, area);
    }

    void SetSource(int slot, Mesh mesh, Matrix4x4 localToWorld, int area)
    {
        var source = new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Mesh,
            sourceObject = mesh,
            transform = localToWorld,
            area = area,
        };

        int index = sourceIndexBySlot[slot];
        if (index >= 0)
        {
            sources[index] = source;
            return;
        }

        sourceIndexBySlot[slot] = sources.Count;
        sources.Add(source);
        sourceSlots.Add(slot);
    }

    // Удаление обменом с последним
    void RemoveSource(int slot)
    {
        int index = sourceIndexBySlot[slot];
        if (index < 0)
            return;

        int last = sources.Count - 1;
        sources[index] = sources[last];
        sourceSlots[index] = sourceSlots[last];
        sourceIndexBySlot[sourceSlots[index]] = index;

        sources.RemoveAt(last);
        sourceSlots.RemoveAt(last);
        sourceIndexBySlot[slot] = -1;
    }

    void Update()
    {
        if (running != null && running.isDone)
        {
            LastBuildMs = timer.Elapsed.TotalMilliseconds;
            LastBuildFrames = Time.frameCount - startFrame;
            running = null;
            if (runningLayer == layers.Length - 1)
                HasNavMesh = true;
            runningLayer = -1;
        }
        if (running != null)
            return;

        // Следующий слой той же пересборки — в следующем кадре после предыдущего: запуск каждого
        // занимает главный поток, вместе они дали бы двойное подвисание
        if (nextLayer >= 0)
        {
            StartLayer(nextLayer);
            nextLayer = nextLayer + 1 < layers.Length ? nextLayer + 1 : -1;
            return;
        }

        // Одна пересборка за раз; всё, что изменилось во время неё и после, уйдёт следующей.
        // Первая — сразу: без неё юниты не могут появиться
        if (!dirty)
            return;
        float now = Time.realtimeSinceStartup;
        bool due = !HasNavMesh ||
                   (urgent && now - lastStartTime >= minRebuildInterval) ||
                   now - firstDirtyTime >= deferredRebuildInterval;
        if (!due)
            return;

        dirty = false;
        urgent = false;
        firstDirtyTime = float.PositiveInfinity;
        lastStartTime = now;
        buildSources.Clear();
        buildSources.AddRange(sources);

        timer.Restart();
        startFrame = Time.frameCount;
        StartLayer(0);
        nextLayer = layers.Length > 1 ? 1 : -1;
    }

    void StartLayer(int index)
    {
        var bounds = arena.WorldBounds;
        bounds.Expand(new Vector3(0f, 4f, 0f));

        var layer = layers[index];
        runningLayer = index;
        // Запуск сборки сам занимает главный поток (Unity забирает данные всех источников)
        using (StartMarker.Auto())
            running = NavMeshBuilder.UpdateNavMeshDataAsync(layer.data, layer.settings, buildSources, bounds);
    }
}
