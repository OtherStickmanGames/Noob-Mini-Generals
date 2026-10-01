using System.Collections.Generic;
using UnityEngine;

namespace Generals
{
    /// <summary>
    /// Здание, которое выпускает войска с поведением «Оборона / Атака» и постоянным наймом: казармы
    /// и машинный завод. Над такими зданиями — плашка с режимом.
    /// </summary>
    public interface IUnitProducer
    {
        Structure Structure { get; }
        BarracksBehavior Behavior { get; }
        /// <summary>Включён постоянный найм</summary>
        bool Repeat { get; }
        void SetBehavior(BarracksBehavior behavior);
        Transform transform { get; }
    }

    /// <summary>
    /// Машинный завод (решение автора: техника как в Dawn of War, управление как у казарм): очередь
    /// производства машин, исследование танка и артиллерии прямо в заводе, поведение машин
    /// («Оборона» / «Атака» — сразу для всех его машин) и постоянное производство одного типа (ПКМ или
    /// долгое нажатие по машине). Висит на здании завода рядом со Structure.
    /// </summary>
    public class Factory : MonoBehaviour, IUnitProducer
    {
        // Выезд перекрыт — следующая попытка выпустить машину через столько секунд
        const float BlockedRetry = 1f;

        public Structure Structure { get; private set; }
        public BarracksBehavior Behavior { get; private set; } = BarracksBehavior.Defend;
        /// <summary>Постоянное производство этого типа: в очереди всегда машина, пока хватает ресурсов</summary>
        public VehicleType? RepeatType { get; private set; }
        public bool Repeat => RepeatType.HasValue;
        public int Queued => queue.Count;
        public IReadOnlyList<VehicleType> Queue => queue;
        /// <summary>Прогресс производства текущей машины, 0..1</summary>
        public float Progress { get; private set; }
        /// <summary>Что исследуется в этом заводе</summary>
        public VehicleType? Researching { get; private set; }
        public float ResearchProgress { get; private set; }
        /// <summary>Машина готова, но выехать к воротам нельзя (проезд перекрыт зданиями)</summary>
        public bool ExitBlocked { get; private set; }
        public readonly List<VehicleUnit> Vehicles = new();

        readonly List<VehicleType> queue = new();
        float blockedTimer;

        public Faction Faction => Structure.Faction;

        public void Init(Structure structure)
        {
            Structure = structure;
        }

        public int QueuedOf(VehicleType type)
        {
            int n = 0;
            foreach (var t in queue)
                if (t == type)
                    n++;
            return n;
        }

        public bool TryHire(VehicleType type, out string reason)
        {
            var def = VehicleCatalog.Get(type);
            if (MatchManager.Instance.IsOver)
                reason = "Бой окончен";
            else if (!Structure.IsBuilt)
                reason = "Завод ещё строится";
            else if (!Faction.KnowsVehicle(type))
                reason = $"Сначала изучите: {def.name}";
            else if (Queued >= VehicleCatalog.FactoryQueueLimit)
                reason = "Очередь производства заполнена";
            else if (!Faction.CanAfford(def.cost, 0))
                reason = "Не хватает ресурсов";
            else
                reason = null;
            if (reason != null)
                return false;

            Faction.Pay(def.cost, 0);
            queue.Add(type);
            return true;
        }

        /// <summary>Постоянное производство: включить для типа (выключить, если он уже включён)</summary>
        public void ToggleRepeat(VehicleType type)
        {
            RepeatType = RepeatType == type ? null : type;
        }

        public bool TryResearch(VehicleType type, out string reason)
        {
            var def = VehicleCatalog.Get(type);
            if (MatchManager.Instance.IsOver)
                reason = "Бой окончен";
            else if (!Structure.IsBuilt)
                reason = "Завод ещё строится";
            else if (Faction.KnowsVehicle(type))
                reason = $"{def.name} уже открыт";
            else if (Faction.vehicleResearching.HasValue)
                reason = $"Уже изучается: {VehicleCatalog.Get(Faction.vehicleResearching.Value).name}";
            else if (!Faction.CanAfford(def.researchCost, 0))
                reason = "Не хватает ресурсов";
            else
                reason = null;
            if (reason != null)
                return false;

            Faction.Pay(def.researchCost, 0);
            Researching = type;
            ResearchProgress = 0f;
            Faction.vehicleResearching = type;
            return true;
        }

        public void SetBehavior(BarracksBehavior behavior)
        {
            if (Behavior == behavior)
                return;
            Behavior = behavior;
            foreach (var v in Vehicles)
                if (v != null)
                    v.BehaviorChanged();
        }

        void Update()
        {
            Vehicles.RemoveAll(v => v == null || !v.IsAlive);
            if (!Structure.IsBuilt || MatchManager.Instance.IsOver)
                return;

            if (Researching.HasValue)
            {
                var def = VehicleCatalog.Get(Researching.Value);
                ResearchProgress += Time.deltaTime / def.researchTime;
                if (ResearchProgress >= 1f)
                {
                    if (!Faction.knownVehicles.Contains(def.type))
                        Faction.knownVehicles.Add(def.type);
                    Faction.vehicleResearching = null;
                    Researching = null;
                    ResearchProgress = 0f;
                }
            }

            // Постоянное производство: очередь опустела — заказать следующую, если хватает ресурсов
            if (RepeatType.HasValue && Queued == 0 && Faction.KnowsVehicle(RepeatType.Value) &&
                Faction.CanAfford(VehicleCatalog.Get(RepeatType.Value).cost, 0))
                TryHire(RepeatType.Value, out _);

            if (Queued == 0)
            {
                ExitBlocked = false;
                return;
            }

            Progress += Time.deltaTime / VehicleCatalog.Get(queue[0]).buildTime;
            if (Progress < 1f)
                return;
            Progress = 1f;

            // Готовая машина ждёт, пока можно выехать (NavMesh не готов или проезд перекрыт)
            blockedTimer -= Time.deltaTime;
            if (blockedTimer > 0f)
                return;
            var vehicle = MatchManager.Instance.SpawnVehicle(Faction, this, Behavior, queue[0], out bool blocked);
            if (vehicle == null)
            {
                ExitBlocked = blocked;
                blockedTimer = BlockedRetry;
                return;
            }

            ExitBlocked = false;
            Progress = 0f;
            queue.RemoveAt(0);
        }

        /// <summary>
        /// Завод разрушен: деньги за очередь и незаконченное исследование возвращаются, машины остаются
        /// с последним поведением завода
        /// </summary>
        public void OnDestroyed()
        {
            foreach (var type in queue)
                Faction.baseResource += VehicleCatalog.Get(type).cost;
            queue.Clear();
            if (Researching.HasValue)
            {
                Faction.baseResource += VehicleCatalog.Get(Researching.Value).researchCost;
                if (Faction.vehicleResearching == Researching)
                    Faction.vehicleResearching = null;
                Researching = null;
            }
            foreach (var v in Vehicles)
                if (v != null)
                    v.DetachFromFactory(Behavior);
            Vehicles.Clear();
        }
    }
}
