using UnityEngine;
using static VoxelBlocks;

public enum ArenaBiome
{
    Summer,
    Autumn,
    Winter,
    Desert,
}

public enum ArenaBiomeChoice
{
    BySeed,
    Summer,
    Autumn,
    Winter,
    Desert,
}

/// <summary>
/// Биом меняет только цвета палитры, форму деревьев и немного плотность деталей.
/// Структура карты у всех биомов одна.
/// </summary>
public static class ArenaBiomes
{
    public static ArenaBiome Resolve(ArenaBiomeChoice choice, int seed)
    {
        if (choice != ArenaBiomeChoice.BySeed)
            return (ArenaBiome)((int)choice - 1);

        uint hash = Unity.Mathematics.math.hash(new Unity.Mathematics.int2(seed, 19));
        return (ArenaBiome)(hash % 4);
    }

    public static string DisplayName(ArenaBiome biome) => biome switch
    {
        ArenaBiome.Summer => "лето",
        ArenaBiome.Autumn => "осень",
        ArenaBiome.Winter => "зима",
        _ => "пустыня",
    };

    /// <summary>С какой высоты вершины гор под снегом</summary>
    public static int SnowLine(ArenaBiome biome, int highHeight) =>
        biome == ArenaBiome.Winter ? highHeight + 3 : highHeight + 15;

    public static Color FogColor(ArenaBiome biome) => biome switch
    {
        ArenaBiome.Summer => new Color32(170, 200, 226, 255),
        ArenaBiome.Autumn => new Color32(200, 186, 162, 255),
        ArenaBiome.Winter => new Color32(212, 222, 234, 255),
        _ => new Color32(226, 206, 170, 255),
    };

    // Множители к настройкам генерации
    public static float WaterThresholdBonus(ArenaBiome biome) => biome == ArenaBiome.Desert ? 0.12f : 0f;
    public static float ForestDensityScale(ArenaBiome biome) => biome == ArenaBiome.Desert ? 0.25f : 1f;
    public static float RockChanceScale(ArenaBiome biome) => biome == ArenaBiome.Desert ? 2.5f : 1f;

    public static Color32[] Palette(ArenaBiome biome)
    {
        var p = new Color32[SlotCount];
        p[SlotBedrock] = new Color32(52, 52, 58, 255);
        p[SlotGoldOre] = new Color32(236, 196, 52, 255);
        p[SlotIronOre] = new Color32(170, 108, 88, 255);
        p[SlotSnow] = new Color32(242, 246, 250, 255);
        p[SlotMarker] = new Color32(232, 232, 226, 255);
        p[SlotTeamOne] = new Color32(58, 110, 214, 255);
        p[SlotTeamOneDark] = new Color32(34, 62, 128, 255);
        p[SlotTeamTwo] = new Color32(208, 58, 48, 255);
        p[SlotTeamTwoDark] = new Color32(118, 32, 28, 255);
        p[SlotMetal] = new Color32(118, 124, 132, 255);
        p[SlotWallTop] = new Color32(152, 148, 142, 255);
        p[SlotWallSide] = new Color32(122, 118, 112, 255);

        switch (biome)
        {
            case ArenaBiome.Summer:
                p[SlotStoneTop] = new Color32(128, 130, 136, 255);
                p[SlotStoneSide] = new Color32(104, 106, 112, 255);
                p[SlotDirt] = new Color32(123, 88, 60, 255);
                p[SlotGrassTop] = new Color32(104, 168, 62, 255);
                p[SlotSand] = new Color32(218, 198, 140, 255);
                p[SlotWater] = new Color32(64, 124, 204, 255);
                p[SlotWoodSide] = new Color32(106, 76, 48, 255);
                p[SlotWoodTop] = new Color32(150, 115, 75, 255);
                p[SlotLeaves] = new Color32(58, 136, 50, 255);
                p[SlotLeavesAlt] = new Color32(84, 158, 58, 255);
                break;

            case ArenaBiome.Autumn:
                p[SlotStoneTop] = new Color32(126, 124, 124, 255);
                p[SlotStoneSide] = new Color32(102, 100, 100, 255);
                p[SlotDirt] = new Color32(112, 78, 54, 255);
                p[SlotGrassTop] = new Color32(150, 148, 66, 255);
                p[SlotSand] = new Color32(208, 186, 132, 255);
                p[SlotWater] = new Color32(56, 100, 166, 255);
                p[SlotWoodSide] = new Color32(96, 68, 44, 255);
                p[SlotWoodTop] = new Color32(140, 106, 70, 255);
                p[SlotLeaves] = new Color32(216, 116, 38, 255);
                p[SlotLeavesAlt] = new Color32(192, 62, 40, 255);
                break;

            case ArenaBiome.Winter:
                p[SlotStoneTop] = new Color32(150, 160, 172, 255);
                p[SlotStoneSide] = new Color32(116, 124, 138, 255);
                p[SlotDirt] = new Color32(94, 80, 72, 255);
                p[SlotGrassTop] = new Color32(234, 240, 246, 255);
                p[SlotSand] = new Color32(168, 172, 178, 255);
                p[SlotWater] = new Color32(126, 176, 214, 255);
                p[SlotWoodSide] = new Color32(86, 64, 46, 255);
                p[SlotWoodTop] = new Color32(128, 98, 70, 255);
                p[SlotLeaves] = new Color32(38, 86, 58, 255);
                p[SlotLeavesAlt] = new Color32(50, 100, 70, 255);
                p[SlotWallTop] = new Color32(172, 178, 188, 255);
                p[SlotWallSide] = new Color32(138, 144, 156, 255);
                break;

            default: // Desert
                p[SlotStoneTop] = new Color32(190, 120, 80, 255);
                p[SlotStoneSide] = new Color32(166, 98, 64, 255);
                p[SlotDirt] = new Color32(204, 150, 96, 255);
                p[SlotGrassTop] = new Color32(226, 196, 132, 255);
                p[SlotSand] = new Color32(240, 222, 168, 255);
                p[SlotWater] = new Color32(60, 172, 182, 255);
                p[SlotWoodSide] = new Color32(120, 90, 60, 255);
                p[SlotWoodTop] = new Color32(150, 116, 80, 255);
                p[SlotLeaves] = new Color32(76, 138, 68, 255);
                p[SlotLeavesAlt] = new Color32(96, 160, 82, 255);
                p[SlotWallTop] = new Color32(210, 174, 124, 255);
                p[SlotWallSide] = new Color32(182, 146, 100, 255);
                break;
        }

        return p;
    }
}
