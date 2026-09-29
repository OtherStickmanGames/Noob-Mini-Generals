using Unity.Collections;
using UnityEngine;

/// <summary>
/// Типы блоков арены и их цвета. Цвет берётся из палитры по индексу:
/// у каждого блока свой индекс для верхней грани, боковых и нижней.
/// </summary>
public static class VoxelBlocks
{
    public const byte Air = 0;
    public const byte Bedrock = 1;
    public const byte Stone = 2;
    public const byte Dirt = 3;
    public const byte Grass = 4;
    public const byte Sand = 5;
    public const int Count = 6;

    public const int FaceTop = 0;
    public const int FaceSide = 1;
    public const int FaceBottom = 2;

    // Индексы палитры 16 и дальше оставлены под цвета команд
    public static readonly Color32[] Palette =
    {
        new(0, 0, 0, 255),        // 0  не используется
        new(52, 52, 58, 255),     // 1  коренная порода
        new(124, 126, 132, 255),  // 2  камень
        new(98, 100, 106, 255),   // 3  тёмный камень
        new(123, 88, 60, 255),    // 4  земля
        new(104, 168, 62, 255),   // 5  трава сверху
        new(88, 146, 52, 255),    // 6  трава тёмная
        new(218, 198, 140, 255),  // 7  песок
    };

    // top, side, bottom
    static readonly byte[,] faces =
    {
        { 0, 0, 0 },  // Air
        { 1, 1, 1 },  // Bedrock
        { 2, 3, 3 },  // Stone
        { 4, 4, 4 },  // Dirt
        { 5, 4, 4 },  // Grass
        { 7, 7, 7 },  // Sand
    };

    public static bool IsIndestructible(byte block) => block == Bedrock;

    /// <summary>Таблица block * 3 + face -> индекс палитры для джоб.</summary>
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
        var texture = new Texture2D(256, 1, TextureFormat.RGBA32, false)
        {
            name = "Voxel Palette",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        var pixels = new Color32[256];
        for (int i = 0; i < Palette.Length; i++)
            pixels[i] = Palette[i];

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
