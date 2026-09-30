using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Generals
{
    /// <summary>
    /// Участок стены базы (квадрат клеток WallSegmentCells × WallSegmentCells) как цель: у него
    /// прочность, он крошится под обстрелом (выбиваются его воксели в арене у точки попадания),
    /// а при нуле прочности обрушается целиком — остаётся пролом, NavMesh пересобирается сам.
    /// Сама стена — воксели арены; этот объект только хранит, какие воксели его.
    /// </summary>
    public class WallSegment : MonoBehaviour, IAreaTarget
    {
        /// <summary>Доля вокселей участка, выбитых к нулю прочности (дальше обрушение)</summary>
        const float MaxChippedShare = 0.45f;
        const int MaxDebrisPerHit = 10;
        const int MaxCollapseDebris = 90;

        public Faction Faction { get; private set; }
        public float Health { get; private set; }
        public float MaxHealth => StructureCatalog.WallSegmentHealth;
        public bool IsAlive => Health > 0f;
        public float LastDamageTime { get; private set; } = float.NegativeInfinity;
        public Vector3 Velocity => Vector3.zero;
        public Vector2 HalfExtents { get; private set; }
        public float Height { get; private set; }
        /// <summary>Клетки земли участка</summary>
        public IReadOnlyList<int2> Cells => cells;

        VoxelArena arena;
        readonly List<int2> cells = new();
        // Воксели стены этого участка (проверяются по арене: часть могла выбить воронка)
        readonly List<int3> voxels = new();
        readonly List<(float distance, int index)> candidates = new();
        readonly List<int3> toClear = new();
        int initialCount;
        int chipped;

        public void Init(Faction faction, VoxelArena arena, List<int2> segmentCells)
        {
            Faction = faction;
            this.arena = arena;
            cells.AddRange(segmentCells);
            Health = MaxHealth;

            int2 min = new(int.MaxValue), max = new(int.MinValue);
            int ground = int.MaxValue, top = 0;
            var dims = arena.Dims;
            foreach (var cell in cells)
            {
                min = math.min(min, cell);
                max = math.max(max, cell);
                for (int y = 1; y < dims.y; y++)
                {
                    var v = new int3(cell.x, y, cell.y);
                    if (arena.GetBlock(v) != VoxelBlocks.Wall)
                        continue;
                    voxels.Add(v);
                    ground = math.min(ground, y);
                    top = math.max(top, y + 1);
                }
            }
            initialCount = voxels.Count;
            if (ground == int.MaxValue)
                ground = arena.SurfaceY(min.x, min.y);

            float size = arena.VoxelSize;
            var center = (float2)(min + max + 1) * 0.5f;
            transform.position = arena.VoxelToWorld(new float3(center.x, ground, center.y));
            HalfExtents = new Vector2(max.x - min.x + 1, max.y - min.y + 1) * size * 0.5f;
            Height = Mathf.Max(1, top - ground) * size;
            name = $"Стена {faction.team} ({min.x}, {min.y})";
        }

        public void TakeDamage(float amount, Vector3 point, Vector3 direction)
        {
            if (!IsAlive)
                return;

            Health -= amount;
            LastDamageTime = Time.time;
            var effects = MatchManager.Instance.Effects;
            if (Health > 0f)
            {
                Chip(point, direction, effects);
                return;
            }

            Health = 0f;
            Collapse(effects);
            MatchManager.Instance.WallSegmentDestroyed(this);
            Destroy(gameObject);
        }

        // Выбить воксели участка, ближайшие к попаданию, — сколько положено по снятой прочности
        void Chip(Vector3 point, Vector3 direction, Effects effects)
        {
            int target = Mathf.FloorToInt(initialCount * MaxChippedShare * (1f - Health / MaxHealth));
            int count = target - chipped;
            if (count <= 0)
                return;

            var local = arena.WorldToVoxel(point);
            candidates.Clear();
            for (int i = 0; i < voxels.Count; i++)
            {
                if (arena.GetBlock(voxels[i]) != VoxelBlocks.Wall)
                    continue;
                candidates.Add((math.distancesq((float3)voxels[i] + 0.5f, local), i));
            }
            candidates.Sort((a, b) => a.distance.CompareTo(b.distance));

            var back = direction.sqrMagnitude > 1e-6f ? -direction.normalized : Vector3.up;
            toClear.Clear();
            count = Mathf.Min(count, candidates.Count);
            for (int k = 0; k < count; k++)
            {
                var v = voxels[candidates[k].index];
                toClear.Add(v);
                if (k < MaxDebrisPerHit)
                    effects.WallDebris(VoxelCenter(v), back * Random.Range(1.5f, 3.5f) + Random.insideUnitSphere + Vector3.up * 2f);
            }
            chipped += count;
            arena.ClearVoxels(toClear);
        }

        // Обрушение: все оставшиеся воксели участка выбиваются, куски разлетаются, поднимается пыль
        void Collapse(Effects effects)
        {
            toClear.Clear();
            foreach (var v in voxels)
                if (arena.GetBlock(v) == VoxelBlocks.Wall)
                    toClear.Add(v);

            int step = Mathf.Max(1, Mathf.CeilToInt(toClear.Count / (float)MaxCollapseDebris));
            var center = transform.position + Vector3.up * (Height * 0.3f);
            for (int i = 0; i < toClear.Count; i += step)
            {
                var p = VoxelCenter(toClear[i]);
                var outward = p - center;
                outward.y = Mathf.Max(0f, outward.y);
                effects.WallDebris(p, outward.normalized * Random.Range(0.5f, 2.5f) + Vector3.up * Random.Range(0.5f, 3f));
            }
            effects.Dust(transform.position, HalfExtents, Height);
            arena.ClearVoxels(toClear);
        }

        Vector3 VoxelCenter(int3 v) => arena.VoxelToWorld((float3)v + 0.5f);

        // ---------- Цель ----------

        public Vector3 AimPoint(Vector3 from) => Combat.AreaAimPoint(this, from, Height);

        public Vector3 MissPoint(Vector3 from) => Combat.AreaMissPoint(this, from);

        public Vector3 ClosestSurfacePoint(Vector3 from) => Combat.AreaSurfacePoint(this, from, Height);

        public Vector3 ClosestEdgePoint(Vector3 from, float margin) =>
            Combat.RectEdgePoint(transform.position, HalfExtents, from, margin);

        public float DistanceTo(Vector3 point) => Combat.RectDistance(transform.position, HalfExtents, point);
    }
}
