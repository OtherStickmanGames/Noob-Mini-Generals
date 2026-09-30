using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;
using UnityEngine.AI;

namespace Generals
{
    /// <summary>
    /// Здание: занимает прямоугольник клеток земли, строится строителем (поднимается из земли),
    /// после постройки приносит доход. Вырезает себя из навмеша. Получает урон; при нуле прочности
    /// рушится (MatchManager.StructureDestroyed освобождает клетки, главное здание решает исход боя).
    /// </summary>
    public class Structure : MonoBehaviour, IAreaTarget
    {
        public StructureDef Def { get; private set; }
        public Faction Faction { get; private set; }
        public int2 MinCell { get; private set; }
        public bool IsBuilt { get; private set; }
        public float Progress { get; private set; }
        public float Health { get; private set; }
        public float MaxHealth => Def.health;
        public bool IsAlive => Health > 0f;
        public float LastDamageTime { get; private set; } = float.NegativeInfinity;
        public Vector3 Velocity => Vector3.zero;
        /// <summary>Для шахты на точке захвата</summary>
        public CapturePoint CapturePoint { get; private set; }
        public BuilderUnit AssignedBuilder { get; set; }
        /// <summary>Очередь найма и поведение бойцов — только у казарм</summary>
        public Barracks Barracks { get; private set; }
        /// <summary>Поворотная башня — только у турели</summary>
        public Turret Turret { get; private set; }

        /// <summary>Высота модели здания, м</summary>
        public float Height => modelHeight;

        /// <summary>Половина размера в мировых единицах по x и z</summary>
        public Vector2 HalfExtents { get; private set; }

        Transform holder;
        BoxCollider box;
        float modelHeight;
        DestructibleModel destructible;

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

            // Высота — по модели целиком; у турели на основании потом встаёт поворотная башня
            modelHeight = VoxelModels.Size(VoxelModels.Structure(def.type, faction.team)).y * VoxelModels.VoxelSize;
            // Своя копия вокселей: от попаданий здание крошится
            var body = VoxelModels.StructureBody(def.type, faction.team);
            var size = body.Size;

            // Держатель поворачивается, модель внутри сдвинута так, чтобы центр был в нуле
            holder = new GameObject("Holder").transform;
            holder.SetParent(transform, false);
            holder.localRotation = Quaternion.Euler(0f, rotationY, 0f);

            var model = new GameObject("Model");
            model.transform.SetParent(holder, false);
            model.transform.localScale = Vector3.one * VoxelModels.VoxelSize;
            model.transform.localPosition = new Vector3(-size.x, 0f, -size.z) * (VoxelModels.VoxelSize * 0.5f);
            destructible = new DestructibleModel(body, model.transform, model.AddComponent<MeshFilter>(), name);
            model.AddComponent<MeshRenderer>().sharedMaterial = material;

            // Коллайдер для тапа по зданию и попаданий
            box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, modelHeight * 0.5f, 0f);
            box.size = new Vector3(HalfExtents.x * 2f, modelHeight, HalfExtents.y * 2f);

            // Здание вырезает себя из навмеша
            var obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = box.center;
            obstacle.size = box.size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            if (def.type == StructureType.Barracks)
            {
                Barracks = gameObject.AddComponent<Barracks>();
                Barracks.Init(this);
            }
            else if (def.type == StructureType.Turret)
            {
                Turret = gameObject.AddComponent<Turret>();
                Turret.Init(this, holder, material);
            }

            if (built)
                Complete();
            else
                UpdateModel();
        }

        /// <summary>Строитель работает над зданием seconds секунд</summary>
        public void AddWork(float seconds)
        {
            if (IsBuilt || !IsAlive)
                return;

            Progress = math.saturate(Progress + seconds / Def.buildTime);
            if (Progress >= 1f)
            {
                Complete();
                Faction.structuresBuilt++;
            }
            else
            {
                UpdateModel();
            }
        }

        void Complete()
        {
            IsBuilt = true;
            Progress = 1f;
            UpdateModel();
        }

        // Недостроенное здание утоплено в землю и поднимается по мере работы;
        // коллайдер — только над землёй, чтобы пули не попадали в пустоту над стройкой
        void UpdateModel()
        {
            float sink = modelHeight * (1f - math.lerp(0.15f, 1f, Progress));
            holder.localPosition = new Vector3(0f, -sink, 0f);

            float visible = modelHeight - sink;
            box.center = new Vector3(0f, visible * 0.5f, 0f);
            box.size = new Vector3(HalfExtents.x * 2f, visible, HalfExtents.y * 2f);
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

        // Выбитые за кадр воксели — одной пересборкой меша
        void LateUpdate()
        {
            destructible.RebuildIfDirty();
        }

        void OnDestroy()
        {
            destructible?.Dispose();
        }

        // ---------- Бой ----------

        public void TakeDamage(float amount, Vector3 point, Vector3 direction)
        {
            if (!IsAlive)
                return;

            Health -= amount;
            LastDamageTime = Time.time;
            var effects = MatchManager.Instance.Effects;
            if (Health > 0f)
            {
                // Крошится там, куда попали: чем меньше прочности, тем больше выбито
                destructible.Damage(point, direction, 1f - Health / MaxHealth, effects);
                return;
            }

            Health = 0f;
            MatchManager.Instance.StructureDestroyed(this);
            effects.StructureDestroyed(transform.position, HalfExtents, box.size.y);
            destructible.Shatter(effects);
            Destroy(gameObject);
        }

        /// <summary>
        /// Куда целиться: случайный целый воксель со стороны стрелка; у стройки (модель ещё
        /// в земле) — ближний край на уровне груди
        /// </summary>
        public Vector3 AimPoint(Vector3 from) =>
            IsBuilt && destructible.TryPickAimPoint(from, out var point) ? point : Combat.AreaAimPoint(this, from, box.size.y);

        /// <summary>Промах — в землю у стены здания со стороны стрелка, чуть вбок</summary>
        public Vector3 MissPoint(Vector3 from) => Combat.AreaMissPoint(this, from);

        public Vector3 ClosestSurfacePoint(Vector3 from) => Combat.AreaSurfacePoint(this, from, box.size.y);

        // ---------- Геометрия ----------

        /// <summary>Ближайшая к точке позиция на краю здания с отступом</summary>
        public Vector3 ClosestEdgePoint(Vector3 from, float margin) =>
            Combat.RectEdgePoint(transform.position, HalfExtents, from, margin);

        /// <summary>Расстояние по горизонтали от точки до прямоугольника здания</summary>
        public float DistanceTo(Vector3 point) => Combat.RectDistance(transform.position, HalfExtents, point);
    }
}
