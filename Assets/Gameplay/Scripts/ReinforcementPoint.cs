using System.Collections.Generic;
using UnityEngine;

namespace Generals
{
    /// <summary>
    /// Пункт подкрепления: пополняет неполные отряды (решение автора — отдельная постройка, не
    /// казармы). У каждого отряда в панели пункта кнопка «Пополнить»: оплата сразу за всех
    /// недостающих, дальше бойцы по одному выходят из пункта (UnitCatalog.ReinforceTime на бойца)
    /// и бегут к своему отряду, где бы он ни был. Отряд погиб до прибытия или пункт разрушен —
    /// деньги за невыданных бойцов возвращаются. Висит на здании рядом со Structure.
    /// </summary>
    public class ReinforcementPoint : MonoBehaviour
    {
        public Structure Structure { get; private set; }
        /// <summary>Прогресс текущего бойца, 0..1</summary>
        public float Progress { get; private set; }

        // Очередь: по бойцу на запись, отряды в порядке заказа
        readonly List<Squad> queue = new();

        public Faction Faction => Structure.Faction;
        public int Queued => queue.Count;

        public void Init(Structure structure)
        {
            Structure = structure;
        }

        /// <summary>Сколько бойцов не хватает отряду с учётом уже заказанных</summary>
        public static int Missing(Squad squad) =>
            Mathf.Max(0, UnitCatalog.SquadSize - squad.Members.Count - squad.PendingReinforcements);

        public bool TryReinforce(Squad squad, out string reason)
        {
            int missing = squad != null && squad.IsAlive ? Missing(squad) : 0;
            if (MatchManager.Instance.IsOver)
                reason = "Бой окончен";
            else if (!Structure.IsBuilt)
                reason = "Пункт подкрепления ещё строится";
            else if (missing == 0)
                reason = "Отряд полный";
            else if (!Faction.CanAfford(missing * UnitCatalog.ReinforceCost, 0))
                reason = "Не хватает ресурсов";
            else
                reason = null;
            if (reason != null)
                return false;

            Faction.Pay(missing * UnitCatalog.ReinforceCost, 0);
            for (int i = 0; i < missing; i++)
                queue.Add(squad);
            squad.PendingReinforcements += missing;
            return true;
        }

        void Update()
        {
            // Погибшие отряды из очереди — с возвратом денег
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                if (queue[i].IsAlive)
                    continue;
                queue[i].PendingReinforcements--;
                Faction.baseResource += UnitCatalog.ReinforceCost;
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
            var squad = queue[0];
            if (!MatchManager.Instance.SpawnReinforcement(this, squad))
            {
                Progress = 1f;
                return;
            }

            squad.PendingReinforcements--;
            queue.RemoveAt(0);
            Progress = 0f;
        }

        /// <summary>Пункт разрушен: деньги за невыданных бойцов возвращаются</summary>
        public void OnDestroyed()
        {
            foreach (var squad in queue)
            {
                squad.PendingReinforcements--;
                Faction.baseResource += UnitCatalog.ReinforceCost;
            }
            queue.Clear();
        }
    }
}
