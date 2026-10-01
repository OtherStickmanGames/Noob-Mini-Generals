using System.Collections.Generic;
using UnityEngine;

namespace Generals
{
    /// <summary>
    /// Казармы: очередь найма отрядов пехоты (отряд — UnitCatalog.SquadSize бойцов сразу, как в
    /// Dawn of War), вооружение новых отрядов (винтовки или открытое в оружейной спецоружие — его
    /// несут WeaponCatalog.SpecialsPerSquad бойцов, отряд дороже) и поведение отрядов («Оборона» /
    /// «Атака»). Поведение задаётся на казармы и сразу действует на все их отряды, включая уже нанятые.
    /// Висит на здании казарм рядом со Structure.
    /// </summary>
    public class Barracks : MonoBehaviour, IUnitProducer
    {
        public Structure Structure { get; private set; }
        public BarracksBehavior Behavior { get; private set; } = BarracksBehavior.Defend;
        /// <summary>С каким вооружением выходят новые отряды</summary>
        public SquadWeapon Weapon { get; private set; } = SquadWeapon.Rifle;
        public int Queued => queue.Count;
        /// <summary>Прогресс найма текущего отряда, 0..1</summary>
        public float Progress { get; private set; }
        public readonly List<Squad> Squads = new();
        /// <summary>
        /// Постоянный найм (как правый клик в Dawn of War): в очереди всегда отряд, пока хватает
        /// ресурсов; не хватает — казармы ждут и закажут, как только появятся
        /// </summary>
        public bool Repeat { get; private set; }

        // Очередь: вооружение каждого заказанного отряда — какое было выбрано и оплачено при заказе
        readonly List<SquadWeapon> queue = new();

        public Faction Faction => Structure.Faction;

        /// <summary>Сколько живых бойцов во всех отрядах казарм</summary>
        public int UnitCount
        {
            get
            {
                int n = 0;
                foreach (var s in Squads)
                    n += s.Members.Count;
                return n;
            }
        }

        public void Init(Structure structure)
        {
            Structure = structure;
        }

        public void SetRepeat(bool on) => Repeat = on;

        /// <summary>Выбрать вооружение новых отрядов (только открытое; уже заказанные не меняются)</summary>
        public void SetWeapon(SquadWeapon weapon)
        {
            if (Faction.Knows(weapon))
                Weapon = weapon;
        }

        public bool TryHire(out string reason)
        {
            if (MatchManager.Instance.IsOver)
            {
                reason = "Бой окончен";
                return false;
            }
            if (!Structure.IsBuilt)
            {
                reason = "Казармы ещё строятся";
                return false;
            }
            if (Queued >= UnitCatalog.BarracksQueueLimit)
            {
                reason = "Очередь найма заполнена";
                return false;
            }
            int cost = WeaponCatalog.SquadCost(Weapon);
            if (!Faction.CanAfford(cost, 0))
            {
                reason = "Не хватает ресурсов";
                return false;
            }

            Faction.Pay(cost, 0);
            queue.Add(Weapon);
            reason = null;
            return true;
        }

        public void SetBehavior(BarracksBehavior behavior)
        {
            if (Behavior == behavior)
                return;
            Behavior = behavior;
            foreach (var squad in Squads)
                squad.BehaviorChanged();
        }

        void Update()
        {
            Squads.RemoveAll(s => !s.IsAlive);

            // Постоянный найм: очередь опустела — заказать следующий, если хватает ресурсов
            if (Repeat && Queued == 0 && Structure.IsBuilt && !MatchManager.Instance.IsOver &&
                Faction.CanAfford(WeaponCatalog.SquadCost(Weapon), 0))
                TryHire(out _);

            if (!Structure.IsBuilt || Queued == 0)
                return;

            Progress += Time.deltaTime / UnitCatalog.SquadHireTime;
            if (Progress < 1f)
                return;

            // Выйти негде (NavMesh ещё не готов) — ждём с полным прогрессом
            var squad = MatchManager.Instance.SpawnSquad(Faction, this, Behavior, queue[0]);
            if (squad == null)
            {
                Progress = 1f;
                return;
            }

            Progress = 0f;
            queue.RemoveAt(0);
        }

        /// <summary>
        /// Казармы разрушены: деньги за очередь найма возвращаются, отряды остаются
        /// с последним поведением казарм
        /// </summary>
        public void OnDestroyed()
        {
            foreach (var weapon in queue)
                Faction.baseResource += WeaponCatalog.SquadCost(weapon);
            queue.Clear();
            foreach (var squad in Squads)
                squad.DetachFromBarracks(Behavior);
            Squads.Clear();
        }
    }
}
