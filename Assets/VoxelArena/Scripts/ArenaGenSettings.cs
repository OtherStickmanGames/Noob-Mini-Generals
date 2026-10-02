using System;
using UnityEngine;

/// <summary>
/// Настройки генерации арены. Размеры и высоты — в вокселях (клетках).
/// </summary>
[Serializable]
public struct ArenaGenSettings
{
    // Меняется, когда в настройках появляются новые поля:
    // сцена со старой версией получает значения по умолчанию
    public const int CurrentVersion = 10;

    [HideInInspector] public int version;
    public int seed;
    public ArenaBiomeChoice biome;

    [Header("Горы по краю карты")]
    [Tooltip("Ширина гор меняется по шуму от минимума до максимума — край неровный")]
    public int borderMinWidth;
    public int borderMaxWidth;
    public float borderNoiseScale;
    [Tooltip("За сколько клеток от подножия горы набирают полную высоту")]
    public int mountainRise;
    [Tooltip("Высота гор над верхним уровнем, в блоках")]
    public int mountainHeight;

    [Header("Уровни высоты")]
    public int lowHeight;
    public int midHeight;
    public int highHeight;
    [Range(0f, 1f)] public float lowShare;
    [Range(0f, 1f)] public float highShare;
    public float levelNoiseScale;
    [Tooltip("Участки меньше этой площади вливаются в соседние")]
    public int minRegionCells;

    [Header("База: стены (внутренняя сторона квадрата по уровням)")]
    public int wallLevel1Size;
    public int wallLevel2Size;
    public int wallLevel3Size;
    public int wallThickness;
    public int wallHeight;
    public int gateWidth;
    [Tooltip("Ровный отступ вокруг стен 3-го уровня")]
    public int basePadding;
    [Tooltip("Сколько клеток поля остаётся за базой до гор")]
    public int baseBackSpace;

    [Header("Рампы")]
    public int rampWidth;
    [Range(0f, 1f)] public float extraRampChance;

    [Header("Вода")]
    public float waterNoiseScale;
    public float waterThreshold;
    public int minLakeCells;

    [Header("Лес и камни")]
    public float forestNoiseScale;
    public float forestThreshold;
    [Range(0f, 1f)] public float forestDensity;
    [Range(0f, 0.1f)] public float rockChance;

    public static ArenaGenSettings Default => new()
    {
        version = CurrentVersion,
        seed = 1,
        // Пустыня — выбор автора; другой биом или «по сиду» — в инспекторе арены
        biome = ArenaBiomeChoice.Desert,
        borderMinWidth = 8,
        borderMaxWidth = 44,
        borderNoiseScale = 0.022f,
        mountainRise = 12,
        mountainHeight = 20,
        lowHeight = 6,
        midHeight = 10,
        highHeight = 15,
        lowShare = 0.33f,
        highShare = 0.24f,
        levelNoiseScale = 0.02f,
        minRegionCells = 90,
        // Стартовая база 22×22 м (было 16×16): с проходами 3 м между зданиями на ней помещаются
        // казармы, завод и остальное (решение автора 02.10.2026)
        wallLevel1Size = 44,
        wallLevel2Size = 52,
        wallLevel3Size = 60,
        wallThickness = 2,
        wallHeight = 4,
        // Ворота и рампы — под технику (танк ~2 м, решение автора 01.10.2026)
        gateWidth = 10,
        basePadding = 4,
        baseBackSpace = 50,
        rampWidth = 10,
        extraRampChance = 0.35f,
        waterNoiseScale = 0.03f,
        waterThreshold = 0.12f,
        minLakeCells = 25,
        forestNoiseScale = 0.05f,
        forestThreshold = 0.18f,
        forestDensity = 0.55f,
        rockChance = 0.012f,
    };
}
