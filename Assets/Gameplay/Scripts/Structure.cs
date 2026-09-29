using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

namespace Generals
{
    /// <summary>
    /// Здание: занимает прямоугольник клеток земли, строится строителем (поднимается из земли),
    /// после постройки приносит доход. Вырезает себя из навмеша.
    /// </summary>
    public class Structure : MonoBehaviour
    {
        public StructureDef Def { get; private set; }
        public Faction Faction { get; private set; }
        public int2 MinCell { get; private set; }
        public bool IsBuilt { get; private set; }
        public float Progress { get; private set; }
        public float Health { get; private set; }
        /// <summary>Для шахты на точке захвата</summary>
        public CapturePoint CapturePoint { get; private set; }
        public BuilderUnit AssignedBuilder { get; set; }

        /// <summary>Половина размера в мировых единицах по x и z</summary>
        public Vector2 HalfExtents { get; private set; }

        Transform holder;
        float modelHeight;

        public void Init(StructureDef def, Faction faction, int2 minCell, Vector3 center, float cellSize,
                         float rotationY, bool built, CapturePoint capturePoint, Material material)
        {
            Def = def;
            Faction = faction;
            MinCell = minCell;
            CapturePoint = capturePoint;
            Health = def.health;
            HalfExtents = new Vector2(def.footprint.x, def.footprint.y) * cellSize * 0.5f;

            name = $"{def.name} {faction.team}";
            transform.position = center;

            var mesh = VoxelModels.Structure(def.type, faction.team);
            var size = VoxelModels.Size(mesh);
            modelHeight = size.y * VoxelModels.VoxelSize;

            // Держатель поворачивается, модель внутри сдвинута так, чтобы центр был в нуле
            holder = new GameObject("Holder").transform;
            holder.SetParent(transform, false);
            holder.localRotation = Quaternion.Euler(0f, rotationY, 0f);

            var model = new GameObject("Model");
            model.transform.SetParent(holder, false);
            model.transform.localScale = Vector3.one * VoxelModels.VoxelSize;
            model.transform.localPosition = new Vector3(-size.x, 0f, -size.z) * (VoxelModels.VoxelSize * 0.5f);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterial = material;

            // Коллайдер для тапа по зданию
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, modelHeight * 0.5f, 0f);
            box.size = new Vector3(HalfExtents.x * 2f, modelHeight, HalfExtents.y * 2f);

            // Здание вырезает себя из навмеша
            var obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = box.center;
            obstacle.size = box.size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            if (built)
                Complete();
            else
                UpdateModel();
        }

        /// <summary>Строитель работает над зданием seconds секунд</summary>
        public void AddWork(float seconds)
        {
            if (IsBuilt)
                return;

            Progress = math.saturate(Progress + seconds / Def.buildTime);
            if (Progress >= 1f)
                Complete();
            else
                UpdateModel();
        }

        void Complete()
        {
            IsBuilt = true;
            Progress = 1f;
            UpdateModel();
        }

        // Недостроенное здание утоплено в землю и поднимается по мере работы
        void UpdateModel()
        {
            float sink = modelHeight * (1f - math.lerp(0.15f, 1f, Progress));
            holder.localPosition = new Vector3(0f, -sink, 0f);
        }

        void Update()
        {
            if (!IsBuilt)
                return;

            float dt = Time.deltaTime;
            switch (Def.type)
            {
                case StructureType.Headquarters:
                    Faction.baseResource += StructureCatalog.HeadquartersIncome * dt;
                    break;

                case StructureType.Extractor:
                    Faction.baseResource += StructureCatalog.ExtractorIncome * dt;
                    break;

                case StructureType.Mine:
                    if (CapturePoint.Owner == Faction.team)
                        Faction.valuable += StructureCatalog.MineIncome * dt;
                    break;
            }
        }

        /// <summary>Ближайшая к точке позиция на краю здания с отступом</summary>
        public Vector3 ClosestEdgePoint(Vector3 from, float margin)
        {
            var c = transform.position;
            float hx = HalfExtents.x + margin, hz = HalfExtents.y + margin;
            var p = new Vector3(Mathf.Clamp(from.x, c.x - hx, c.x + hx), c.y, Mathf.Clamp(from.z, c.z - hz, c.z + hz));

            // Точка внутри прямоугольника — выталкиваем на ближайшую сторону
            float dx = hx - Mathf.Abs(p.x - c.x), dz = hz - Mathf.Abs(p.z - c.z);
            if (dx > 0f && dz > 0f)
            {
                if (dx < dz)
                    p.x = c.x + Mathf.Sign(p.x - c.x + 0.0001f) * hx;
                else
                    p.z = c.z + Mathf.Sign(p.z - c.z + 0.0001f) * hz;
            }
            return p;
        }

        /// <summary>Расстояние по горизонтали от точки до прямоугольника здания</summary>
        public float DistanceTo(Vector3 point)
        {
            var c = transform.position;
            float dx = Mathf.Max(0f, Mathf.Abs(point.x - c.x) - HalfExtents.x);
            float dz = Mathf.Max(0f, Mathf.Abs(point.z - c.z) - HalfExtents.y);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
