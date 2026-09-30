using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

namespace Generals
{
    /// <summary>
    /// Бой: две стороны, стартовые здания (главное и добытчик рядом с ним), строители, точки захвата,
    /// заказ построек и найм строителей.
    /// </summary>
    public class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [SerializeField] VoxelArena arena;
        [SerializeField] ArenaNavMesh navMesh;

        public VoxelArena Arena => arena;
        public Faction Player { get; private set; }
        public Faction Enemy { get; private set; }
        public BuildGrid Grid { get; private set; }
        public IReadOnlyList<CapturePoint> CapturePoints => capturePoints;

        /// <summary>Материал зданий: копия материала арены, при установке здания становится полупрозрачным</summary>
        public Material StructureMaterial
        {
            get
            {
                if (structureMaterial == null)
                {
                    structureMaterial = BuildModeVisuals.CreateVariant(arena.Material, "Structures");
                }
                return structureMaterial;
            }
        }

        Material structureMaterial;

        readonly List<CapturePoint> capturePoints = new();
        readonly List<GameObject> spawned = new();
        bool buildersSpawned;

        void Awake()
        {
            Instance = this;
            arena.Generated += Arena_Generated;
        }

        void OnDestroy()
        {
            arena.Generated -= Arena_Generated;
        }

        public Faction GetFaction(int team) => team == 0 ? Player : Enemy;

        void Arena_Generated()
        {
            foreach (var go in spawned)
                Destroy(go);
            spawned.Clear();
            capturePoints.Clear();
            CapturePoint.Capturers.Clear();

            Player = new Faction(0, true);
            Enemy = new Faction(1, false);
            Grid = new BuildGrid(arena);
            buildersSpawned = false;

            var layout = arena.Layout;
            foreach (var r in layout.resources)
            {
                if (r.kind != ResourceKind.CapturePoint)
                    continue;
                var go = new GameObject();
                spawned.Add(go);
                var point = go.AddComponent<CapturePoint>();
                // Кольцо точки захвата — 7x7 вокруг центра
                point.Init(r.cell, CellCenter(r.cell - 3, new int2(7, 7)), arena.Material);
                capturePoints.Add(point);
            }

            foreach (var faction in new[] { Player, Enemy })
            {
                var baseCell = faction.team == 0 ? layout.baseOne : layout.baseTwo;
                var hqDef = StructureCatalog.Get(StructureType.Headquarters);
                faction.headquarters = PlaceStructure(faction, hqDef, BuildGrid.MinFromCenter(hqDef, baseCell), true, null);

                // Стартовый добытчик — рядом с главным зданием, внутри стен
                var extractorDef = StructureCatalog.Get(StructureType.Extractor);
                if (Grid.FindNearest(faction, extractorDef, baseCell, 12, out var extractorMin))
                    PlaceStructure(faction, extractorDef, extractorMin, true, null);
            }
        }

        void Update()
        {
            if (Player == null)
                return;

            // Строители появляются, когда готов навмеш
            if (!buildersSpawned && navMesh.HasNavMesh && !navMesh.IsBusy)
            {
                buildersSpawned = true;
                SpawnBuilder(Player);
                SpawnBuilder(Enemy);
            }

            if (buildersSpawned)
            {
                UpdateHiring(Player);
                UpdateHiring(Enemy);
            }
        }

        // ---------- Здания ----------

        /// <summary>Заказ постройки: проверка места и цены, оплата, закладка стройки</summary>
        public bool TryOrderConstruction(Faction faction, StructureDef def, int2 min, out string reason)
        {
            if (!Grid.CanPlace(faction, def, min, out reason, out var capturePoint))
                return false;

            if (!faction.CanAfford(def.costBase, def.costValuable))
            {
                reason = "Не хватает ресурсов";
                return false;
            }

            faction.Pay(def.costBase, def.costValuable);
            PlaceStructure(faction, def, min, false, capturePoint);
            return true;
        }

        public Structure PlaceStructure(Faction faction, StructureDef def, int2 min, bool built, CapturePoint capturePoint)
        {
            var go = new GameObject();
            spawned.Add(go);
            var structure = go.AddComponent<Structure>();

            // Главное здание и турель смотрят входом к противнику, остальные — по осям карты
            float rotation = 0f;
            if (def.type == StructureType.Headquarters || def.type == StructureType.Turret)
                rotation = GateRotation(faction.team);

            structure.Init(def, faction, min, CellCenter(min, def.footprint), arena.VoxelSize,
                           rotation, built, capturePoint, StructureMaterial);

            faction.structures.Add(structure);
            Grid.SetOccupied(min, def.footprint, true);
            if (capturePoint != null)
                capturePoint.Mine = structure;
            return structure;
        }

        float GateRotation(int team)
        {
            int side = (arena.Layout.gateSide + (team == 0 ? 0 : 2)) % 4;
            return side * 90f;
        }

        /// <summary>
        /// Мировой центр прямоугольника клеток. Высота — по самому низкому углу:
        /// в центре точки захвата торчит рудный бугор.
        /// </summary>
        public Vector3 CellCenter(int2 min, int2 size)
        {
            var max = min + size - 1;
            int y = math.min(math.min(arena.SurfaceY(min.x, min.y), arena.SurfaceY(max.x, min.y)),
                             math.min(arena.SurfaceY(min.x, max.y), arena.SurfaceY(max.x, max.y)));
            float2 c = (float2)min + (float2)size * 0.5f;
            return arena.VoxelToWorld(new float3(c.x, y, c.y));
        }

        /// <summary>Строитель берёт ближайшую незанятую стройку своей стороны</summary>
        public Structure ClaimSite(BuilderUnit builder)
        {
            Structure best = null;
            float bestDistance = float.MaxValue;
            foreach (var s in builder.Faction.structures)
            {
                if (s.IsBuilt || s.AssignedBuilder != null)
                    continue;
                float d = s.DistanceTo(builder.transform.position);
                if (d < bestDistance)
                {
                    best = s;
                    bestDistance = d;
                }
            }

            if (best != null)
                best.AssignedBuilder = builder;
            return best;
        }

        public CapturePoint CapturePointAt(int2 cell)
        {
            foreach (var p in capturePoints)
                if (p.Cell.Equals(cell))
                    return p;
            return null;
        }

        // ---------- Строители ----------

        public bool TryHireBuilder(Faction faction, out string reason)
        {
            if (!faction.CanAfford(StructureCatalog.BuilderCost, 0))
            {
                reason = "Не хватает ресурсов";
                return false;
            }

            faction.Pay(StructureCatalog.BuilderCost, 0);
            faction.buildersQueued++;
            reason = null;
            return true;
        }

        void UpdateHiring(Faction faction)
        {
            if (faction.buildersQueued == 0 || faction.headquarters == null)
                return;

            faction.hireProgress += Time.deltaTime / StructureCatalog.BuilderHireTime;
            if (faction.hireProgress < 1f)
                return;

            faction.hireProgress = 0f;
            faction.buildersQueued--;
            SpawnBuilder(faction);
        }

        void SpawnBuilder(Faction faction)
        {
            // Перед входом главного здания, со стороны ворот
            var hq = faction.headquarters;
            var forward = Quaternion.Euler(0f, GateRotation(faction.team), 0f) * Vector3.forward;
            var point = hq.transform.position + forward * (hq.HalfExtents.y + 1.5f);

            if (!NavMesh.SamplePosition(point, out var hit, 4f, NavMesh.AllAreas))
                return;

            var go = new GameObject();
            go.transform.SetPositionAndRotation(hit.position, Quaternion.LookRotation(forward));
            spawned.Add(go);
            var builder = go.AddComponent<BuilderUnit>();
            builder.Init(faction, arena.Material);
            faction.builders.Add(builder);
        }

        // ---------- Отладка ----------

        /// <summary>Отдать игроку ближайшую к точке точку захвата (пока нет боевых юнитов)</summary>
        public void DebugCaptureNearest(Vector3 position, int team)
        {
            CapturePoint best = null;
            float bestDistance = float.MaxValue;
            foreach (var p in capturePoints)
            {
                float d = Vector3.Distance(p.transform.position, position);
                if (d < bestDistance)
                {
                    best = p;
                    bestDistance = d;
                }
            }
            best?.SetOwner(team);
        }
    }
}
