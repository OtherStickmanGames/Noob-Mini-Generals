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

        // ---------- Пехота ----------

        /// <summary>Боец выходит из казарм со стороны своих ворот. null — выйти негде (NavMesh не готов)</summary>
        public InfantryUnit SpawnInfantry(Faction faction, Barracks barracks, BarracksBehavior behavior)
        {
            var gate = GateOf(faction);
            var from = barracks != null ? barracks.Structure.ClosestEdgePoint(gate, 1f) : gate;
            return SpawnInfantryAt(faction, barracks, behavior, from);
        }

        InfantryUnit SpawnInfantryAt(Faction faction, Barracks barracks, BarracksBehavior behavior, Vector3 point)
        {
            if (!navMesh.HasNavMesh || !NavMesh.SamplePosition(point, out var hit, 4f, NavMesh.AllAreas))
                return null;

            var go = new GameObject();
            var look = GateOf(faction) - hit.position;
            look.y = 0f;
            go.transform.SetPositionAndRotation(hit.position, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);
            spawned.Add(go);

            var unit = go.AddComponent<InfantryUnit>();
            unit.Init(faction, barracks, behavior, arena.Material);
            faction.units.Add(unit);
            if (barracks != null)
                barracks.Units.Add(unit);
            return unit;
        }

        /// <summary>Мировая точка сразу снаружи ворот базы стороны</summary>
        public Vector3 GateOf(Faction faction) => faction.team == 0 ? arena.GateOne : arena.GateTwo;

        /// <summary>
        /// Пост защитника: ряды за воротами внутри стен, лицом к воротам. Место в строю — порядковый
        /// номер бойца среди защитников его стороны, поэтому строй сам уплотняется.
        /// </summary>
        public Vector3 DefendPost(InfantryUnit unit)
        {
            var faction = unit.Faction;
            int index = 0, slot = 0;
            foreach (var u in faction.units)
            {
                if (u == null || u.Behavior != BarracksBehavior.Defend)
                    continue;
                if (u == unit)
                    index = slot;
                slot++;
            }

            var gate = GateOf(faction);
            var center = faction.team == 0 ? arena.BaseOne : arena.BaseTwo;
            var inward = center - gate;
            inward.y = 0f;
            inward.Normalize();
            var side = new Vector3(inward.z, 0f, -inward.x);

            const int perRow = 6;
            const float spacing = 1.3f;
            int row = index / perRow, column = index % perRow;
            float lateral = (column - (perRow - 1) * 0.5f) * spacing;
            return gate + inward * (5f + row * spacing) + side * lateral;
        }

        // ---------- Отладка ----------

        /// <summary>Отряд противника в атаке у его ворот (пока нет ИИ противника — шаг 6)</summary>
        public void DebugSpawnEnemySquad(int count)
        {
            var gate = GateOf(Enemy);
            var center = arena.BaseTwo;
            var outward = gate - center;
            outward.y = 0f;
            outward.Normalize();
            var side = new Vector3(outward.z, 0f, -outward.x);
            for (int i = 0; i < count; i++)
                SpawnInfantryAt(Enemy, null, BarracksBehavior.Attack, gate + outward * 2f + side * ((i - (count - 1) * 0.5f) * 1.2f));
        }

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
