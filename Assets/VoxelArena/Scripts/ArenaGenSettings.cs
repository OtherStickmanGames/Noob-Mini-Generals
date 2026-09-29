using System;

/// <summary>
/// Настройки генерации арены. Все размеры и высоты — в вокселях.
/// </summary>
[Serializable]
public struct ArenaGenSettings
{
    public int seed;

    // Рельеф
    public int groundLevel;
    public int hillHeight;
    public int terraceCount;
    public float noiseScale;
    public int octaves;

    // Пологие участки (рампы) между уровнями
    public float rampScale;
    public float rampThreshold;

    // Дорога между базами
    public float roadWidth;
    public float roadAmplitude;
    public float roadWaves;

    // Базы
    public int baseMargin;
    public float baseRadius;
    public float baseFalloff;
    public int baseHeight;

    public int sandLevel;

    public static ArenaGenSettings Default => new()
    {
        seed = 1,
        groundLevel = 6,
        hillHeight = 16,
        terraceCount = 4,
        noiseScale = 0.02f,
        octaves = 3,
        rampScale = 0.04f,
        rampThreshold = 0.3f,
        roadWidth = 3f,
        roadAmplitude = 20f,
        roadWaves = 1f,
        baseMargin = 16,
        baseRadius = 12f,
        baseFalloff = 10f,
        baseHeight = 10,
        sandLevel = 6,
    };
}
