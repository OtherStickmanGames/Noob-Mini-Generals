using System.Collections.Generic;
using Unity.Mathematics;

namespace Generals
{
    public enum StructureType
    {
        Headquarters,
        Extractor,
        Barracks,
        Turret,
    }

    public enum PlacementRule
    {
        /// <summary>Внутри стен текущего уровня</summary>
        InsideWalls,
        /// <summary>На ровной площадке базы (оборонительные постройки)</summary>
        BaseArea,
        /// <summary>Центром на месторождение внутри стен или на свою точку захвата</summary>
        Deposit,
    }

    public class StructureDef
    {
        public StructureType type;
        public string name;
        /// <summary>Размер в клетках земли (0.5 м)</summary>
        public int2 footprint;
        public int costBase;
        public int costValuable;
        /// <summary>Секунды работы одного строителя</summary>
        public float buildTime;
        public float health;
        public PlacementRule rule;
        /// <summary>Можно строить из меню (главное здание — нельзя)</summary>
        public bool buildable;
    }

    /// <summary>
    /// Здания вертикального среза. Цифры предварительные, баланс позже.
    /// </summary>
    public static class StructureCatalog
    {
        static readonly Dictionary<StructureType, StructureDef> defs = new()
        {
            [StructureType.Headquarters] = new StructureDef
            {
                type = StructureType.Headquarters,
                name = "Главное здание",
                footprint = new int2(8, 8),
                buildTime = 60f,
                health = 3000f,
                rule = PlacementRule.InsideWalls,
                buildable = false,
            },
            [StructureType.Extractor] = new StructureDef
            {
                type = StructureType.Extractor,
                name = "Добытчик",
                footprint = new int2(3, 3),
                costBase = 100,
                buildTime = 12f,
                health = 500f,
                rule = PlacementRule.Deposit,
                buildable = true,
            },
            [StructureType.Barracks] = new StructureDef
            {
                type = StructureType.Barracks,
                name = "Казармы",
                footprint = new int2(6, 8),
                costBase = 150,
                buildTime = 20f,
                health = 1200f,
                rule = PlacementRule.InsideWalls,
                buildable = true,
            },
            [StructureType.Turret] = new StructureDef
            {
                type = StructureType.Turret,
                name = "Турель",
                footprint = new int2(2, 2),
                costBase = 120,
                buildTime = 10f,
                health = 600f,
                rule = PlacementRule.BaseArea,
                buildable = true,
            },
        };

        public static StructureDef Get(StructureType type) => defs[type];

        public static IEnumerable<StructureDef> All => defs.Values;

        // Экономика
        public const int StartBase = 250;
        public const int StartValuable = 0;
        public const float HeadquartersIncome = 1f;          // базовый ресурс в секунду
        public const float ExtractorBaseIncome = 2f;         // с месторождения на базе
        public const float CapturePointIncome = 0.5f;        // ценный — за захваченную точку
        public const float ExtractorValuableIncome = 1.5f;   // ценный — с добытчика на точке

        public const int BuilderCost = 50;
        public const float BuilderHireTime = 8f;
    }
}
