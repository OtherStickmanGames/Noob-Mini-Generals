using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Заполняет арену по столбцам. Карта симметрична относительно центра
/// (поворот на 180°): у обоих игроков одинаковые условия.
/// </summary>
[BurstCompile]
public struct ArenaGenerateJob : IJobParallelFor
{
    public int3 dims;
    public ArenaGenSettings settings;
    public float2 hillOffset;
    public float2 rampOffset;

    // Каждый столбец пишет только свои ячейки, пересечений между потоками нет
    [NativeDisableParallelForRestriction] public NativeArray<byte> voxels;

    public void Execute(int column)
    {
        int x = column % dims.x;
        int z = column / dims.x;

        int height = ColumnHeight(x, z);

        int layer = dims.x * dims.z;
        bool sandy = height - 1 <= settings.sandLevel;

        for (int y = 0; y < dims.y; y++)
        {
            byte block;
            if (y == 0)
                block = VoxelBlocks.Bedrock;
            else if (y >= height)
                block = VoxelBlocks.Air;
            else if (y == height - 1)
                block = sandy ? VoxelBlocks.Sand : VoxelBlocks.Grass;
            else if (y >= height - 4)
                block = sandy ? VoxelBlocks.Sand : VoxelBlocks.Dirt;
            else
                block = VoxelBlocks.Stone;

            voxels[y * layer + column] = block;
        }
    }

    int ColumnHeight(int x, int z)
    {
        var p = new float2(x, z);
        var q = new float2(dims.x - 1 - x, dims.z - 1 - z);

        // Среднее двух зеркальных точек даёт непрерывную симметричную функцию
        float hills = 0.5f * (Fbm(p, hillOffset) + Fbm(q, hillOffset)) * 1.4f;
        float h01 = math.saturate(hills * 0.5f + 0.5f);

        float smooth = settings.groundLevel + h01 * settings.hillHeight;
        float terraceStep = (float)settings.hillHeight / math.max(1, settings.terraceCount);
        float terraced = settings.groundLevel + math.floor(h01 * settings.terraceCount) * terraceStep;

        // Там, где шум рамп высокий, террасы заменяются пологим склоном
        float ramps = 0.5f * (noise.snoise(p * settings.rampScale + rampOffset)
                            + noise.snoise(q * settings.rampScale + rampOffset));
        float rampBlend = math.smoothstep(settings.rampThreshold, settings.rampThreshold + 0.15f, ramps);

        // Извилистая дорога от базы к базе, тоже пологая
        float cx = (dims.x - 1) * 0.5f;
        float cz = (dims.z - 1) * 0.5f;
        float wave = math.sin((z - cz) / dims.z * 2f * math.PI * settings.roadWaves);
        float roadX = cx + wave * settings.roadAmplitude;
        float roadBlend = 1f - math.smoothstep(settings.roadWidth, settings.roadWidth + 6f, math.abs(x - roadX));

        float h = math.lerp(terraced, smooth, math.max(rampBlend, roadBlend));

        // Ровные площадки под базы
        var baseA = new float2(cx, settings.baseMargin);
        var baseB = new float2(cx, dims.z - 1 - settings.baseMargin);
        float toBase = math.min(math.distance(p, baseA), math.distance(p, baseB));
        float flat = 1f - math.smoothstep(settings.baseRadius, settings.baseRadius + settings.baseFalloff, toBase);
        h = math.lerp(h, settings.baseHeight, flat);

        return math.clamp((int)math.round(h), 1, dims.y - 1);
    }

    float Fbm(float2 p, float2 offset)
    {
        float sum = 0f;
        float amplitude = 1f;
        float norm = 0f;
        float frequency = settings.noiseScale;

        for (int o = 0; o < settings.octaves; o++)
        {
            sum += noise.snoise(p * frequency + offset) * amplitude;
            norm += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }

        return sum / norm;
    }
}
