using System.Collections.Generic;
using UnityEngine;

namespace Generals
{
    /// <summary>
    /// Казармы: очередь найма пехоты и поведение бойцов («Оборона» / «Атака»).
    /// Поведение задаётся на казармы и сразу действует на всех их бойцов, включая уже нанятых.
    /// Висит на здании казарм рядом со Structure.
    /// </summary>
    public class Barracks : MonoBehaviour
    {
        public Structure Structure { get; private set; }
        public BarracksBehavior Behavior { get; private set; } = BarracksBehavior.Defend;
        public int Queued { get; private set; }
        /// <summary>Прогресс найма текущего бойца, 0..1</summary>
        public float Progress { get; private set; }
        public readonly List<InfantryUnit> Units = new();

        public Faction Faction => Structure.Faction;

        public void Init(Structure structure)
        {
            Structure = structure;
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
            if (!Faction.CanAfford(UnitCatalog.InfantryCost, 0))
            {
                reason = "Не хватает ресурсов";
                return false;
            }

            Faction.Pay(UnitCatalog.InfantryCost, 0);
            Queued++;
            reason = null;
            return true;
        }

        public void SetBehavior(BarracksBehavior behavior)
        {
            if (Behavior == behavior)
                return;
            Behavior = behavior;
            foreach (var unit in Units)
                if (unit != null)
                    unit.BehaviorChanged();
        }

        void Update()
        {
            Units.RemoveAll(u => u == null);

            if (!Structure.IsBuilt || Queued == 0)
                return;

            Progress += Time.deltaTime / UnitCatalog.InfantryHireTime;
            if (Progress < 1f)
                return;

            // Выйти негде (NavMesh ещё не готов) — ждём с полным прогрессом
            var unit = MatchManager.Instance.SpawnInfantry(Faction, this, Behavior);
            if (unit == null)
            {
                Progress = 1f;
                return;
            }

            Progress = 0f;
            Queued--;
        }

        /// <summary>
        /// Казармы разрушены: деньги за очередь найма возвращаются, бойцы остаются
        /// с последним поведением казарм
        /// </summary>
        public void OnDestroyed()
        {
            Faction.baseResource += Queued * UnitCatalog.InfantryCost;
            Queued = 0;
            foreach (var unit in Units)
                if (unit != null)
                    unit.DetachFromBarracks(Behavior);
            Units.Clear();
        }
    }
}
