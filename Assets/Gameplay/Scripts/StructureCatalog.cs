using System.Collections.Generic;
using Unity.Mathematics;

namespace Generals
{
    public enum StructureType
    {
        Headquarters,
        Extractor,
        Mine,
        Barracks,
        Turret,
        ReinforcementPoint,
        Armoury,
    }

    public enum PlacementRule
    {
        /// <summary>Внутри стен текущего уровня</summary>
        InsideWalls,
        /// <summary>На ровной площадке базы (оборонительные постройки)</summary>
        BaseArea,
        /// <summary>Центром на свою точку захвата</summary>
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
                rule = PlacementRule.InsideWalls,
                buildable = true,
            },
            [StructureType.Mine] = new StructureDef
            {
                type = StructureType.Mine,
                name = "Шахта",
                footprint = new int2(3, 3),
                costBase = 150,
                buildTime = 15f,
                health = 600f,
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
            // Пополнение отрядов (решение автора: отдельная постройка, не казармы)
            [StructureType.ReinforcementPoint] = new StructureDef
            {
                type = StructureType.ReinforcementPoint,
                name = "Пункт подкрепления",
                footprint = new int2(5, 5),
                costBase = 150,
                buildTime = 20f,
                health = 900f,
                rule = PlacementRule.InsideWalls,
                buildable = true,
            },
            // Исследования спецоружия для отрядов (вариант «2 из 4»)
            [StructureType.Armoury] = new StructureDef
            {
                type = StructureType.Armoury,
                name = "Оружейная",
                footprint = new int2(5, 6),
                costBase = 200,
                buildTime = 25f,
                health = 1000f,
                rule = PlacementRule.InsideWalls,
                buildable = true,
            },
        };

        public static StructureDef Get(StructureType type) => defs[type];

        public static IEnumerable<StructureDef> All => defs.Values;

        // Экономика
        public const int StartBase = 250;
        public const int StartValuable = 0;
        public const float HeadquartersIncome = 1f;          // базовый ресурс в секунду
        public const float ExtractorIncome = 2f;             // базовый — с добытчика (ставится где угодно внутри стен)
        public const float CapturePointIncome = 0.5f;        // ценный — за захваченную точку
        public const float MineIncome = 1.5f;                // ценный — с шахты на своей точке захвата

        // Стены: участок — квадрат клеток такого размера, у него своя прочность
        public const int WallSegmentCells = 4;
        public const float WallSegmentHealth = 400f;

        public const int BuilderCost = 50;
        public const float BuilderHireTime = 8f;
    }
}
