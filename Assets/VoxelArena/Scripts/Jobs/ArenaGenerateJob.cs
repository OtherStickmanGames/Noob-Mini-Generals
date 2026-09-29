using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Заполняет столбцы вокселей по готовой раскладке (ArenaLayout).
/// </summary>
[BurstCompile]
public struct ArenaGenerateJob : IJobParallelFor
{
    public int3 dims;
    // С этой высоты верх гор покрыт снегом
    public int snowLine;

    [ReadOnly] public NativeArray<int> height;
    [ReadOnly] public NativeArray<int> waterTop;
    [ReadOnly] public NativeArray<byte> flags;

    // Каждый столбец пишет только свои ячейки, пересечений между потоками нет
    [NativeDisableParallelForRestriction] public NativeArray<byte> voxels;

    public void Execute(int column)
    {
        int h = math.clamp(height[column], 1, dims.y - 1);
        int water = math.min(waterTop[column], dims.y);
        bool sand = (flags[column] & ArenaLayout.FlagSand) != 0;
        bool cliff = (flags[column] & ArenaLayout.FlagCliff) != 0;
        bool rock = (flags[column] & ArenaLayout.FlagRock) != 0;
        bool softTop = (flags[column] & ArenaLayout.FlagSoftTop) != 0;
        int top = h - 1;
        int layer = dims.x * dims.z;

        for (int y = 0; y < dims.y; y++)
        {
            byte block;
            if (y == 0)
                block = VoxelBlocks.Bedrock;
            else if (y < h)
            {
                if (rock)
                {
                    if (y == top && top >= snowLine)
                        block = VoxelBlocks.Snow;
                    else if (y == top && softTop)
                        block = VoxelBlocks.Grass;
                    else
                        block = VoxelBlocks.Stone;
                }
                else if (y == top)
                    block = sand ? VoxelBlocks.Sand : VoxelBlocks.Grass;
                else if (cliff)
                    // На краю обрыва стенка каменная, под травой один слой земли
                    block = y == top - 1 && !sand ? VoxelBlocks.Dirt : VoxelBlocks.Stone;
                else if (y >= top - 3)
                    block = sand ? VoxelBlocks.Sand : VoxelBlocks.Dirt;
                else
                    block = VoxelBlocks.Stone;
            }
            else if (y < water)
                block = VoxelBlocks.Water;
            else
                block = VoxelBlocks.Air;

            voxels[y * layer + column] = block;
        }
    }
}
