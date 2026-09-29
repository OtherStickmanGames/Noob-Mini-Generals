using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Один NavMesh на всю арену. Источники геометрии (по мешу на чанк) хранятся
/// готовым списком: при изменении чанка заменяется только его элемент, а
/// UpdateNavMeshDataAsync пересобирает только плитки, куда попала изменённая геометрия.
/// Сбора геометрии со сцены (CollectSources) нет вообще.
/// </summary>
[RequireComponent(typeof(VoxelArena))]
public class ArenaNavMesh : MonoBehaviour
{
    [SerializeField] int agentTypeID = 0;
    [Tooltip("Сколько клеток навмеша приходится на один воксель по ширине")]
    [SerializeField] int navCellsPerVoxel = 3;
    [Tooltip("Высота уступа, на который агент заходит без линков, в вокселях")]
    [SerializeField] float stepHeightInVoxels = 1.05f;

    VoxelArena arena;
    NavMeshData data;
    NavMeshDataInstance instance;
    NavMeshBuildSettings settings;

    // Постоянный список: индекс источника <-> чанк
    readonly List<NavMeshBuildSource> sources = new();
    readonly List<int> sourceChunks = new();
    int[] sourceIndexByChunk;

    // Копия, которую получает сборка, чтобы правки во время сборки её не трогали
    readonly List<NavMeshBuildSource> buildSources = new();
    AsyncOperation running;
    bool dirty;

    readonly Stopwatch timer = new();
    int startFrame;

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

        if (sourceIndexByChunk != null)
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

        sourceIndexByChunk = new int[arena.ChunkTotal];
        for (int i = 0; i < sourceIndexByChunk.Length; i++)
            sourceIndexByChunk[i] = -1;
    }

    void Chunk_MeshChanged(int chunk, Mesh mesh, Matrix4x4 localToWorld)
    {
        if (sourceIndexByChunk == null)
            Init();

        if (mesh == null)
            RemoveSource(chunk);
        else
            SetSource(chunk, mesh, localToWorld);

        dirty = true;
    }

    void SetSource(int chunk, Mesh mesh, Matrix4x4 localToWorld)
    {
        var source = new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Mesh,
            sourceObject = mesh,
            transform = localToWorld,
            area = 0,
        };

        int index = sourceIndexByChunk[chunk];
        if (index >= 0)
        {
            sources[index] = source;
            return;
        }

        sourceIndexByChunk[chunk] = sources.Count;
        sources.Add(source);
        sourceChunks.Add(chunk);
    }

    // Удаление обменом с последним
    void RemoveSource(int chunk)
    {
        int index = sourceIndexByChunk[chunk];
        if (index < 0)
            return;

        int last = sources.Count - 1;
        sources[index] = sources[last];
        sourceChunks[index] = sourceChunks[last];
        sourceIndexByChunk[sourceChunks[index]] = index;

        sources.RemoveAt(last);
        sourceChunks.RemoveAt(last);
        sourceIndexByChunk[chunk] = -1;
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

        // Одна сборка за раз; всё, что изменилось во время неё, уйдёт следующей
        if (!dirty || running != null)
            return;

        dirty = false;
        buildSources.Clear();
        buildSources.AddRange(sources);

        var bounds = arena.WorldBounds;
        bounds.Expand(new Vector3(0f, 4f, 0f));

        timer.Restart();
        startFrame = Time.frameCount;
        running = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, buildSources, bounds);
    }
}
