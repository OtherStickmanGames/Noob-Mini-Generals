using Unity.Collections;
using UnityEngine;

/// <summary>
/// Типы блоков арены. Блок хранит только смысл (трава, камень, вода),
/// цвет грани берётся из палитры биома по индексу слота.
/// </summary>
public static class VoxelBlocks
{
    public const byte Air = 0;
    public const byte Bedrock = 1;
    public const byte Stone = 2;
    public const byte Dirt = 3;
    public const byte Grass = 4;
    public const byte Sand = 5;
    public const byte Water = 6;
    public const byte Wood = 7;
    public const byte Leaves = 8;
    public const byte LeavesAlt = 9;
    public const byte Snow = 10;
    public const byte GoldOre = 11;
    public const byte IronOre = 12;
    public const byte Wall = 13;
    public const byte Marker = 14;
    // Блоки моделей зданий и юнитов
    public const byte TeamOne = 15;
    public const byte TeamTwo = 16;
    public const byte TeamOneDark = 17;
    public const byte TeamTwoDark = 18;
    public const byte Metal = 19;
    public const int Count = 20;

    public const int FaceTop = 0;
    public const int FaceSide = 1;
    public const int FaceBottom = 2;

    // Слоты палитры. 32 и дальше оставлены под цвета команд.
    public const byte SlotBedrock = 1;
    public const byte SlotStoneTop = 2;
    public const byte SlotStoneSide = 3;
    public const byte SlotDirt = 4;
    public const byte SlotGrassTop = 5;
    public const byte SlotSand = 6;
    public const byte SlotWater = 7;
    public const byte SlotWoodSide = 8;
    public const byte SlotWoodTop = 9;
    public const byte SlotLeaves = 10;
    public const byte SlotLeavesAlt = 11;
    public const byte SlotSnow = 12;
    public const byte SlotGoldOre = 13;
    public const byte SlotIronOre = 14;
    public const byte SlotWallTop = 15;
    public const byte SlotWallSide = 16;
    public const byte SlotMarker = 17;
    public const byte SlotTeamOne = 32;
    public const byte SlotTeamTwo = 33;
    public const byte SlotTeamOneDark = 34;
    public const byte SlotTeamTwoDark = 35;
    public const byte SlotMetal = 36;
    public const int SlotCount = 40;

    // top, side, bottom
    static readonly byte[,] faces =
    {
        { 0, 0, 0 },                                        // Air
        { SlotBedrock, SlotBedrock, SlotBedrock },          // Bedrock
        { SlotStoneTop, SlotStoneSide, SlotStoneSide },     // Stone
        { SlotDirt, SlotDirt, SlotDirt },                   // Dirt
        { SlotGrassTop, SlotDirt, SlotDirt },               // Grass
        { SlotSand, SlotSand, SlotSand },                   // Sand
        { SlotWater, SlotWater, SlotWater },                // Water
        { SlotWoodTop, SlotWoodSide, SlotWoodTop },         // Wood
        { SlotLeaves, SlotLeaves, SlotLeaves },             // Leaves
        { SlotLeavesAlt, SlotLeavesAlt, SlotLeavesAlt },    // LeavesAlt
        { SlotSnow, SlotSnow, SlotSnow },                   // Snow
        { SlotGoldOre, SlotGoldOre, SlotGoldOre },          // GoldOre
        { SlotIronOre, SlotIronOre, SlotIronOre },          // IronOre
        { SlotWallTop, SlotWallSide, SlotWallSide },        // Wall
        { SlotMarker, SlotMarker, SlotMarker },             // Marker
        { SlotTeamOne, SlotTeamOne, SlotTeamOne },          // TeamOne
        { SlotTeamTwo, SlotTeamTwo, SlotTeamTwo },          // TeamTwo
        { SlotTeamOneDark, SlotTeamOneDark, SlotTeamOneDark },  // TeamOneDark
        { SlotTeamTwoDark, SlotTeamTwoDark, SlotTeamTwoDark },  // TeamTwoDark
        { SlotMetal, SlotMetal, SlotMetal },                // Metal
    };

    public static byte TeamColor(int team) => team == 0 ? TeamOne : TeamTwo;
    public static byte TeamColorDark(int team) => team == 0 ? TeamOneDark : TeamTwoDark;

    /// <summary>Вода и бедрок взрывом не выбиваются.</summary>
    public static bool IsIndestructible(byte block) => block == Bedrock || block == Water;

    /// <summary>Таблица block * 3 + face -> слот палитры для джоб.</summary>
    public static NativeArray<byte> CreateFaceTable(Allocator allocator)
    {
        var table = new NativeArray<byte>(Count * 3, allocator);
        for (int b = 0; b < Count; b++)
        {
            table[b * 3 + FaceTop] = faces[b, FaceTop];
            table[b * 3 + FaceSide] = faces[b, FaceSide];
            table[b * 3 + FaceBottom] = faces[b, FaceBottom];
        }
        return table;
    }

    public static Texture2D CreatePaletteTexture()
    {
        return new Texture2D(256, 1, TextureFormat.RGBA32, false)
        {
            name = "Voxel Palette",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
    }

    public static void WritePalette(Texture2D texture, Color32[] palette)
    {
        var pixels = new Color32[256];
        for (int i = 0; i < palette.Length; i++)
            pixels[i] = palette[i];

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
    }
}
