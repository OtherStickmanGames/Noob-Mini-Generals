using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;
using UnityEngine.AI;
using Unity.Profiling;

namespace Generals
{
    /// <summary>
    /// Бой: две стороны, стартовые здания (главное и добытчик рядом с ним), строители, точки захвата,
    /// заказ построек и найм строителей. Разрушение зданий и исход боя: чьё главное здание
    /// разрушено, тот проиграл.
    /// </summary>
    public class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [SerializeField] VoxelArena arena;
        [SerializeField] ArenaNavMesh navMesh;
        [Tooltip("Скриптовый ИИ противника; настройки можно менять в Play mode")]
        [SerializeField] EnemyAiSettings enemyAi = new();

        public EnemyAI EnemyAI { get; private set; }

        public VoxelArena Arena => arena;
        public ArenaNavMesh ArenaNav => navMesh;
        public Faction Player { get; private set; }
        public Faction Enemy { get; private set; }
        public BuildGrid Grid { get; private set; }
        public IReadOnlyList<CapturePoint> CapturePoints => capturePoints;
        public Projectiles Projectiles { get; private set; }
        public Effects Effects { get; private set; }

        /// <summary>Бой окончен: чьё-то главное здание разрушено</summary>
        public bool IsOver => Winner != null;
        public Faction Winner { get; private set; }
        /// <summary>Где стояло разрушенное главное здание — туда едет камера в конце боя</summary>
        public Vector3 EndPoint { get; private set; }
        /// <summary>Секунды боя (после конца не растут)</summary>
        public float MatchTime { get; private set; }

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
            gameObject.AddComponent<PhysicsQueries>();
            Effects = gameObject.AddComponent<Effects>();
            Projectiles = gameObject.AddComponent<Projectiles>();
            Projectiles.Init(arena, Effects);
            slotPath = new NavMeshPath();
            EnemyAI = gameObject.AddComponent<EnemyAI>();
            EnemyAI.Init(this, enemyAi);
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
            Projectiles.Clear();
            Effects.Clear();

            Player = new Faction(0, true);
            Enemy = new Faction(1, false);
            Grid = new BuildGrid(arena);
            buildersSpawned = false;
            Winner = null;
            MatchTime = 0f;

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

            CreateWallSegments();
            EnemyAI.ResetForMatch(Enemy, Player);
        }

        // ---------- Стены ----------

        readonly Dictionary<int2, WallSegment> wallByCell = new();

        // Стены баз режутся на участки-квадраты: у каждого своя прочность, по ним стреляют
        void CreateWallSegments()
        {
            wallByCell.Clear();
            attackSlots.Clear();
            var layout = arena.Layout;
            var groups = new Dictionary<int3, List<int2>>();
            foreach (var wall in layout.walls)
            {
                int team = layout.baseArea[wall.cell.y * layout.sizeX + wall.cell.x] - 1;
                if (team < 0)
                    continue;
                var key = new int3(wall.cell / StructureCatalog.WallSegmentCells, team);
                if (!groups.TryGetValue(key, out var cells))
                    groups[key] = cells = new List<int2>();
                cells.Add(wall.cell);
            }

            foreach (var (key, cells) in groups)
            {
                var go = new GameObject();
                spawned.Add(go);
                var segment = go.AddComponent<WallSegment>();
                var faction = GetFaction(key.z);
                segment.Init(faction, arena, cells);
                faction.walls.Add(segment);
                foreach (var cell in cells)
                    wallByCell[cell] = segment;
            }
        }

        /// <summary>Живой участок стены, которому принадлежит воксель стены в точке; иначе null</summary>
        public WallSegment WallSegmentAt(Vector3 world)
        {
            var voxel = (int3)math.floor(arena.WorldToVoxel(world));
            if (arena.GetBlock(voxel) != VoxelBlocks.Wall)
                return null;
            return wallByCell.TryGetValue(voxel.xz, out var segment) && segment != null && segment.IsAlive ? segment : null;
        }

        /// <summary>Участок стены обрушился (вызывает сам участок перед уничтожением)</summary>
        public void WallSegmentDestroyed(WallSegment segment)
        {
            // Пролом — новый проход: NavMesh сразу, не дожидаясь накопления воронок
            navMesh.RequestUrgentRebuild();
            segment.Faction.walls.Remove(segment);
            foreach (var cell in segment.Cells)
                if (wallByCell.TryGetValue(cell, out var s) && s == segment)
                    wallByCell.Remove(cell);
            attackSlots.Remove(segment);

            // Турель на этом участке стены падает вместе с ним
            var cells = new HashSet<int2>(segment.Cells);
            foreach (var structure in segment.Faction.structures.ToArray())
            {
                if (structure.Def.type != StructureType.Turret || !structure.IsAlive)
                    continue;
                bool onSegment = false;
                for (int z = 0; z < structure.Def.footprint.y && !onSegment; z++)
                    for (int x = 0; x < structure.Def.footprint.x && !onSegment; x++)
                        onSegment = cells.Contains(structure.MinCell + new int2(x, z));
                if (onSegment)
                    structure.TakeDamage(structure.Health + 1f, structure.transform.position, Vector3.down);
            }
        }

        // ---------- Цели атаки ----------

        // Позиции бойцов вокруг цели, чтобы они не толпились в одной точке (например, в воротах)
        readonly Dictionary<IAreaTarget, List<(InfantryUnit unit, Vector3 point)>> attackSlots = new();

        /// <summary>Сколько бойцов уже атакует цель</summary>
        public int AttackersOf(IAreaTarget target) =>
            attackSlots.TryGetValue(target, out var list) ? list.Count : 0;

        /// <summary>
        /// Цель атакующего бойца: случайная из ближайших построек врага — зданий и участков стены.
        /// Ближе — вероятнее, здание вдвое вероятнее участка стены, турель вдвое вероятнее здания, цель, которую уже бьют многие,
        /// менее вероятна. Так отряд расходится по целям, а в пролом видят казармы.
        /// </summary>
        public IAreaTarget ChooseAttackTarget(InfantryUnit unit, IAreaTarget exclude)
        {
            var enemy = GetFaction(1 - unit.Faction.team);
            var position = unit.transform.position;
            candidateTargets.Clear();

            // Прорвался внутрь стен — бьёт только здания, стены за спиной уже не цель. Пока бой идёт,
            // главное здание врага стоит, так что здание есть всегда; если исключённое — последнее,
            // берём его же (боец подойдёт и поищет позицию заново)
            if (IsInsideWalls(position, enemy))
                return ChooseStructure(enemy, position, HasOtherStructure(enemy, exclude) ? exclude : null);

            float nearest = float.MaxValue;
            foreach (var s in enemy.structures)
                if (Combat.IsAlive(s) && (IAreaTarget)s != exclude)
                    nearest = Mathf.Min(nearest, s.DistanceTo(position));
            foreach (var w in enemy.walls)
                if (Combat.IsAlive(w) && (IAreaTarget)w != exclude)
                    nearest = Mathf.Min(nearest, w.DistanceTo(position));
            if (nearest == float.MaxValue)
                return null;

            float total = 0f;
            foreach (var s in enemy.structures)
                if (Combat.IsAlive(s) && (IAreaTarget)s != exclude)
                    total += AddCandidate(s, s.DistanceTo(position) - nearest, s.Turret != null ? 4f : 2f);
            foreach (var w in enemy.walls)
                if (Combat.IsAlive(w) && (IAreaTarget)w != exclude)
                    total += AddCandidate(w, w.DistanceTo(position) - nearest, 1f);

            float roll = Random.value * total;
            foreach (var (target, weight) in candidateTargets)
            {
                roll -= weight;
                if (roll <= 0f)
                    return target;
            }
            return candidateTargets[^1].target;
        }

        /// <summary>Точка внутри стен базы стороны (текущего уровня стен)</summary>
        public bool IsInsideWalls(Vector3 world, Faction faction)
        {
            var layout = arena.Layout;
            var voxel = (int3)math.floor(arena.WorldToVoxel(world));
            if (voxel.x < 0 || voxel.z < 0 || voxel.x >= layout.sizeX || voxel.z >= layout.sizeZ)
                return false;
            int zone = layout.baseZone[voxel.z * layout.sizeX + voxel.x];
            int level = faction.team == 0 ? zone : zone - 4;
            return level >= 1 && level <= faction.wallLevel;
        }

        static bool HasOtherStructure(Faction faction, IAreaTarget exclude)
        {
            foreach (var s in faction.structures)
                if (Combat.IsAlive(s) && (IAreaTarget)s != exclude)
                    return true;
            return false;
        }

        // Только здания: те же веса близости и числа атакующих, без окна по расстоянию
        IAreaTarget ChooseStructure(Faction enemy, Vector3 position, IAreaTarget exclude)
        {
            float nearest = float.MaxValue;
            foreach (var s in enemy.structures)
                if (Combat.IsAlive(s) && (IAreaTarget)s != exclude)
                    nearest = Mathf.Min(nearest, s.DistanceTo(position));

            float total = 0f;
            foreach (var s in enemy.structures)
                if (Combat.IsAlive(s) && (IAreaTarget)s != exclude)
                    total += AddCandidate(s, Mathf.Min(s.DistanceTo(position) - nearest, UnitCatalog.AttackTargetWindow), s.Turret != null ? 2f : 1f);

            float roll = Random.value * total;
            foreach (var (target, weight) in candidateTargets)
            {
                roll -= weight;
                if (roll <= 0f)
                    return target;
            }
            return candidateTargets[^1].target;
        }

        readonly List<(IAreaTarget target, float weight)> candidateTargets = new();
        NavMeshPath slotPath;

        float AddCandidate(IAreaTarget target, float extraDistance, float preference)
        {
            // Дальше ближайшей цели больше чем на AttackTargetWindow — не рассматриваем
            if (extraDistance > UnitCatalog.AttackTargetWindow)
                return 0f;
            float closeness = 1f / (1f + extraDistance / 4f);
            float weight = preference * closeness * closeness / (1f + 0.35f * AttackersOf(target));
            candidateTargets.Add((target, weight));
            return weight;
        }

        /// <summary>
        /// Позиция для стрельбы по цели: на кольце вокруг неё на расстоянии ~60% дальности, на NavMesh,
        /// с линией огня, не ближе 1.1 м к позициям других бойцов. Первой пробуется точка напротив
        /// бойца, дальше — по очереди в обе стороны. avoid — позиция, в которой боец застрял.
        /// </summary>
        public bool ClaimAttackSlot(InfantryUnit unit, IAreaTarget target, float range, Vector3? avoid, out Vector3 slot)
        {
            using var _ = ClaimSlotMarker.Auto();
            CanClaimSlotThisFrame(); // сброс счётчика в новом кадре
            slotClaimsThisFrame++;
            ReleaseAttackSlot(unit, target);
            int pathFailures = 0;
            if (!attackSlots.TryGetValue(target, out var claims))
                attackSlots[target] = claims = new List<(InfantryUnit, Vector3)>();
            claims.RemoveAll(c => c.unit == null);

            float offset = Mathf.Clamp(range * 0.6f, 2f, range - 1f);
            var half = target.HalfExtents + new Vector2(offset, offset);
            float perimeter = 4f * (half.x + half.y);
            const float spacing = 1.2f;
            int steps = Mathf.Max(8, Mathf.FloorToInt(perimeter / spacing));

            // Точка периметра напротив бойца
            float start = 0f, best = float.MaxValue;
            for (int i = 0; i < steps; i++)
            {
                float s = i * perimeter / steps;
                float d = (PerimeterPoint(target.transform.position, half, s) - unit.transform.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    start = s;
                }
            }

            for (int k = 0; k < steps; k++)
            {
                // 0, +1, -1, +2, -2 ...
                int n = (k + 1) / 2 * (k % 2 == 0 ? -1 : 1);
                float s = Mathf.Repeat(start + n * perimeter / steps, perimeter);
                var p = PerimeterPoint(target.transform.position, half, s);

                if (!NavMesh.SamplePosition(p, out var hit, 1.2f, Nav.Infantry))
                    continue;
                var point = hit.position;
                if (avoid.HasValue && (point - avoid.Value).sqrMagnitude < 2.25f)
                    continue;
                // Верх стены — отрезанный кусок NavMesh, путь туда не найдётся, а искать его дорого
                if (IsOnWall(point))
                    continue;
                // В воротах не стоять — через них идут остальные
                if (InGateway(point, target.Faction))
                    continue;

                bool taken = false;
                foreach (var c in claims)
                {
                    if ((c.point - point).sqrMagnitude < 1.1f * 1.1f)
                    {
                        taken = true;
                        break;
                    }
                }
                if (taken || target.DistanceTo(point) > range * 0.9f)
                    continue;
                if (!Combat.HasLineOfFire(point + Vector3.up * InfantryUnit.ChestHeight, target, unit.Faction))
                    continue;
                // Дойти можно (не верх стены и не отрезанный кусок NavMesh)
                if (!NavMesh.CalculatePath(unit.transform.position, point, Nav.Infantry, slotPath) ||
                    slotPath.status != NavMeshPathStatus.PathComplete)
                {
                    // Поиск пути к недостижимой точке обходит весь NavMesh — после нескольких таких
                    // не перебираем дальше, отряд возьмёт другую цель
                    if (++pathFailures >= MaxSlotPathFailures)
                        break;
                    continue;
                }

                claims.Add((unit, point));
                slot = point;
                return true;
            }

            slot = default;
            return false;
        }

        const int MaxSlotPathFailures = 3;
        // Позиции вокруг построек ищут не больше стольких бойцов за кадр (каждый поиск — несколько
        // путей по NavMesh); остальные идут к цели и пробуют в следующих кадрах. Иначе волна из
        // нескольких отрядов искала все позиции в одном кадре (150 мс)
        const int SlotClaimsPerFrame = 4;
        static readonly ProfilerMarker ClaimSlotMarker = new("MatchManager.ClaimAttackSlot");

        int slotClaimsThisFrame;
        int slotClaimsFrame;

        /// <summary>Можно ли в этом кадре искать позицию вокруг постройки (бюджет на кадр)</summary>
        public bool CanClaimSlotThisFrame()
        {
            if (slotClaimsFrame != Time.frameCount)
            {
                slotClaimsFrame = Time.frameCount;
                slotClaimsThisFrame = 0;
            }
            return slotClaimsThisFrame < SlotClaimsPerFrame;
        }

        /// <summary>Точка стоит на стене (верх стены — отрезанный кусок NavMesh)</summary>
        public bool IsOnWall(Vector3 point)
        {
            var voxel = (int3)math.floor(arena.WorldToVoxel(point + Vector3.down * (arena.VoxelSize * 0.5f)));
            return arena.GetBlock(voxel) == VoxelBlocks.Wall;
        }

        // Проход ворот базы: от точки снаружи ворот внутрь на толщину стены и чуть дальше
        bool InGateway(Vector3 point, Faction faction)
        {
            var gate = GateOf(faction);
            var center = faction.team == 0 ? arena.BaseOne : arena.BaseTwo;
            var inward = center - gate;
            inward.y = 0f;
            inward.Normalize();
            var delta = point - gate;
            delta.y = 0f;
            float along = Vector3.Dot(delta, inward);
            float across = Vector3.Cross(inward, delta).y;
            return along > -2.5f && along < 4.5f && Mathf.Abs(across) < 3f;
        }

        public void ReleaseAttackSlot(InfantryUnit unit, IAreaTarget target)
        {
            if (target != null && attackSlots.TryGetValue(target, out var claims))
                claims.RemoveAll(c => c.unit == unit || c.unit == null);
        }

        // Точка на периметре прямоугольника по длине дуги s (обход против часовой)
        static Vector3 PerimeterPoint(Vector3 center, Vector2 half, float s)
        {
            float w = half.x * 2f, h = half.y * 2f;
            Vector2 p;
            if (s < w) p = new Vector2(-half.x + s, -half.y);
            else if ((s -= w) < h) p = new Vector2(half.x, -half.y + s);
            else if ((s -= h) < w) p = new Vector2(half.x - s, half.y);
            else p = new Vector2(-half.x, half.y - (s - w));
            return center + new Vector3(p.x, 0f, p.y);
        }

        void Update()
        {
            if (Player == null)
                return;

            if (!IsOver)
                MatchTime += Time.deltaTime;

            // Строители появляются, когда готов навмеш
            if (!buildersSpawned && navMesh.HasNavMesh && !navMesh.IsBusy)
            {
                buildersSpawned = true;
                SpawnBuilder(Player);
                SpawnBuilder(Enemy);
                EnemyAI.Begin();
            }

            if (buildersSpawned)
            {
                UpdateHiring(Player);
                UpdateHiring(Enemy);
            }

            UpdateSquads(Player);
            UpdateSquads(Enemy);
        }

        // ---------- Здания ----------

        /// <summary>Заказ постройки: проверка места и цены, оплата, закладка стройки</summary>
        public bool TryOrderConstruction(Faction faction, StructureDef def, int2 min, out string reason)
        {
            if (IsOver)
            {
                reason = "Бой окончен";
                return false;
            }
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

        /// <summary>
        /// Здание разрушено (вызывает само здание перед уничтожением): освободить клетки, вернуть
        /// деньги за очередь казарм; главное здание — конец боя
        /// </summary>
        public void StructureDestroyed(Structure structure)
        {
            var faction = structure.Faction;
            faction.structures.Remove(structure);
            faction.structuresLost++;
            attackSlots.Remove(structure);
            Grid.SetOccupied(structure.MinCell, structure.Def.footprint, false);

            if (structure.CapturePoint != null && structure.CapturePoint.Mine == structure)
                structure.CapturePoint.Mine = null;
            if (structure.Barracks != null)
                structure.Barracks.OnDestroyed();
            if (structure.ReinforcementPoint != null)
                structure.ReinforcementPoint.OnDestroyed();
            if (structure.Armoury != null)
                structure.Armoury.OnDestroyed();
            if (structure.AssignedBuilder != null)
                structure.AssignedBuilder = null;

            if (faction.headquarters == structure)
            {
                faction.headquarters = null;
                if (!IsOver)
                {
                    Winner = GetFaction(1 - faction.team);
                    EndPoint = structure.transform.position;
                    Debug.Log($"[Match] Главное здание стороны {faction.team} разрушено, победила сторона {Winner.team}, " +
                              $"бой длился {MatchTime:0} с");
                }
            }
        }

        /// <summary>Новый бой на новой карте (генерация идёт на главном потоке — несколько секунд)</summary>
        public void NewMatch()
        {
            arena.Seed = Random.Range(1, int.MaxValue);
            arena.Generate();
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

        /// <summary>Строитель берёт ближайшую незанятую стройку своей стороны, к которой недавно не терял путь</summary>
        public Structure ClaimSite(BuilderUnit builder)
        {
            Structure best = null;
            float bestDistance = float.MaxValue;
            foreach (var s in builder.Faction.structures)
            {
                if (s.IsBuilt || s.AssignedBuilder != null || Time.time < s.UnreachableUntil)
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

        /// <summary>Строитель не может пройти к стройке (для подсказки игроку)</summary>
        public event System.Action<Structure> SiteUnreachable;

        /// <summary>К стройке нет пути: несколько секунд её никто не берёт, потом снова пробуют</summary>
        public void ReportUnreachableSite(Structure site)
        {
            site.UnreachableUntil = Time.time + UnreachableSiteRetry;
            if (site.UnreachableReported)
                return;
            site.UnreachableReported = true;
            Debug.Log($"[Стройка] Строителям стороны {site.Faction.team} не пройти к «{site.Def.name}» " +
                      $"в {site.transform.position} — повтор через {UnreachableSiteRetry} с");
            SiteUnreachable?.Invoke(site);
        }

        const float UnreachableSiteRetry = 5f;

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
            if (IsOver)
            {
                reason = "Бой окончен";
                return false;
            }
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

            if (!NavMesh.SamplePosition(point, out var hit, 4f, Nav.Infantry))
                return;

            var go = new GameObject();
            go.transform.SetPositionAndRotation(hit.position, Quaternion.LookRotation(forward));
            spawned.Add(go);
            var builder = go.AddComponent<BuilderUnit>();
            builder.Init(faction, arena.Material);
            faction.builders.Add(builder);
        }

        // ---------- Пехота ----------

        /// <summary>
        /// Отряд выходит из казарм со стороны своих ворот, строем. null — выйти негде (NavMesh не готов)
        /// </summary>
        public Squad SpawnSquad(Faction faction, Barracks barracks, BarracksBehavior behavior, SquadWeapon weapon)
        {
            var gate = GateOf(faction);
            var from = barracks != null ? barracks.Structure.ClosestEdgePoint(gate, 1.5f) : gate;
            return SpawnSquadAt(faction, barracks, behavior, weapon, from);
        }

        Squad SpawnSquadAt(Faction faction, Barracks barracks, BarracksBehavior behavior, SquadWeapon weapon, Vector3 point)
        {
            if (!navMesh.HasNavMesh || !NavMesh.SamplePosition(point, out var hit, 4f, Nav.Infantry))
                return null;

            var look = GateOf(faction) - hit.position;
            look.y = 0f;
            var rotation = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;
            var squad = new Squad(faction, barracks, behavior, weapon);

            for (int i = 0; i < UnitCatalog.SquadSize; i++)
            {
                // Бойцы кучкой, каждый — на ближайшую к своему месту точку NavMesh
                var offset = rotation * new Vector3((i % 3 - 1) * 0.9f, 0f, -(i / 3) * 0.9f);
                var position = NavMesh.SamplePosition(hit.position + offset, out var spot, 1.5f, Nav.Infantry) ? spot.position : hit.position;
                // Спецоружие — у последних SpecialsPerSquad бойцов (задний ряд строя), остальные с винтовками
                bool special = weapon != SquadWeapon.Rifle && i >= UnitCatalog.SquadSize - WeaponCatalog.SpecialsPerSquad;

                var go = new GameObject();
                go.transform.SetPositionAndRotation(position, rotation);
                spawned.Add(go);
                var unit = go.AddComponent<InfantryUnit>();
                unit.Init(faction, squad, arena.Material, special ? weapon : SquadWeapon.Rifle);
                faction.units.Add(unit);
                squad.Members.Add(unit);
            }

            faction.squads.Add(squad);
            if (barracks != null)
            {
                barracks.Squads.Add(squad);
                faction.unitsHired += squad.Members.Count;
            }
            return squad;
        }

        /// <summary>
        /// Боец пополнения выходит из пункта подкрепления со стороны отряда и сразу становится его
        /// бойцом (побежит на своё место в строю). false — выйти негде (NavMesh не готов)
        /// </summary>
        public bool SpawnReinforcement(ReinforcementPoint point, Squad squad, SquadWeapon weapon)
        {
            var from = point.Structure.ClosestEdgePoint(squad.Center, 1.2f);
            if (!navMesh.HasNavMesh || !NavMesh.SamplePosition(from, out var hit, 3f, Nav.Infantry))
                return false;

            var look = squad.Center - hit.position;
            look.y = 0f;
            var go = new GameObject();
            go.transform.SetPositionAndRotation(hit.position, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);
            spawned.Add(go);
            var unit = go.AddComponent<InfantryUnit>();
            unit.Init(squad.Faction, squad, arena.Material, weapon);
            squad.Faction.units.Add(unit);
            squad.Members.Add(unit);
            squad.Faction.unitsHired++;
            return true;
        }

        void UpdateSquads(Faction faction)
        {
            float dt = Time.deltaTime;
            foreach (var squad in faction.squads)
                squad.Tick(dt);
            faction.squads.RemoveAll(s => !s.IsAlive);
        }

        /// <summary>Мировая точка сразу снаружи ворот базы стороны</summary>
        public Vector3 GateOf(Faction faction) => faction.team == 0 ? arena.GateOne : arena.GateTwo;

        /// <summary>
        /// Пост отряда в обороне: за воротами внутри стен, лицом к воротам. Отряды стоят рядами по
        /// три поста; место — порядковый номер отряда среди обороняющихся, строй сам уплотняется.
        /// </summary>
        public void SquadPost(Squad squad, out Vector3 post, out Quaternion facing)
        {
            var faction = squad.Faction;
            int index = 0, slot = 0;
            foreach (var s in faction.squads)
            {
                if (!s.IsAlive || s.Behavior != BarracksBehavior.Defend)
                    continue;
                if (s == squad)
                    index = slot;
                slot++;
            }

            var gate = GateOf(faction);
            var center = faction.team == 0 ? arena.BaseOne : arena.BaseTwo;
            var inward = center - gate;
            inward.y = 0f;
            inward.Normalize();
            var side = new Vector3(inward.z, 0f, -inward.x);

            const int perRow = 3;
            const float lateralSpacing = 4.5f;
            const float rowSpacing = 3.2f;
            int row = index / perRow, column = index % perRow;
            // Посередине, потом по бокам
            float lateral = (column == 0 ? 0f : column == 1 ? -1f : 1f) * lateralSpacing;
            post = gate + inward * (6f + row * rowSpacing) + side * lateral;
            facing = Quaternion.LookRotation(-inward);
        }

        // ---------- Отладка ----------

        /// <summary>Отряд противника у его ворот (отладка, в обход ИИ)</summary>
        public void DebugSpawnEnemySquad(BarracksBehavior behavior)
        {
            var gate = GateOf(Enemy);
            var outward = gate - arena.BaseTwo;
            outward.y = 0f;
            outward.Normalize();
            SpawnSquadAt(Enemy, null, behavior, SquadWeapon.Rifle, gate + outward * 3f);
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
