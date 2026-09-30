using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Один NavMesh на всю арену. Источники геометрии (по мешу на чанк) хранятся
/// готовым списком: при изменении чанка заменяется только его элемент, а
/// UpdateNavMeshDataAsync пересобирает только плитки, куда попала изменённая геометрия.
/// Сбора геометрии со сцены (CollectSources) нет вообще.
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
    [Tooltip("Не чаще одной пересборки за столько секунд: в бою воронки и сколы стен идут непрерывно, " +
             "а каждая сборка заново хеширует геометрию всей арены в рабочих потоках")]
    [SerializeField] float minRebuildInterval = 1f;

    VoxelArena arena;
    NavMeshData data;
    NavMeshDataInstance instance;
    NavMeshBuildSettings settings;

    const int NotWalkableArea = 1;

    // Постоянный список: индекс источника <-> слот (чанк * 2 + 0 суша / 1 вода)
    readonly List<NavMeshBuildSource> sources = new();
    readonly List<int> sourceSlots = new();
    int[] sourceIndexBySlot;

    // Копия, которую получает сборка, чтобы правки во время сборки её не трогали
    readonly List<NavMeshBuildSource> buildSources = new();
    AsyncOperation running;
    bool dirty;

    static readonly Unity.Profiling.ProfilerMarker StartMarker = new("ArenaNavMesh.StartUpdate");

    readonly Stopwatch timer = new();
    int startFrame;
    float lastStartTime = float.NegativeInfinity;

    public bool HasNavMesh { get; private set; }
    public bool IsBusy => dirty || running != null;
    public double LastBuildMs { get; private set; }
    public int LastBuildFrames { get; private set; }
    public int TileSize => settings.tileSize;
    public int AgentTypeID => agentTypeID;

    void Awake()
    {
        arena = GetComponent<VoxelArena>();
        arena.ChunkMeshChanged += Chunk_MeshChanged;
    }

    void OnDestroy()
    {
        arena.ChunkMeshChanged -= Chunk_MeshChanged;

        if (sourceIndexBySlot != null)
            instance.Remove();
    }

    // Ленивая инициализация: первые события приходят из VoxelArena.Start,
    // к этому моменту размеры арены уже известны
    void Init()
    {
        settings = NavMesh.GetSettingsByID(agentTypeID);
        settings.overrideVoxelSize = true;
        settings.voxelSize = arena.VoxelSize / navCellsPerVoxel;

        // Плитка размером с чанк: изменение в одном месте пересобирает 1-4 плитки
        settings.overrideTileSize = true;
        settings.tileSize = Mathf.Max(16, Mathf.RoundToInt(VoxelArena.ChunkSize * arena.VoxelSize / settings.voxelSize));

        settings.agentClimb = Mathf.Max(settings.agentClimb, arena.VoxelSize * stepHeightInVoxels);

        data = new NavMeshData(agentTypeID);
        instance = NavMesh.AddNavMeshData(data);

        sourceIndexBySlot = new int[arena.ChunkTotal * 2];
        for (int i = 0; i < sourceIndexBySlot.Length; i++)
            sourceIndexBySlot[i] = -1;
    }

    void Chunk_MeshChanged(int chunk, Mesh land, Mesh water, Matrix4x4 localToWorld)
    {
        if (sourceIndexBySlot == null)
            Init();

        UpdateSource(chunk * 2, land, localToWorld, 0);
        UpdateSource(chunk * 2 + 1, water, localToWorld, NotWalkableArea);
        dirty = true;
    }

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
            HasNavMesh = true;
        }

        // Одна сборка за раз; всё, что изменилось во время неё и после, уйдёт следующей.
        // Первая сборка — сразу: без неё юниты не могут появиться
        if (!dirty || running != null)
            return;
        if (HasNavMesh && Time.realtimeSinceStartup - lastStartTime < minRebuildInterval)
            return;

        dirty = false;
        lastStartTime = Time.realtimeSinceStartup;
        buildSources.Clear();
        buildSources.AddRange(sources);

        var bounds = arena.WorldBounds;
        bounds.Expand(new Vector3(0f, 4f, 0f));

        timer.Restart();
        startFrame = Time.frameCount;
        // Запуск сборки сам занимает главный поток (Unity забирает данные всех источников)
        using (StartMarker.Auto())
            running = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, buildSources, bounds);
    }
}
