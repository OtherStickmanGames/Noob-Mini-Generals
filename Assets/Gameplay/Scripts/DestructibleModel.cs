using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Generals
{
    /// <summary>
    /// Воксельная модель здания, которая крошится под обстрелом. У каждого здания своя копия вокселей
    /// и свой меш. Сколько вокселей выбито, задаёт снятая прочность: к нулю выбита доля
    /// MaxChippedShare модели, дальше здание рушится целиком (это решает Structure).
    /// Выбиваются воксели ближе всего к точке попадания; куски, потерявшие связь с фундаментом, падают.
    /// Фундамент (нижний слой) не выбивается — здание до конца стоит на земле.
    /// </summary>
    public class DestructibleModel
    {
        /// <summary>Доля вокселей, выбитых к нулю прочности</summary>
        public const float MaxChippedShare = 0.35f;
        const int MaxDebrisPerHit = 24;
        const int MaxFallingDebris = 80;
        const int MaxShatterDebris = 170;

        readonly VoxelModels.Model model;
        readonly Transform modelTransform;
        readonly Mesh mesh;
        readonly int initialSolid;
        int removed;
        bool dirty;

        readonly List<(float distance, int index)> candidates = new();
        readonly bool[] connected;
        readonly Queue<int> queue = new();

        public DestructibleModel(VoxelModels.Model model, Transform modelTransform, MeshFilter filter, string name)
        {
            this.model = model;
            this.modelTransform = modelTransform;
            mesh = model.ToMesh(name);
            filter.sharedMesh = mesh;
            connected = new bool[model.Voxels.Length];
            foreach (var v in model.Voxels)
                if (v != 0)
                    initialSolid++;
        }

        public Mesh Mesh => mesh;
        public int3 Size => model.Size;

        public void Dispose()
        {
            Object.Destroy(mesh);
        }

        /// <summary>
        /// Попадание: здание потеряло долю прочности lostShare (0..1, от максимума, всего с начала боя).
        /// Выбивает недостающие воксели вокруг worldPoint; direction — куда летел снаряд.
        /// </summary>
        public void Damage(Vector3 worldPoint, Vector3 direction, float lostShare, Effects effects)
        {
            int target = Mathf.FloorToInt(initialSolid * MaxChippedShare * Mathf.Clamp01(lostShare));
            int count = target - removed;
            if (count <= 0)
                return;

            // Координаты модели — в вокселях (у объекта модели масштаб — размер вокселя)
            float3 local = (Vector3)modelTransform.InverseTransformPoint(worldPoint);
            var voxels = model.Voxels;
            candidates.Clear();
            for (int i = 0; i < voxels.Length; i++)
            {
                if (voxels[i] == 0)
                    continue;
                var c = model.Coord(i);
                if (c.y == 0)
                    continue;
                candidates.Add((math.distancesq((float3)c + 0.5f, local), i));
            }
            candidates.Sort((a, b) => a.distance.CompareTo(b.distance));

            // Обломки летят навстречу снаряду и вверх
            var back = direction.sqrMagnitude > 1e-6f ? -direction.normalized : Vector3.up;
            count = Mathf.Min(count, candidates.Count);
            for (int k = 0; k < count; k++)
            {
                int i = candidates[k].index;
                if (k < MaxDebrisPerHit)
                {
                    var velocity = back * Random.Range(1.5f, 3.5f) + Random.insideUnitSphere * 1.5f + Vector3.up * 2f;
                    effects.VoxelDebris(VoxelWorld(i), velocity, Slot(voxels[i]));
                }
                voxels[i] = 0;
                removed++;
            }

            DropFloating(effects);
            dirty = true;
        }

        // Всё, что не связано с фундаментом по граням, падает
        void DropFloating(Effects effects)
        {
            var voxels = model.Voxels;
            var size = model.Size;
            System.Array.Clear(connected, 0, connected.Length);
            queue.Clear();

            for (int z = 0; z < size.z; z++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    int i = model.Index(x, 0, z);
                    if (voxels[i] != 0)
                    {
                        connected[i] = true;
                        queue.Enqueue(i);
                    }
                }
            }

            while (queue.Count > 0)
            {
                var c = model.Coord(queue.Dequeue());
                Visit(c.x + 1, c.y, c.z);
                Visit(c.x - 1, c.y, c.z);
                Visit(c.x, c.y + 1, c.z);
                Visit(c.x, c.y - 1, c.z);
                Visit(c.x, c.y, c.z + 1);
                Visit(c.x, c.y, c.z - 1);
            }

            int emitted = 0;
            for (int i = 0; i < voxels.Length; i++)
            {
                if (voxels[i] == 0 || connected[i])
                    continue;
                if (emitted++ < MaxFallingDebris)
                    effects.VoxelDebris(VoxelWorld(i), Random.insideUnitSphere * 1.2f + Vector3.down, Slot(voxels[i]));
                voxels[i] = 0;
                removed++;
            }
        }

        void Visit(int x, int y, int z)
        {
            var size = model.Size;
            if (x < 0 || y < 0 || z < 0 || x >= size.x || y >= size.y || z >= size.z)
                return;
            int i = model.Index(x, y, z);
            if (connected[i] || model.Voxels[i] == 0)
                return;
            connected[i] = true;
            queue.Enqueue(i);
        }

        /// <summary>Здание рухнуло: оставшиеся воксели разлетаются обломками своих цветов</summary>
        public void Shatter(Effects effects)
        {
            var voxels = model.Voxels;
            int solid = 0;
            foreach (var v in voxels)
                if (v != 0)
                    solid++;
            if (solid == 0)
                return;

            // Каждый step-й воксель, чтобы не превысить лимит частиц
            int step = Mathf.Max(1, Mathf.CeilToInt(solid / (float)MaxShatterDebris));
            var center = modelTransform.TransformPoint((float3)model.Size * new float3(0.5f, 0.3f, 0.5f));
            int n = 0;
            for (int i = 0; i < voxels.Length; i++)
            {
                if (voxels[i] == 0 || n++ % step != 0)
                    continue;
                var position = VoxelWorld(i);
                var outward = position - center;
                outward.y = Mathf.Max(outward.y, 0f);
                var velocity = outward.normalized * Random.Range(1f, 4f) + Vector3.up * Random.Range(1.5f, 5f);
                effects.VoxelDebris(position, velocity, Slot(voxels[i]));
            }
        }

        /// <summary>Пересобрать меш, если за кадр что-то выбили (все попадания кадра — одной пересборкой)</summary>
        public void RebuildIfDirty()
        {
            if (!dirty)
                return;
            dirty = false;
            model.WriteMesh(mesh);
        }

        Vector3 VoxelWorld(int index) => modelTransform.TransformPoint((float3)model.Coord(index) + 0.5f);

        static byte Slot(byte block) => VoxelBlocks.FaceSlot(block, VoxelBlocks.FaceSide);
    }
}
