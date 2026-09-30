using UnityEngine;

namespace Generals
{
    /// <summary>
    /// Оружейная: исследования спецоружия (вариант «2 из 4», выбран автором). Открыто может быть не
    /// больше WeaponCatalog.MaxKnownSpecials; изучая ещё одно, игрок выбирает, какое из открытых
    /// забыть (оно забывается, когда новое изучено). У стороны — одно исследование за раз.
    /// Уже нанятые отряды забытое оружие сохраняют, новых с ним не нанять.
    /// Висит на здании рядом со Structure.
    /// </summary>
    public class Armoury : MonoBehaviour
    {
        public Structure Structure { get; private set; }
        /// <summary>Что изучается в этой оружейной; null — ничего</summary>
        public SquadWeapon? Researching { get; private set; }
        /// <summary>Что будет забыто, когда исследование закончится</summary>
        public SquadWeapon? Replacing { get; private set; }
        public float Progress { get; private set; }

        public Faction Faction => Structure.Faction;

        public void Init(Structure structure)
        {
            Structure = structure;
        }

        /// <summary>Нужно ли для этого исследования забыть одно из открытых</summary>
        public bool NeedsReplacement => Faction.knownWeapons.Count >= WeaponCatalog.MaxKnownSpecials;

        public bool TryResearch(SquadWeapon weapon, SquadWeapon? replace, out string reason)
        {
            var special = WeaponCatalog.Special(weapon);
            if (MatchManager.Instance.IsOver)
                reason = "Бой окончен";
            else if (!Structure.IsBuilt)
                reason = "Оружейная ещё строится";
            else if (special == null || Faction.Knows(weapon))
                reason = "Уже изучено";
            else if (Faction.researching.HasValue)
                reason = "Уже идёт исследование";
            else if (NeedsReplacement && (!replace.HasValue || !Faction.knownWeapons.Contains(replace.Value)))
                reason = "Выберите, что забыть";
            else if (!Faction.CanAfford(special.researchCost, 0))
                reason = "Не хватает ресурсов";
            else
                reason = null;
            if (reason != null)
                return false;

            Faction.Pay(special.researchCost, 0);
            Researching = weapon;
            Replacing = NeedsReplacement ? replace : null;
            Faction.researching = weapon;
            Progress = 0f;
            return true;
        }

        void Update()
        {
            if (!Researching.HasValue || !Structure.IsBuilt)
                return;

            Progress += Time.deltaTime / WeaponCatalog.Special(Researching.Value).researchTime;
            if (Progress < 1f)
                return;

            Faction.Learn(Researching.Value, Replacing);
            Faction.researching = null;
            Researching = null;
            Replacing = null;
            Progress = 0f;
        }

        /// <summary>Оружейная разрушена: незаконченное исследование отменяется, деньги возвращаются</summary>
        public void OnDestroyed()
        {
            if (!Researching.HasValue)
                return;
            Faction.baseResource += WeaponCatalog.Special(Researching.Value).researchCost;
            Faction.researching = null;
            Researching = null;
            Replacing = null;
        }
    }
}
