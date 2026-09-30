using System.Collections.Generic;

namespace Generals
{
    /// <summary>
    /// Сторона в бою: ресурсы, уровень стен, здания и строители.
    /// team 0 — база один (игрок), team 1 — база два (противник).
    /// </summary>
    public class Faction
    {
        public readonly int team;
        public readonly bool isPlayer;

        public float baseResource = StructureCatalog.StartBase;
        public float valuable = StructureCatalog.StartValuable;
        public int wallLevel = 1;

        public Structure headquarters;
        public readonly List<Structure> structures = new();
        public readonly List<BuilderUnit> builders = new();
        public readonly List<InfantryUnit> units = new();
        /// <summary>Отряды пехоты (бойцы — в units)</summary>
        public readonly List<Squad> squads = new();
        /// <summary>Сколько отрядов создано за бой — для их номеров</summary>
        public int squadsCreated;

        /// <summary>Открытое спецоружие (не больше WeaponCatalog.MaxKnownSpecials)</summary>
        public readonly List<SquadWeapon> knownWeapons = new();
        /// <summary>Что сейчас изучается в оружейной (у стороны — одно исследование за раз)</summary>
        public SquadWeapon? researching;

        public bool Knows(SquadWeapon weapon) => weapon == SquadWeapon.Rifle || knownWeapons.Contains(weapon);

        /// <summary>
        /// Исследование закончено: новое оружие открыто, replaced (если было) — забыто. Казармы, где было
        /// выбрано забытое оружие, переходят на винтовки; уже нанятые отряды своё оружие сохраняют
        /// </summary>
        public void Learn(SquadWeapon weapon, SquadWeapon? replaced)
        {
            if (replaced.HasValue && knownWeapons.Remove(replaced.Value))
            {
                foreach (var s in structures)
                    if (s != null && s.Barracks != null && s.Barracks.Weapon == replaced.Value)
                        s.Barracks.SetWeapon(SquadWeapon.Rifle);
            }
            if (!knownWeapons.Contains(weapon))
                knownWeapons.Add(weapon);
        }
        /// <summary>Участки стены базы (цели для противника)</summary>
        public readonly List<WallSegment> walls = new();

        // Найм строителей в главном здании
        public int buildersQueued;
        public float hireProgress;

        // Итоги боя
        public int unitsHired;
        public int unitsLost;
        public int structuresBuilt;
        public int structuresLost;

        public Faction(int team, bool isPlayer)
        {
            this.team = team;
            this.isPlayer = isPlayer;
        }

        public int BaseResource => (int)baseResource;
        public int Valuable => (int)valuable;

        public bool CanAfford(int costBase, int costValuable) =>
            baseResource >= costBase && valuable >= costValuable;

        public void Pay(int costBase, int costValuable)
        {
            baseResource -= costBase;
            valuable -= costValuable;
        }
    }
}
