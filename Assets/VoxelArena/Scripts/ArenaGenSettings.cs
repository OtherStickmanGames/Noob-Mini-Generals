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
    public const int CurrentVersion = 4;

    [HideInInspector] public int version;
    public int seed;
    public ArenaBiomeChoice biome;

    [Header("Горы по краю карты")]
    public int borderWidth;
    [Tooltip("На сколько блоков горы растут на клетку ближе к краю")]
    public float borderSlope;

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
        biome = ArenaBiomeChoice.BySeed,
        borderWidth = 14,
        borderSlope = 1.2f,
        lowHeight = 6,
        midHeight = 10,
        highHeight = 15,
        lowShare = 0.33f,
        highShare = 0.24f,
        levelNoiseScale = 0.02f,
        minRegionCells = 90,
        wallLevel1Size = 24,
        wallLevel2Size = 36,
        wallLevel3Size = 48,
        wallThickness = 2,
        wallHeight = 4,
        gateWidth = 4,
        basePadding = 4,
        rampWidth = 5,
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
