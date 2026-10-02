using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Generals
{
    /// <summary>Настройки скриптового ИИ противника (инспектор MatchManager)</summary>
    [Serializable]
    public class EnemyAiSettings
    {
        [Tooltip("Выключить — противник ничего не делает (отладка)")]
        public bool enabled = true;
        [Tooltip("Раз в сколько секунд ИИ принимает решения")]
        [Range(0.2f, 5f)] public float thinkInterval = 1f;
        [Tooltip("Сколько секунд после появления строителей ИИ ничего не делает — фора игроку")]
        [Range(0f, 120f)] public float startDelay = 15f;
        [Tooltip("Первые столько секунд после форы — без волн атаки, только стройка и найм (отладка, автотест)")]
        [Min(0f)] public float peaceSeconds;
        [Tooltip("Отрядов в первой волне атаки")]
        [Range(1, 10)] public int firstWaveSize = 2;
        [Tooltip("На сколько отрядов каждая следующая волна больше")]
        [Range(0, 5)] public int waveGrowth = 1;
        [Range(1, 15)] public int maxWaveSize = 5;
        [Tooltip("Волна закончилась, когда от атакующих отрядов осталось не больше стольких бойцов — копятся снова")]
        [Range(0, 10)] public int waveEndSurvivors = 2;
        [Tooltip("Столько бойцов игрока внутри стен — атакующих отзывают на защиту")]
        [Range(1, 20)] public int recallThreshold = 3;
        [Tooltip("Очередь найма на каждых казармах держится такой")]
        [Range(1, 5)] public int barracksQueue = 2;
    }

    /// <summary>
    /// Простой скриптовый ИИ противника (шаг 6 среза; нейросеть — после среза). Играет по тем же
    /// правилам, что игрок: те же ресурсы, цены, строители, правила застройки, без подглядывания.
    /// Экономика — по списку стройки (разрушенное отстраивает). Армия нанимается постоянно, с запасом
    /// денег на следующую постройку; завод — танк и артиллерия исследованием, по машине в очереди.
    /// Отряды и машины копятся в обороне; набралась волна — все казармы и заводы в атаку;
    /// волна почти погибла — снова оборона. Игрок прорвался внутрь стен — атакующих отзывают.
    /// </summary>
    public class EnemyAI : MonoBehaviour
    {
        // null — нанять строителя; иначе — здание. Сколько раз встречается до позиции k — столько
        // таких должно стоять, чтобы пункт k считался выполненным.
        static readonly StructureType?[] BuildOrder =
        {
            StructureType.Extractor,
            StructureType.Barracks,
            // Завод — рано, пока база пустая: ангару 8×10 нужен проезд к воротам шириной в машину, в
            // застроенной базе его уже нет (автотест: после вторых казарм — ни одного места)
            StructureType.Factory,
            StructureType.Extractor,
            null,
            StructureType.Turret,
            StructureType.Extractor,
            StructureType.Barracks,
            StructureType.Turret,
            StructureType.ReinforcementPoint,
            StructureType.Armoury,
            null,
            StructureType.Extractor,
            StructureType.Turret,
        };

        // Зазор между зданиями (в клетках) — чтобы между ними проходили бойцы
        const int Clearance = 2;
        // Коридор от ворот к главному зданию не застраивается, клеток от оси
        const float CorridorHalfWidth = 3.5f;

        public int WaveNumber { get; private set; }
        public bool Attacking { get; private set; }

        MatchManager match;
        EnemyAiSettings settings;
        Faction me;
        Faction foe;
        float timer;
        float startTime = -1f;
        int waveSize;
        int turretSide;
        readonly List<int2> spots = new();

        public void Init(MatchManager match, EnemyAiSettings settings)
        {
            this.match = match;
            this.settings = settings;
        }

        /// <summary>Новый бой: всё с начала</summary>
        public void ResetForMatch(Faction me, Faction foe)
        {
            this.me = me;
            this.foe = foe;
            WaveNumber = 0;
            Attacking = false;
            waveSize = settings.firstWaveSize;
            startTime = -1f;
            timer = 0f;
            turretSide = 0;
            noSpotUntil.Clear();
            noSpotLogged = null;
        }

        /// <summary>Строители появились (готов NavMesh) — пошёл отсчёт форы</summary>
        public void Begin()
        {
            startTime = Time.time;
        }

        void Update()
        {
            if (!settings.enabled || me == null || startTime < 0f || match.IsOver || me.headquarters == null)
                return;
            if (Time.time - startTime < settings.startDelay)
                return;

            timer -= Time.deltaTime;
            if (timer > 0f)
                return;
            timer = settings.thinkInterval;

            var next = NextBuildItem(out bool hasNext, out bool waiting);
            float reserve = 0f;
            if (hasNext)
            {
                int cost = next.HasValue ? StructureCatalog.Get(next.Value).costBase : StructureCatalog.BuilderCost;
                if (!waiting && me.CanAfford(cost, 0))
                    DoBuildItem(next);
                else
                    reserve = cost;
            }

            ReinforceSquads(reserve);
            Research(reserve);
            ResearchVehicles(reserve);
            HireInfantry(reserve);
            HireVehicles(reserve);
            UpdateWaves();
        }

        // ---------- Экономика ----------

        // Первый невыполненный пункт списка; waiting — строек больше, чем строителей: ставить ещё
        // рано, но деньги на этот пункт уже откладываются (иначе их съедал найм и стройка вставала)
        StructureType? NextBuildItem(out bool hasNext, out bool waiting)
        {
            hasNext = false;
            int unfinished = 0;
            foreach (var s in me.structures)
                if (s != null && !s.IsBuilt)
                    unfinished++;
            waiting = unfinished >= Mathf.Max(1, me.builders.Count);

            var required = new Dictionary<int, int>();
            foreach (var item in BuildOrder)
            {
                int key = item.HasValue ? (int)item.Value : -1;
                required.TryGetValue(key, out int n);
                required[key] = ++n;
                // Для этого здания недавно не нашлось места — пока пропустить, строить следующее по списку
                // (автотест: стройка вставала целиком на «негде поставить завод»)
                if (item.HasValue && noSpotUntil.TryGetValue(item.Value, out float until) && Time.time < until)
                    continue;
                if (Count(item) < n)
                {
                    hasNext = true;
                    return item;
                }
            }
            return null;
        }

        int Count(StructureType? item)
        {
            if (!item.HasValue)
                return me.builders.Count + me.buildersQueued;
            int n = 0;
            foreach (var s in me.structures)
                if (s != null && s.IsAlive && s.Def.type == item.Value)
                    n++;
            return n;
        }

        void DoBuildItem(StructureType? item)
        {
            if (!item.HasValue)
            {
                match.TryHireBuilder(me, out _);
                return;
            }

            var def = StructureCatalog.Get(item.Value);
            bool spotFound = FindSpot(def, out var min);
            if (spotFound)
            {
                match.TryOrderConstruction(me, def, min, out _);
                noSpotLogged = null;
            }
            else
            {
                noSpotUntil[def.type] = Time.time + NoSpotRetry;
            }
            if (!spotFound && noSpotLogged != def.type)
            {
                // Раз на тип: причину видно в консоли (и в отчёте автотеста)
                noSpotLogged = def.type;
                Debug.Log($"[ИИ] Стороне {me.team} негде поставить «{def.name}» — пропускаю на {NoSpotRetry:0} с, строю следующее " +
                          $"(мест по правилам и зазору: {spotsByRules}, проходы: {lastPassageReason ?? "—"})");
            }
        }

        StructureType? noSpotLogged;
        // Здания, для которых не нашлось места, — когда пробовать снова
        readonly Dictionary<StructureType, float> noSpotUntil = new();
        const float NoSpotRetry = 30f;
        int spotsByRules;
        string lastPassageReason;

        // Отряды, вернувшиеся в оборону потрёпанными, пополняются в пункте подкрепления
        void ReinforceSquads(float reserve)
        {
            ReinforcementPoint point = null;
            foreach (var s in me.structures)
                if (s != null && s.IsBuilt && s.ReinforcementPoint != null)
                    point = s.ReinforcementPoint;
            if (point == null)
                return;

            foreach (var squad in me.squads)
            {
                if (!squad.IsAlive || squad.Behavior != BarracksBehavior.Defend)
                    continue;
                int missing = ReinforcementPoint.Missing(squad);
                if (missing < 2 || me.baseResource - missing * UnitCatalog.ReinforceCost < reserve)
                    continue;
                point.TryReinforce(squad, out _);
            }
        }

        // Оружейная: изучить недостающее до двух случайное спецоружие (без переучивания)
        void Research(float reserve)
        {
            if (me.researching.HasValue || me.knownWeapons.Count >= WeaponCatalog.MaxKnownSpecials)
                return;
            Armoury armoury = null;
            foreach (var s in me.structures)
                if (s != null && s.IsBuilt && s.Armoury != null)
                    armoury = s.Armoury;
            if (armoury == null)
                return;

            var options = new List<SpecialWeapon>();
            foreach (var special in WeaponCatalog.Specials)
                if (!me.Knows(special.id))
                    options.Add(special);
            var pick = options[Random.Range(0, options.Count)];
            if (me.baseResource - pick.researchCost >= reserve)
                armoury.TryResearch(pick.id, null, out _);
        }

        void HireInfantry(float reserve)
        {
            // Копить не больше, чем нужно на следующую волну (плюс отряд на защиту): лишняя толпа у
            // ворот только ест деньги стройки (автотест: 18 отрядов у ворот и ни одного нового здания)
            if (SquadsAtHome() >= waveSize + 1)
                return;

            foreach (var s in me.structures)
            {
                var barracks = s != null ? s.Barracks : null;
                if (barracks == null || !s.IsBuilt || barracks.Queued >= settings.barracksQueue)
                    continue;

                // Открыто спецоружие — чаще отряды с ним, иногда простые
                if (me.knownWeapons.Count > 0 && Random.value < 0.7f)
                    barracks.SetWeapon(me.knownWeapons[Random.Range(0, me.knownWeapons.Count)]);
                else
                    barracks.SetWeapon(SquadWeapon.Rifle);

                if (me.baseResource - WeaponCatalog.SquadCost(barracks.Weapon) < reserve)
                    return;
                barracks.TryHire(out _);
            }
        }

        Factory BuiltFactory()
        {
            foreach (var s in me.structures)
                if (s != null && s.IsBuilt && s.Factory != null)
                    return s.Factory;
            return null;
        }

        // Завод: сначала танк, потом артиллерия
        void ResearchVehicles(float reserve)
        {
            if (me.vehicleResearching.HasValue)
                return;
            var factory = BuiltFactory();
            if (factory == null)
                return;
            foreach (var type in VehicleCatalog.All)
            {
                if (me.KnowsVehicle(type))
                    continue;
                if (me.baseResource - VehicleCatalog.Get(type).researchCost >= reserve)
                    factory.TryResearch(type, out _);
                return;
            }
        }

        // Одна машина в очереди: танк чаще, разведчик и артиллерия реже (из открытых)
        void HireVehicles(float reserve)
        {
            var factory = BuiltFactory();
            if (factory == null || factory.Queued >= 1)
                return;
            // Машин в обороне — не больше размера волны (иначе в мирное время копились без предела)
            int vehiclesAtHome = 0;
            foreach (var v in me.vehicles)
                if (v != null && v.IsAlive && v.Behavior == BarracksBehavior.Defend)
                    vehiclesAtHome++;
            if (vehiclesAtHome >= waveSize)
                return;

            float roll = Random.value;
            var type = roll < 0.5f && me.KnowsVehicle(VehicleType.Tank) ? VehicleType.Tank
                     : roll < 0.75f && me.KnowsVehicle(VehicleType.Artillery) ? VehicleType.Artillery
                     : VehicleType.Scout;
            // Пока отрядов на волну не хватает — машина, только если останется и на отряд: дешёвый
            // разведчик иначе забирал все деньги раньше отряда (автотест: 24 разведчика и ни одного отряда)
            float keep = reserve + (SquadsAtHome() < waveSize ? UnitCatalog.SquadCost : 0);
            if (me.baseResource - VehicleCatalog.Get(type).cost >= keep)
                factory.TryHire(type, out _);
        }

        // Отряды на базе (в обороне) и заказанные в казармах
        int SquadsAtHome()
        {
            int squads = 0;
            foreach (var squad in me.squads)
                if (squad.IsAlive && squad.Behavior == BarracksBehavior.Defend)
                    squads++;
            foreach (var s in me.structures)
                if (s != null && s.Barracks != null)
                    squads += s.Barracks.Queued;
            return squads;
        }

        // ---------- Застройка ----------

        int2 HqCenter => me.headquarters.MinCell + me.headquarters.Def.footprint / 2;
        int2 GateCell => match.Arena.Layout.gates[me.team];

        // Куда ставить: обычные здания — вокруг главного здания со случайным сдвигом (база
        // застраивается равномерно), турели — по бокам от ворот снаружи, по очереди справа и слева
        bool FindSpot(StructureDef def, out int2 min)
        {
            int2 center;
            if (def.rule == PlacementRule.BaseArea)
            {
                var outward = math.normalizesafe((float2)(GateCell - HqCenter));
                var side = new float2(-outward.y, outward.x);
                float sign = turretSide++ % 2 == 0 ? 1f : -1f;
                center = (int2)math.round(GateCell + outward * 3f + side * sign * 6f);
            }
            else if (def.type == StructureType.Factory)
            {
                // Завод — сбоку от прохода к воротам, ближе к ним: технике нужен свободный выезд
                var outward = math.normalizesafe((float2)(GateCell - HqCenter));
                var side = new float2(-outward.y, outward.x);
                var mid = ((float2)HqCenter + GateCell) * 0.5f;
                center = (int2)math.round(mid + side * (Random.value < 0.5f ? -9f : 9f));
            }
            else
            {
                center = HqCenter + new int2(Random.Range(-6, 7), Random.Range(-6, 7));
            }

            // Первые несколько подходящих мест от центра — из них случайное
            spots.Clear();
            spotsByRules = 0;
            lastPassageReason = null;
            var grid = match.Grid;
            var start = BuildGrid.MinFromCenter(def, center);
            for (int r = 0; r <= 24 && spots.Count < 6; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (math.max(math.abs(dx), math.abs(dz)) != r)
                            continue;
                        var candidate = start + new int2(dx, dz);
                        // Проходы (дорогая проверка) — последней
                        if (!grid.CanPlace(me, def, candidate, out _, out _, checkPassages: false) ||
                            !grid.HasClearance(candidate, def.footprint, def.type == StructureType.Factory ? 1 : Clearance) ||
                            InCorridor(candidate, def.footprint))
                            continue;
                        spotsByRules++;
                        if (grid.KeepsPassages(me, def, candidate, out var passage))
                            spots.Add(candidate);
                        else
                            lastPassageReason = passage;
                    }
                }
            }

            min = spots.Count > 0 ? spots[Random.Range(0, spots.Count)] : default;
            return spots.Count > 0;
        }

        // Прямоугольник задевает коридор от главного здания к воротам и дальше наружу
        bool InCorridor(int2 min, int2 size)
        {
            float2 a = HqCenter;
            float2 gate = GateCell;
            float2 b = gate + math.normalizesafe(gate - a) * 8f;
            for (int z = min.y; z < min.y + size.y; z++)
            {
                for (int x = min.x; x < min.x + size.x; x++)
                {
                    float2 p = new(x + 0.5f, z + 0.5f);
                    float2 ab = b - a;
                    float t = math.saturate(math.dot(p - a, ab) / math.lengthsq(ab));
                    if (math.distance(p, a + ab * t) < CorridorHalfWidth)
                        return true;
                }
            }
            return false;
        }

        // ---------- Волны ----------

        void UpdateWaves()
        {
            if (Time.time - startTime < settings.startDelay + settings.peaceSeconds)
                return;
            // Волна считается в отрядах, её остаток — в бойцах
            int defendingSquads = 0, attackers = 0;
            foreach (var squad in me.squads)
            {
                if (!squad.IsAlive)
                    continue;
                if (squad.Behavior == BarracksBehavior.Attack)
                    attackers += squad.Members.Count;
                else
                    defendingSquads++;
            }
            // Машина в волне — как отряд; живая атакующая машина — как три бойца
            foreach (var v in me.vehicles)
            {
                if (v == null || !v.IsAlive)
                    continue;
                if (v.Behavior == BarracksBehavior.Attack)
                    attackers += 3;
                else
                    defendingSquads++;
            }

            if (!Attacking)
            {
                // Пока враг внутри стен — не в атаку: отозвали бы на следующем же ходу (волны каждые 2 с)
                if (defendingSquads >= waveSize && EnemiesInsideWalls() < settings.recallThreshold)
                {
                    Attacking = true;
                    WaveNumber++;
                    SetBehavior(BarracksBehavior.Attack);
                    Debug.Log($"[ИИ] Волна {WaveNumber}: отрядов в атаку — {defendingSquads}");
                }
                return;
            }

            // Игрок прорвался внутрь стен — все на защиту; волна почти погибла — копим следующую
            bool breached = EnemiesInsideWalls() >= settings.recallThreshold;
            if (breached || attackers <= settings.waveEndSurvivors)
            {
                Attacking = false;
                SetBehavior(BarracksBehavior.Defend);
                if (!breached)
                    waveSize = Mathf.Min(settings.maxWaveSize, waveSize + settings.waveGrowth);
            }
        }

        int EnemiesInsideWalls()
        {
            int n = 0;
            foreach (var u in foe.units)
                if (Combat.IsAlive(u) && match.IsInsideWalls(u.transform.position, me))
                    n++;
            foreach (var v in foe.vehicles)
                if (Combat.IsAlive(v) && match.IsInsideWalls(v.transform.position, me))
                    n += 3;
            return n;
        }

        void SetBehavior(BarracksBehavior behavior)
        {
            foreach (var s in me.structures)
            {
                if (s == null)
                    continue;
                if (s.Barracks != null)
                    s.Barracks.SetBehavior(behavior);
                if (s.Factory != null)
                    s.Factory.SetBehavior(behavior);
            }
        }
    }
}
