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
