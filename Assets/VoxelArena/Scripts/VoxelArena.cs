using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Воксельная арена фиксированного размера. Все воксели лежат в одном массиве,
/// чанки 16³ нужны только для мешей и коллайдеров. Арена не должна быть повёрнута.
/// У каждого чанка два меша: суша (с MeshCollider) и вода (только отрисовка).
/// </summary>
public class VoxelArena : MonoBehaviour
{
    public const int ChunkSize = 16;

    static readonly int PaletteTexId = Shader.PropertyToID("_VoxelPaletteTex");
    static readonly int VoxelSizeId = Shader.PropertyToID("_VoxelSize");

    [SerializeField] Material material;
    [SerializeField] int sizeX = 400;
    [SerializeField] int sizeY = 48;
    [SerializeField] int sizeZ = 400;
    [SerializeField] float voxelSize = 0.5f;
    [SerializeField] ArenaGenSettings generation = ArenaGenSettings.Default;

    /// <summary>Меши чанка изменились: суша, вода (null — пусто), матрица чанка.</summary>
    public event Action<int, Mesh, Mesh, Matrix4x4> ChunkMeshChanged;
    public event Action Generated;

    public int3 Dims => dims;
    public Material Material => material;
    public int ChunkTotal => chunks.Length;
    public float VoxelSize => voxelSize;
    public NativeArray<byte> Voxels => voxels;
    public ArenaLayout Layout { get; private set; }
    public ArenaBiome Biome { get; private set; }
    public Vector3 BaseOne { get; private set; }
    public Vector3 BaseTwo { get; private set; }
    /// <summary>Мировые точки сразу снаружи ворот базы один и базы два</summary>
    public Vector3 GateOne { get; private set; }
    public Vector3 GateTwo { get; private set; }

    public int Seed
    {
        get => generation.seed;
        set => generation.seed = value;
    }

    /// <summary>Мировая коробка без гор по краю: камере дальше смотреть незачем</summary>
    public Bounds PlayableBounds
    {
        get
        {
            var bounds = WorldBounds;
            float inset = generation.borderMinWidth * voxelSize;
            bounds.Expand(new Vector3(-inset * 2f, 0f, -inset * 2f));
            return bounds;
        }
    }

    public Bounds WorldBounds
    {
        get
        {
            var size = (Vector3)(float3)dims * voxelSize;
            return new Bounds(transform.position + size * 0.5f, size);
        }
    }

    // Замеры
    public double LastGenerateMs { get; private set; }
    public int LastRebuiltChunks { get; private set; }
    public double LastMeshMs { get; private set; }
    public double LastApplyMs { get; private set; }
    public int TotalTriangles { get; private set; }

    class Chunk
    {
        public GameObject root;
        public Mesh land;
        public Mesh water;
        public MeshRenderer landRenderer;
        public MeshRenderer waterRenderer;
        public MeshCollider collider;
        public int triangles;
        public bool dirty;
    }

    class MeshBuffers
    {
        public NativeList<float3> positions;
        public NativeList<float3> normals;
        public NativeList<Color32> colors;
        public NativeList<uint> indices;

        public MeshBuffers()
        {
            positions = new NativeList<float3>(1024, Allocator.TempJob);
            normals = new NativeList<float3>(1024, Allocator.TempJob);
            colors = new NativeList<Color32>(1024, Allocator.TempJob);
            indices = new NativeList<uint>(1536, Allocator.TempJob);
        }

        public void Dispose()
        {
            positions.Dispose();
            normals.Dispose();
            colors.Dispose();
            indices.Dispose();
        }
    }

    int3 dims;
    int3 chunkCounts;

    NativeArray<byte> voxels;
    NativeArray<byte> faceColors;
    Texture2D paletteTexture;

    Chunk[] chunks;
    readonly List<int> dirtyChunks = new();

    readonly Stopwatch timer = new();
    readonly Stopwatch generateTimer = new();

    void Awake()
    {
        dims = new int3(RoundToChunk(sizeX), RoundToChunk(sizeY), RoundToChunk(sizeZ));
        chunkCounts = dims / ChunkSize;

        voxels = new NativeArray<byte>(dims.x * dims.y * dims.z, Allocator.Persistent);
        faceColors = VoxelBlocks.CreateFaceTable(Allocator.Persistent);

        paletteTexture = VoxelBlocks.CreatePaletteTexture();
        Shader.SetGlobalTexture(PaletteTexId, paletteTexture);
        Shader.SetGlobalFloat(VoxelSizeId, voxelSize);

        // Сцены, сохранённые со старой версией настроек, не знают новых полей
        if (generation.version != ArenaGenSettings.CurrentVersion)
        {
            int seed = generation.seed;
            generation = ArenaGenSettings.Default;
            generation.seed = seed;
            Debug.Log("[Arena] Настройки генерации из старой версии, взяты значения по умолчанию");
        }

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

        foreach (var chunk in chunks)
        {
            Destroy(chunk.land);
            Destroy(chunk.water);
        }
        Destroy(paletteTexture);
    }

    public void Generate()
    {
        generateTimer.Restart();

        Biome = ArenaBiomes.Resolve(generation.biome, generation.seed);
        Layout = ArenaLayoutGenerator.Generate(generation, Biome, dims.x, dims.y, dims.z);
        double layoutMs = generateTimer.Elapsed.TotalMilliseconds;

        var height = new NativeArray<int>(Layout.height, Allocator.TempJob);
        var waterTop = new NativeArray<int>(Layout.waterTop, Allocator.TempJob);
        var flags = new NativeArray<byte>(Layout.flags, Allocator.TempJob);

        new ArenaGenerateJob
        {
            dims = dims,
            snowLine = ArenaBiomes.SnowLine(Biome, generation.highHeight),
            height = height,
            waterTop = waterTop,
            flags = flags,
            voxels = voxels,
        }
        .Schedule(dims.x * dims.z, dims.x)
        .Complete();

        height.Dispose();
        waterTop.Dispose();
        flags.Dispose();

        ArenaDecorator.Decorate(voxels, dims, Layout, Biome, generation.seed, generation.wallHeight);
        VoxelBlocks.WritePalette(paletteTexture, ArenaBiomes.Palette(Biome));

        BaseOne = ColumnTop(Layout.baseOne.x, Layout.baseOne.y);
        BaseTwo = ColumnTop(Layout.baseTwo.x, Layout.baseTwo.y);
        GateOne = ColumnTop(Layout.gates[0].x, Layout.gates[0].y);
        GateTwo = ColumnTop(Layout.gates[1].x, Layout.gates[1].y);

        for (int c = 0; c < chunks.Length; c++)
            MarkChunkDirty(c);

        RebuildDirtyChunks();
        LastGenerateMs = generateTimer.Elapsed.TotalMilliseconds;

        Debug.Log($"[Arena] Сид {generation.seed}, биом {ArenaBiomes.DisplayName(Biome)}: " +
                  $"участков {Layout.regionCount}, рамп {Layout.rampCount}, выровнено {Layout.flattenedRegions}, " +
                  $"по сетке: базы связаны {Layout.basesConnected}, точки захвата достижимы {Layout.capturePointsReachable}, " +
                  $"деревьев {Layout.trees.Count}, камней {Layout.rocks.Count}, " +
                  $"раскладка {layoutMs:0} мс, всего {LastGenerateMs:0} мс");

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

    /// <summary>Мировая точка на верхней грани самого высокого сплошного вокселя столбца.</summary>
    public Vector3 ColumnTop(int x, int z)
    {
        int y = dims.y - 1;
        while (y > 0)
        {
            byte block = voxels[VoxelIndex(x, y, z)];
            if (block != VoxelBlocks.Air && block != VoxelBlocks.Water)
                break;
            y--;
        }

        return VoxelToWorld(new float3(x + 0.5f, y + 1, z + 0.5f));
    }

    /// <summary>Высота поверхности столбца: y первого пустого вокселя над сплошным (вода — не сплошное)</summary>
    public int SurfaceY(int x, int z)
    {
        int y = dims.y - 1;
        while (y > 0)
        {
            byte block = voxels[VoxelIndex(x, y, z)];
            if (block != VoxelBlocks.Air && block != VoxelBlocks.Water)
                break;
            y--;
        }
        return y + 1;
    }

    /// <summary>Есть ли вода прямо над поверхностью столбца</summary>
    public bool IsWaterAt(int x, int z)
    {
        int y = SurfaceY(x, z);
        return y < dims.y && voxels[VoxelIndex(x, y, z)] == VoxelBlocks.Water;
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

    void MarkChunkDirty(int index)
    {
        if (chunks[index].dirty)
            return;

        chunks[index].dirty = true;
        dirtyChunks.Add(index);
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
        chunks = new Chunk[total];

        for (int c = 0; c < total; c++)
        {
            int3 coord = ChunkCoord(c);

            var root = new GameObject($"Chunk {coord.x}_{coord.y}_{coord.z}");
            root.layer = gameObject.layer;
            root.transform.SetParent(transform, false);
            root.transform.localPosition = (Vector3)(float3)(coord * ChunkSize) * voxelSize;
            root.transform.localScale = Vector3.one * voxelSize;

            var land = new Mesh { name = root.name };
            land.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh = land;
            var landRenderer = root.AddComponent<MeshRenderer>();
            landRenderer.sharedMaterial = material;
            landRenderer.enabled = false;
            var collider = root.AddComponent<MeshCollider>();
            collider.sharedMesh = null;
            collider.enabled = false;

            var waterObject = new GameObject("Water");
            waterObject.layer = gameObject.layer;
            waterObject.transform.SetParent(root.transform, false);
            var water = new Mesh { name = root.name + " Water" };
            water.MarkDynamic();
            waterObject.AddComponent<MeshFilter>().sharedMesh = water;
            var waterRenderer = waterObject.AddComponent<MeshRenderer>();
            waterRenderer.sharedMaterial = material;
            waterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            waterRenderer.enabled = false;

            chunks[c] = new Chunk
            {
                root = root,
                land = land,
                water = water,
                landRenderer = landRenderer,
                waterRenderer = waterRenderer,
                collider = collider,
            };
        }
    }

    void RebuildDirtyChunks()
    {
        int count = dirtyChunks.Count;
        var landBuffers = new MeshBuffers[count];
        var waterBuffers = new MeshBuffers[count];
        var handles = new NativeArray<JobHandle>(count * 2, Allocator.Temp);

        // 1. Меши во всех рабочих потоках: суша и вода отдельными джобами
        timer.Restart();
        for (int k = 0; k < count; k++)
        {
            int3 origin = ChunkCoord(dirtyChunks[k]) * ChunkSize;
            landBuffers[k] = new MeshBuffers();
            waterBuffers[k] = new MeshBuffers();

            handles[k * 2] = ScheduleMesh(origin, false, landBuffers[k]);
            handles[k * 2 + 1] = ScheduleMesh(origin, true, waterBuffers[k]);
        }
        JobHandle.CompleteAll(handles);
        handles.Dispose();
        LastMeshMs = timer.Elapsed.TotalMilliseconds;

        // 2. Загрузка в Mesh и MeshCollider (коллайдер готовит данные при назначении меша)
        timer.Restart();
        for (int k = 0; k < count; k++)
        {
            var chunk = chunks[dirtyChunks[k]];

            // Переназначение заставляет коллайдер взять новые данные
            chunk.collider.sharedMesh = null;

            bool hasLand = ApplyMesh(chunk.land, landBuffers[k]);
            chunk.landRenderer.enabled = hasLand;
            chunk.collider.enabled = hasLand;
            if (hasLand)
                chunk.collider.sharedMesh = chunk.land;

            chunk.waterRenderer.enabled = ApplyMesh(chunk.water, waterBuffers[k]);
            chunk.triangles = (landBuffers[k].indices.Length + waterBuffers[k].indices.Length) / 3;

            landBuffers[k].Dispose();
            waterBuffers[k].Dispose();
        }
        LastApplyMs = timer.Elapsed.TotalMilliseconds;

        int total = 0;
        foreach (var chunk in chunks)
            total += chunk.triangles;
        TotalTriangles = total;
        LastRebuiltChunks = count;

        foreach (int index in dirtyChunks)
        {
            var chunk = chunks[index];
            chunk.dirty = false;

            ChunkMeshChanged?.Invoke(
                index,
                chunk.landRenderer.enabled ? chunk.land : null,
                chunk.waterRenderer.enabled ? chunk.water : null,
                chunk.root.transform.localToWorldMatrix);
        }

        dirtyChunks.Clear();
    }

    JobHandle ScheduleMesh(int3 origin, bool water, MeshBuffers buffers)
    {
        return new GreedyMeshJob
        {
            voxels = voxels,
            faceColors = faceColors,
            dims = dims,
            chunkOrigin = origin,
            chunkSize = ChunkSize,
            water = water,
            positions = buffers.positions,
            normals = buffers.normals,
            colors = buffers.colors,
            indices = buffers.indices,
        }
        .Schedule();
    }

    // Меш собирается обычными SetVertices/SetNormals/SetColors/SetIndices, как в Voxer
    static bool ApplyMesh(Mesh mesh, MeshBuffers buffers)
    {
        mesh.Clear();

        if (buffers.indices.Length == 0)
            return false;

        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(buffers.positions.AsArray());
        mesh.SetNormals(buffers.normals.AsArray());
        mesh.SetColors(buffers.colors.AsArray());
        mesh.SetIndices(buffers.indices.AsArray(), MeshTopology.Triangles, 0);

        // Границы задаём явно: по ним Unity решает, виден ли чанк камере
        mesh.bounds = new Bounds(Vector3.one * (ChunkSize * 0.5f), Vector3.one * ChunkSize);
        return true;
    }
}
