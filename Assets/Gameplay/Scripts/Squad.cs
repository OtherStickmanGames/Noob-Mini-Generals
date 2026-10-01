using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Profiling;

namespace Generals
{
    /// <summary>Что отряд сейчас велит своим бойцам</summary>
    public enum SquadOrder
    {
        /// <summary>Стоять строем у поста (оборона) или на месте</summary>
        Hold,
        /// <summary>Идти на вражеских бойцов (Threat)</summary>
        EngageUnits,
        /// <summary>Бить постройку (AreaTarget) со своих позиций вокруг неё</summary>
        AttackArea,
    }

    /// <summary>
    /// Отряд пехоты, как в Dawn of War: нанимается целиком (UnitCatalog.SquadSize бойцов), решения
    /// принимает отряд — куда идти и что атаковать, а бойцы стреляют свободно: каждый по ближайшему
    /// врагу в своей дальности, иначе по цели отряда. Поведение («Оборона» / «Атака») — от казарм.
    /// Оборона — строем на своём посту у ворот, на подошедших врагов — всем отрядом.
    /// Атака — на бойцов врага в обзоре, иначе на случайную из ближайших построек (MatchManager.ChooseAttackTarget).
    /// </summary>
    public class Squad
    {
        const float ThinkInterval = 0.25f;
        const float Spacing = 1.3f;

        // Строй из пяти: трое впереди, двое сзади (в долях Spacing: x — вбок, y — вперёд)
        static readonly Vector2[] Formation =
        {
            new(0f, 0f), new(-1f, 0f), new(1f, 0f), new(-0.5f, -1f), new(0.5f, -1f),
            new(-1.5f, -1f), new(1.5f, -1f), new(0f, -2f),
        };

        public readonly Faction Faction;
        /// <summary>Номер отряда у стороны (1, 2, …) — для списка в пункте подкрепления</summary>
        public readonly int Number;
        /// <summary>Сколько бойцов уже заказано в пункте подкрепления и ещё не вышло</summary>
        public int PendingReinforcements;
        /// <summary>Из них — со спецоружием</summary>
        public int PendingSpecials;
        /// <summary>Спецоружие отряда (выбрано в казармах при найме); Rifle — у всех винтовки</summary>
        public readonly SquadWeapon Weapon;

        /// <summary>Сколько живых бойцов со спецоружием</summary>
        public int SpecialCount
        {
            get
            {
                int n = 0;
                foreach (var m in Members)
                    if (m != null && m.WeaponType != SquadWeapon.Rifle)
                        n++;
                return n;
            }
        }
        public Barracks Barracks { get; private set; }
        public readonly List<InfantryUnit> Members = new();
        public BarracksBehavior Behavior => Barracks != null ? Barracks.Behavior : ownBehavior;

        public SquadOrder Order { get; private set; }
        public InfantryUnit Threat { get; private set; }
        public IAreaTarget AreaTarget { get; private set; }

        public bool IsAlive => Members.Count > 0;

        /// <summary>Средняя позиция бойцов</summary>
        public Vector3 Center
        {
            get
            {
                var sum = Vector3.zero;
                foreach (var m in Members)
                    sum += m.transform.position;
                return Members.Count > 0 ? sum / Members.Count : sum;
            }
        }

        BarracksBehavior ownBehavior;
        Vector3 anchor;
        Quaternion facing = Quaternion.identity;
        float thinkTimer;

        public Squad(Faction faction, Barracks barracks, BarracksBehavior behavior, SquadWeapon weapon)
        {
            Faction = faction;
            Weapon = weapon;
            Number = ++faction.squadsCreated;
            Barracks = barracks;
            ownBehavior = behavior;
            thinkTimer = Random.value * ThinkInterval;
        }

        /// <summary>Место бойца в строю (по его номеру в отряде)</summary>
        public Vector3 FormationPoint(InfantryUnit member)
        {
            int i = Mathf.Max(0, Members.IndexOf(member)) % Formation.Length;
            var f = Formation[i] * Spacing;
            return anchor + facing * new Vector3(f.x, 0f, f.y);
        }

        public void BehaviorChanged()
        {
            thinkTimer = 0f;
            SetAreaTarget(null);
            Threat = null;
            foreach (var m in Members)
                if (m != null)
                    m.OrderChanged();
        }

        /// <summary>Казармы разрушены: отряд остаётся с их последним поведением</summary>
        public void DetachFromBarracks(BarracksBehavior lastBehavior)
        {
            ownBehavior = lastBehavior;
            Barracks = null;
        }

        public void Tick(float deltaTime)
        {
            Members.RemoveAll(m => !Combat.IsAlive(m));
            if (Members.Count == 0)
            {
                SetAreaTarget(null);
                return;
            }

            thinkTimer -= deltaTime;
            if (thinkTimer > 0f)
                return;
            thinkTimer = ThinkInterval;
            Think();
        }

        void Think()
        {
            using var _ = ThinkMarker.Auto();
            ThinkCore();
        }

        void ThinkCore()
        {
            var match = MatchManager.Instance;
            var enemy = match.GetFaction(1 - Faction.team);
            var center = Center;

            if (Behavior == BarracksBehavior.Defend)
            {
                SetAreaTarget(null);
                match.SquadPost(this, out var post, out var postFacing);
                var threat = NearestEnemyUnit(enemy, post, UnitCatalog.DefendRadius);
                if (threat != null)
                    EngageUnits(threat, center);
                else
                    Hold(post, postFacing);
                return;
            }

            // Атака: бойцы врага в обзоре важнее построек
            var unit = NearestEnemyUnit(enemy, center, UnitCatalog.InfantrySight);
            if (unit != null)
            {
                EngageUnits(unit, center);
                return;
            }

            // По отряду бьёт турель — отвечаем ей, что бы ни били до этого
            var turret = TurretFiringAt(enemy, center);
            if (turret != null)
            {
                SetAreaTarget(turret);
                Order = SquadOrder.AttackArea;
                Threat = null;
                return;
            }

            // Постройка — случайная из ближайших, бьём до разрушения. Шли на участок стены, но уже
            // прошли внутрь (в пролом или ворота) — теперь здания
            var leader = Members[0];
            bool wallBehind = AreaTarget is WallSegment && match.IsInsideWalls(center, enemy);
            if (!Combat.IsAlive(AreaTarget) || wallBehind)
                SetAreaTarget(match.ChooseAttackTarget(leader, null));

            if (AreaTarget != null)
            {
                Order = SquadOrder.AttackArea;
                Threat = null;
            }
            else
            {
                Hold(center, facing);
            }
        }

        /// <summary>Ближайшая вражеская турель, которая сейчас стреляет по бойцу этого отряда</summary>
        public Structure TurretFiringAt(Faction enemy, Vector3 around)
        {
            Structure best = null;
            float bestDistance = float.MaxValue;
            foreach (var s in enemy.structures)
            {
                if (s.Turret == null || !Combat.IsAlive(s) || !(s.Turret.Target is InfantryUnit u) || u.Squad != this)
                    continue;
                float d = s.DistanceTo(around);
                if (d < bestDistance)
                {
                    best = s;
                    bestDistance = d;
                }
            }
            return best;
        }

        /// <summary>Вокруг цели не нашлось места — отряд берёт другую</summary>
        public void RetargetArea(InfantryUnit leader, IAreaTarget failed)
        {
            SetAreaTarget(MatchManager.Instance.ChooseAttackTarget(leader, failed));
        }

        void SetAreaTarget(IAreaTarget target)
        {
            if (AreaTarget == target)
                return;
            AreaTarget = target;
            foreach (var m in Members)
                if (m != null)
                    m.ReleaseSlot();
        }

        void EngageUnits(InfantryUnit threat, Vector3 center)
        {
            Order = SquadOrder.EngageUnits;
            Threat = threat;
            // Строй — на подходе к врагу, лицом к нему
            var toThreat = threat.transform.position - center;
            toThreat.y = 0f;
            if (toThreat.sqrMagnitude > 0.01f)
                facing = Quaternion.LookRotation(toThreat);
            anchor = threat.transform.position - facing * Vector3.forward * (UnitCatalog.InfantryRange * 0.6f);
        }

        void Hold(Vector3 point, Quaternion rotation)
        {
            Order = SquadOrder.Hold;
            Threat = null;
            anchor = point;
            facing = rotation;
        }

        /// <summary>
        /// Ближайший вражеский боец в радиусе, до которого отряд может добраться: видит его кто-то из
        /// бойцов или до него есть короткий путь (не в обход через ворота на другом конце базы).
        /// Враг за стеной — не цель: атакующие продолжат бить постройки (ту же стену), защитники —
        /// стоять на посту. Иначе обе стороны стоят у стены и ждут друг друга.
        /// </summary>
        InfantryUnit NearestEnemyUnit(Faction enemy, Vector3 around, float radius)
        {
            candidates.Clear();
            foreach (var u in enemy.units)
            {
                if (!Combat.IsAlive(u))
                    continue;
                float d = (u.transform.position - around).sqrMagnitude;
                if (d < radius * radius)
                    candidates.Add((d, u));
            }
            candidates.Sort((a, b) => a.distance.CompareTo(b.distance));

            // Проверяем только несколько ближайших — путь дорогой
            for (int i = 0; i < candidates.Count && i < 3; i++)
                if (CanFight(candidates[i].unit))
                    return candidates[i].unit;
            return null;
        }

        bool CanFight(InfantryUnit enemy)
        {
            foreach (var m in Members)
                if (Combat.HasLineOfFire(m.transform.position + Vector3.up * InfantryUnit.ChestHeight, enemy, Faction))
                    return true;

            var from = Members[0].transform.position;
            var to = enemy.transform.position;
            path ??= new NavMeshPath();
            using (CanFightPathMarker.Auto())
            {
                if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    return false;
            }

            float length = 0f;
            var corners = path.corners;
            for (int i = 1; i < corners.Length; i++)
                length += Vector3.Distance(corners[i - 1], corners[i]);
            var flat = to - from;
            flat.y = 0f;
            return length <= flat.magnitude * 1.5f + 6f;
        }

        readonly List<(float distance, InfantryUnit unit)> candidates = new();
        static NavMeshPath path;
        static readonly ProfilerMarker ThinkMarker = new("Squad.Think");
        static readonly ProfilerMarker CanFightPathMarker = new("Squad.CanFight path");
    }
}
