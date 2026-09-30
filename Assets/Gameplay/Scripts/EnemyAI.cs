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
    /// денег на следующую постройку. Бойцы копятся в обороне; набралась волна — все казармы в атаку;
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

            var next = NextBuildItem(out bool hasNext);
            float reserve = 0f;
            if (hasNext)
            {
                int cost = next.HasValue ? StructureCatalog.Get(next.Value).costBase : StructureCatalog.BuilderCost;
                if (me.CanAfford(cost, 0))
                    DoBuildItem(next);
                else
                    reserve = cost * 0.6f;
            }

            ReinforceSquads(reserve);
            Research(reserve);
            HireInfantry(reserve);
            UpdateWaves();
        }

        // ---------- Экономика ----------

        // Первый невыполненный пункт списка; пока строек больше, чем строителей, — ждать
        StructureType? NextBuildItem(out bool hasNext)
        {
            hasNext = false;
            int unfinished = 0;
            foreach (var s in me.structures)
                if (s != null && !s.IsBuilt)
                    unfinished++;
            if (unfinished >= Mathf.Max(1, me.builders.Count))
                return null;

            var required = new Dictionary<int, int>();
            foreach (var item in BuildOrder)
            {
                int key = item.HasValue ? (int)item.Value : -1;
                required.TryGetValue(key, out int n);
                required[key] = ++n;
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
            if (FindSpot(def, out var min))
                match.TryOrderConstruction(me, def, min, out _);
        }

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
            else
            {
                center = HqCenter + new int2(Random.Range(-6, 7), Random.Range(-6, 7));
            }

            // Первые несколько подходящих мест от центра — из них случайное
            spots.Clear();
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
                        if (grid.CanPlace(me, def, candidate, out _, out _) &&
                            grid.HasClearance(candidate, def.footprint, Clearance) &&
                            !InCorridor(candidate, def.footprint))
                            spots.Add(candidate);
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

            if (!Attacking)
            {
                if (defendingSquads >= waveSize)
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
            return n;
        }

        void SetBehavior(BarracksBehavior behavior)
        {
            foreach (var s in me.structures)
                if (s != null && s.Barracks != null)
                    s.Barracks.SetBehavior(behavior);
        }
    }
}
