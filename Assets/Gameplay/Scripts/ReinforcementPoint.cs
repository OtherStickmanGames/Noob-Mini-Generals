using System.Collections.Generic;
using UnityEngine;

namespace Generals
{
    /// <summary>
    /// Пункт подкрепления: пополняет неполные отряды (решение автора — отдельная постройка, не
    /// казармы). У каждого отряда в панели пункта кнопка «Пополнить»: оплата сразу за всех
    /// недостающих, дальше бойцы по одному выходят из пункта (UnitCatalog.ReinforceTime на бойца)
    /// и бегут к своему отряду, где бы он ни был. Сначала восстанавливаются спецбойцы (если
    /// спецоружие отряда ещё открыто — WeaponCatalog.SpecialsPerSquad на отряд), они дороже.
    /// Отряд погиб до прибытия или пункт разрушен — деньги за невыданных бойцов возвращаются.
    /// Висит на здании рядом со Structure.
    /// </summary>
    public class ReinforcementPoint : MonoBehaviour
    {
        public Structure Structure { get; private set; }
        /// <summary>Прогресс текущего бойца, 0..1</summary>
        public float Progress { get; private set; }

        struct Order
        {
            public Squad squad;
            public SquadWeapon weapon;
        }

        // Очередь: по бойцу на запись, отряды в порядке заказа
        readonly List<Order> queue = new();

        public Faction Faction => Structure.Faction;
        public int Queued => queue.Count;

        public void Init(Structure structure)
        {
            Structure = structure;
        }

        /// <summary>Сколько бойцов не хватает отряду с учётом уже заказанных</summary>
        public static int Missing(Squad squad) =>
            Mathf.Max(0, UnitCatalog.SquadSize - squad.Members.Count - squad.PendingReinforcements);

        /// <summary>Сколько из недостающих будут со спецоружием (оно должно быть ещё открыто)</summary>
        public static int MissingSpecials(Squad squad)
        {
            if (squad.Weapon == SquadWeapon.Rifle || !squad.Faction.Knows(squad.Weapon))
                return 0;
            int need = WeaponCatalog.SpecialsPerSquad - squad.SpecialCount - squad.PendingSpecials;
            return Mathf.Clamp(need, 0, Missing(squad));
        }

        /// <summary>Цена пополнения отряда до полного</summary>
        public static int Cost(Squad squad)
        {
            int missing = Missing(squad), specials = MissingSpecials(squad);
            return (missing - specials) * WeaponCatalog.ReinforceCost(SquadWeapon.Rifle) +
                   specials * (specials > 0 ? WeaponCatalog.ReinforceCost(squad.Weapon) : 0);
        }

        public bool TryReinforce(Squad squad, out string reason)
        {
            int missing = squad != null && squad.IsAlive ? Missing(squad) : 0;
            int cost = missing > 0 ? Cost(squad) : 0;
            if (MatchManager.Instance.IsOver)
                reason = "Бой окончен";
            else if (!Structure.IsBuilt)
                reason = "Пункт подкрепления ещё строится";
            else if (missing == 0)
                reason = "Отряд полный";
            else if (!Faction.CanAfford(cost, 0))
                reason = "Не хватает ресурсов";
            else
                reason = null;
            if (reason != null)
                return false;

            Faction.Pay(cost, 0);
            int specials = MissingSpecials(squad);
            for (int i = 0; i < missing; i++)
                queue.Add(new Order { squad = squad, weapon = i < specials ? squad.Weapon : SquadWeapon.Rifle });
            squad.PendingReinforcements += missing;
            squad.PendingSpecials += specials;
            return true;
        }

        void Update()
        {
            // Погибшие отряды из очереди — с возвратом денег
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                if (queue[i].squad.IsAlive)
                    continue;
                Cancel(queue[i]);
                queue.RemoveAt(i);
            }

            if (!Structure.IsBuilt || queue.Count == 0)
            {
                Progress = 0f;
                return;
            }

            Progress += Time.deltaTime / UnitCatalog.ReinforceTime;
            if (Progress < 1f)
                return;

            // Выйти негде (NavMesh ещё не готов) — ждём с полным прогрессом
            var order = queue[0];
            if (!MatchManager.Instance.SpawnReinforcement(this, order.squad, order.weapon))
            {
                Progress = 1f;
                return;
            }

            order.squad.PendingReinforcements--;
            if (order.weapon != SquadWeapon.Rifle)
                order.squad.PendingSpecials--;
            queue.RemoveAt(0);
            Progress = 0f;
        }

        void Cancel(Order order)
        {
            order.squad.PendingReinforcements--;
            if (order.weapon != SquadWeapon.Rifle)
                order.squad.PendingSpecials--;
            Faction.baseResource += WeaponCatalog.ReinforceCost(order.weapon);
        }

        /// <summary>Пункт разрушен: деньги за невыданных бойцов возвращаются</summary>
        public void OnDestroyed()
        {
            foreach (var order in queue)
                Cancel(order);
            queue.Clear();
        }
    }
}
