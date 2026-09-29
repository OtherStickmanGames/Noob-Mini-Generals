using System;
using UnityEngine;

/// <summary>
/// Настройки генерации арены. Размеры и высоты — в вокселях (клетках).
/// </summary>
[Serializable]
public struct ArenaGenSettings
{
    public int seed;
    public ArenaBiomeChoice biome;

    [Header("Уровни высоты")]
    public int lowHeight;
    public int midHeight;
    public int highHeight;
    [Range(0f, 1f)] public float lowShare;
    [Range(0f, 1f)] public float highShare;
    public float levelNoiseScale;
    [Tooltip("Участки меньше этой площади вливаются в соседние")]
    public int minRegionCells;

    [Header("Базы и ресурсы")]
    public int baseMargin;
    public int baseRadius;

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
        seed = 1,
        biome = ArenaBiomeChoice.BySeed,
        lowHeight = 6,
        midHeight = 10,
        highHeight = 15,
        lowShare = 0.33f,
        highShare = 0.24f,
        levelNoiseScale = 0.022f,
        minRegionCells = 90,
        baseMargin = 16,
        baseRadius = 11,
        rampWidth = 5,
        extraRampChance = 0.35f,
        waterNoiseScale = 0.035f,
        waterThreshold = 0.12f,
        minLakeCells = 25,
        forestNoiseScale = 0.05f,
        forestThreshold = 0.18f,
        forestDensity = 0.55f,
        rockChance = 0.012f,
    };
}
