using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

/// <summary>
/// Воксельная арена фиксированного размера. Все воксели лежат в одном массиве,
/// чанки 16³ нужны только для мешей и коллайдеров. Арена не должна быть повёрнута.
/// </summary>
public class VoxelArena : MonoBehaviour
{
    public const int ChunkSize = 16;

    static readonly int PaletteTexId = Shader.PropertyToID("_VoxelPaletteTex");
    static readonly int VoxelSizeId = Shader.PropertyToID("_VoxelSize");

    static readonly VertexAttributeDescriptor[] VertexLayout =
    {
        new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
        new(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
    };

    [SerializeField] Material material;
    [SerializeField] int sizeX = 128;
    [SerializeField] int sizeY = 32;
    [SerializeField] int sizeZ = 128;
    [SerializeField] float voxelSize = 0.5f;
    [SerializeField] ArenaGenSettings generation = ArenaGenSettings.Default;
    [Tooltip("Готовить данные коллайдеров в рабочих потоках (Physics.BakeMesh)")]
    [SerializeField] bool bakeCollidersInJobs = false;

    /// <summary>Меш чанка изменился. Mesh == null — чанк пустой.</summary>
    public event Action<int, Mesh, Matrix4x4> ChunkMeshChanged;
    public event Action Generated;

    public int3 Dims => dims;
    public int ChunkTotal => chunkObjects.Length;
    public float VoxelSize => voxelSize;
    public NativeArray<byte> Voxels => voxels;
    public Vector3 BaseOne { get; private set; }
    public Vector3 BaseTwo { get; private set; }

    public int Seed
    {
        get => generation.seed;
        set => generation.seed = value;
    }

    public Bounds WorldBounds
    {
        get
        {
            var size = (Vector3)(float3)dims * voxelSize;
            return new Bounds(transform.position + size * 0.5f, size);
        }
    }

    // Замеры последней перестройки
    public int LastRebuiltChunks { get; private set; }
    public double LastMeshMs { get; private set; }
    public double LastApplyMs { get; private set; }
    public double LastColliderMs { get; private set; }
    public int TotalTriangles { get; private set; }

    int3 dims;
    int3 chunkCounts;

    NativeArray<byte> voxels;
    NativeArray<byte> faceColors;
    Texture2D paletteTexture;

    GameObject[] chunkObjects;
    Mesh[] meshes;
    MeshRenderer[] renderers;
    MeshCollider[] colliders;
    int[] triangleCounts;
    bool[] dirty;
    readonly List<int> dirtyChunks = new();

    readonly Stopwatch timer = new();

    void Awake()
    {
        dims = new int3(RoundToChunk(sizeX), RoundToChunk(sizeY), RoundToChunk(sizeZ));
        chunkCounts = dims / ChunkSize;

        voxels = new NativeArray<byte>(dims.x * dims.y * dims.z, Allocator.Persistent);
        faceColors = VoxelBlocks.CreateFaceTable(Allocator.Persistent);

        paletteTexture = VoxelBlocks.CreatePaletteTexture();
        Shader.SetGlobalTexture(PaletteTexId, paletteTexture);
        Shader.SetGlobalFloat(VoxelSizeId, voxelSize);

        CreateChunkObjects();
    }

    void Start()
    {
        Generate();
    }

    void LateUpdate()
    {
        // Все правки за кадр перестраиваются одной пачкой
        if (dirtyChunks.Count > 0)
            RebuildDirtyChunks();
    }

    void OnDestroy()
    {
        voxels.Dispose();
        faceColors.Dispose();

        foreach (var mesh in meshes)
            Destroy(mesh);
        Destroy(paletteTexture);
    }

    public void Generate()
    {
        var random = new Unity.Mathematics.Random(math.hash(new int2(generation.seed, 7)) | 1u);

        new ArenaGenerateJob
        {
            dims = dims,
            settings = generation,
            hillOffset = random.NextFloat2(new float2(-10000f), new float2(10000f)),
            rampOffset = random.NextFloat2(new float2(-10000f), new float2(10000f)),
            voxels = voxels,
        }
        .Schedule(dims.x * dims.z, dims.x)
        .Complete();

        int cx = (dims.x - 1) / 2;
        BaseOne = ColumnTop(cx, generation.baseMargin);
        BaseTwo = ColumnTop(dims.x - 1 - cx, dims.z - 1 - generation.baseMargin);

        for (int c = 0; c < chunkObjects.Length; c++)
            MarkChunkDirty(c);

        RebuildDirtyChunks();
        LogColliderCheck();
        Generated?.Invoke();
    }

    /// <summary>Выбивает воксели шаром. Возвращает число выбитых.</summary>
    public int Explode(Vector3 worldCenter, float worldRadius)
    {
        float3 center = WorldToVoxel(worldCenter);
        float radius = worldRadius / voxelSize;

        // y от 1: нижний слой — неразрушаемое дно
        int3 min = math.max((int3)math.floor(center - radius), new int3(0, 1, 0));
        int3 max = math.min((int3)math.ceil(center + radius), dims - 1);

        int removed = 0;
        float radiusSq = radius * radius;

        for (int y = min.y; y <= max.y; y++)
        {
            for (int z = min.z; z <= max.z; z++)
            {
                for (int x = min.x; x <= max.x; x++)
                {
                    var cell = new float3(x + 0.5f, y + 0.5f, z + 0.5f);
                    if (math.distancesq(cell, center) > radiusSq)
                        continue;

                    int index = VoxelIndex(x, y, z);
                    byte block = voxels[index];
                    if (block == VoxelBlocks.Air || VoxelBlocks.IsIndestructible(block))
                        continue;

                    voxels[index] = VoxelBlocks.Air;
                    removed++;
                }
            }
        }

        if (removed > 0)
            MarkVoxelRangeDirty(min, max);

        return removed;
    }

    public float3 WorldToVoxel(Vector3 world)
    {
        return (float3)((world - transform.position) / voxelSize);
    }

    public Vector3 VoxelToWorld(float3 voxel)
    {
        return transform.position + (Vector3)(voxel * voxelSize);
    }

    /// <summary>Мировая точка на верхней грани самого высокого вокселя столбца.</summary>
    public Vector3 ColumnTop(int x, int z)
    {
        int y = dims.y - 1;
        while (y > 0 && voxels[VoxelIndex(x, y, z)] == VoxelBlocks.Air)
            y--;

        return VoxelToWorld(new float3(x + 0.5f, y + 1, z + 0.5f));
    }

    // Проверка: сколько коллайдеров включено и попадает ли луч сверху в центр арены
    void LogColliderCheck()
    {
        int enabledColliders = 0;
        Bounds? sample = null;
        for (int c = 0; c < colliders.Length; c++)
        {
            if (!colliders[c].enabled)
                continue;

            enabledColliders++;
            sample ??= colliders[c].bounds;
        }

        var bounds = WorldBounds;
        var origin = new Vector3(bounds.center.x, bounds.max.y + 10f, bounds.center.z);
        bool hit = Physics.Raycast(origin, Vector3.down, out var rayHit, bounds.size.y + 20f);

        Debug.Log($"[Arena] Коллайдеров включено: {enabledColliders} из {colliders.Length}, " +
                  $"bounds первого: {sample}, bake в джобах: {bakeCollidersInJobs}, " +
                  $"луч сверху в центр: {(hit ? rayHit.collider.name + " " + rayHit.point : "мимо")}");
    }

    int VoxelIndex(int x, int y, int z) => (y * dims.z + z) * dims.x + x;

    int ChunkIndex(int3 c) => (c.y * chunkCounts.z + c.z) * chunkCounts.x + c.x;

    int3 ChunkCoord(int index)
    {
        int x = index % chunkCounts.x;
        int z = index / chunkCounts.x % chunkCounts.z;
        int y = index / (chunkCounts.x * chunkCounts.z);
        return new int3(x, y, z);
    }

    static int RoundToChunk(int size)
    {
        return math.max(ChunkSize, (size + ChunkSize - 1) / ChunkSize * ChunkSize);
    }

    void MarkChunkDirty(int chunk)
    {
        if (dirty[chunk])
            return;

        dirty[chunk] = true;
        dirtyChunks.Add(chunk);
    }

    // Грани и затенение зависят от соседей, поэтому захватываем на воксель шире
    void MarkVoxelRangeDirty(int3 min, int3 max)
    {
        int3 from = math.max(min - 1, 0) / ChunkSize;
        int3 to = math.min(max + 1, dims - 1) / ChunkSize;

        for (int y = from.y; y <= to.y; y++)
            for (int z = from.z; z <= to.z; z++)
                for (int x = from.x; x <= to.x; x++)
                    MarkChunkDirty(ChunkIndex(new int3(x, y, z)));
    }

    void CreateChunkObjects()
    {
        int total = chunkCounts.x * chunkCounts.y * chunkCounts.z;

        chunkObjects = new GameObject[total];
        meshes = new Mesh[total];
        renderers = new MeshRenderer[total];
        colliders = new MeshCollider[total];
        triangleCounts = new int[total];
        dirty = new bool[total];

        for (int c = 0; c < total; c++)
        {
            int3 coord = ChunkCoord(c);

            var go = new GameObject($"Chunk {coord.x}_{coord.y}_{coord.z}");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = (Vector3)(float3)(coord * ChunkSize) * voxelSize;
            go.transform.localScale = Vector3.one * voxelSize;

            var mesh = new Mesh { name = go.name };
            mesh.MarkDynamic();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.enabled = false;

            var meshCollider = go.AddComponent<MeshCollider>();
            meshCollider.enabled = false;

            chunkObjects[c] = go;
            meshes[c] = mesh;
            renderers[c] = meshRenderer;
            colliders[c] = meshCollider;
        }
    }

    void RebuildDirtyChunks()
    {
        int count = dirtyChunks.Count;
        var vertexLists = new NativeList<VoxelVertex>[count];
        var indexLists = new NativeList<uint>[count];
        var handles = new NativeArray<JobHandle>(count, Allocator.Temp);

        // 1. Меши во всех рабочих потоках
        timer.Restart();
        for (int k = 0; k < count; k++)
        {
            vertexLists[k] = new NativeList<VoxelVertex>(1024, Allocator.TempJob);
            indexLists[k] = new NativeList<uint>(1536, Allocator.TempJob);

            handles[k] = new GreedyMeshJob
            {
                voxels = voxels,
                faceColors = faceColors,
                dims = dims,
                chunkOrigin = ChunkCoord(dirtyChunks[k]) * ChunkSize,
                chunkSize = ChunkSize,
                vertices = vertexLists[k],
                indices = indexLists[k],
            }
            .Schedule();
        }
        JobHandle.CompleteAll(handles);
        handles.Dispose();
        LastMeshMs = timer.Elapsed.TotalMilliseconds;

        // 2. Загрузка в Mesh
        timer.Restart();
        var bakeIds = new NativeList<int>(count, Allocator.TempJob);
        for (int k = 0; k < count; k++)
        {
            int chunk = dirtyChunks[k];
            ApplyMesh(chunk, vertexLists[k], indexLists[k]);

            if (triangleCounts[chunk] > 0)
                bakeIds.Add(meshes[chunk].GetInstanceID());

            vertexLists[k].Dispose();
            indexLists[k].Dispose();
        }
        LastApplyMs = timer.Elapsed.TotalMilliseconds;

        // 3. Коллайдеры
        timer.Restart();
        if (bakeCollidersInJobs)
        {
            new BakeCollidersJob { meshIds = bakeIds.AsArray() }
                .Schedule(bakeIds.Length, 1)
                .Complete();
        }
        bakeIds.Dispose();

        for (int k = 0; k < count; k++)
        {
            int chunk = dirtyChunks[k];
            bool hasMesh = triangleCounts[chunk] > 0;

            // Переназначение заставляет коллайдер взять новые данные
            colliders[chunk].sharedMesh = null;
            if (hasMesh)
                colliders[chunk].sharedMesh = meshes[chunk];

            colliders[chunk].enabled = hasMesh;
            renderers[chunk].enabled = hasMesh;
        }
        LastColliderMs = timer.Elapsed.TotalMilliseconds;

        int total = 0;
        foreach (int triangles in triangleCounts)
            total += triangles;
        TotalTriangles = total;
        LastRebuiltChunks = count;

        for (int k = 0; k < count; k++)
        {
            int chunk = dirtyChunks[k];
            dirty[chunk] = false;

            var mesh = triangleCounts[chunk] > 0 ? meshes[chunk] : null;
            ChunkMeshChanged?.Invoke(chunk, mesh, chunkObjects[chunk].transform.localToWorldMatrix);
        }

        dirtyChunks.Clear();
    }

    void ApplyMesh(int chunk, NativeList<VoxelVertex> vertices, NativeList<uint> indices)
    {
        var mesh = meshes[chunk];
        mesh.Clear();

        int vertexCount = vertices.Length;
        int indexCount = indices.Length;
        triangleCounts[chunk] = indexCount / 3;

        if (vertexCount == 0)
            return;

        const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

        mesh.SetVertexBufferParams(vertexCount, VertexLayout);
        mesh.SetVertexBufferData(vertices.AsArray(), 0, 0, vertexCount, 0, flags);

        mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
        mesh.SetIndexBufferData(indices.AsArray(), 0, 0, indexCount, flags);

        mesh.subMeshCount = 1;
        mesh.SetSubMesh(0, new SubMeshDescriptor(0, indexCount), flags);
        mesh.bounds = new Bounds(Vector3.one * (ChunkSize * 0.5f), Vector3.one * ChunkSize);
    }
}
