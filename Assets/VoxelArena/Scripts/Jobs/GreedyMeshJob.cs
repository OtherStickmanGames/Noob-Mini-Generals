using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[StructLayout(LayoutKind.Sequential)]
public struct VoxelVertex
{
    public float3 position;
    public float3 normal;
    // r — индекс палитры, g — затенение угла (AO), 0..255
    public Color32 color;
}

/// <summary>
/// Строит меш одного чанка. Соседние грани склеиваются в один прямоугольник,
/// только если у них одинаковый цвет и одинаковое затенение во всех четырёх углах.
/// Координаты вершин — в вокселях относительно начала чанка.
/// </summary>
[BurstCompile]
public struct GreedyMeshJob : IJob
{
    [ReadOnly] public NativeArray<byte> voxels;
    [ReadOnly] public NativeArray<byte> faceColors;
    public int3 dims;
    public int3 chunkOrigin;
    public int chunkSize;

    public NativeList<VoxelVertex> vertices;
    public NativeList<uint> indices;
    // Только позиции — для меша коллайдера в обычном формате
    public NativeList<float3> positions;

    public void Execute()
    {
        var mask = new NativeArray<int>(chunkSize * chunkSize, Allocator.Temp);

        for (int d = 0; d < 3; d++)
        {
            int3 ed = Axis(d);
            int3 eu = Axis((d + 1) % 3);
            int3 ev = Axis((d + 2) % 3);

            for (int dir = -1; dir <= 1; dir += 2)
            {
                int face = d == 1
                    ? (dir > 0 ? VoxelBlocks.FaceTop : VoxelBlocks.FaceBottom)
                    : VoxelBlocks.FaceSide;

                for (int s = 0; s < chunkSize; s++)
                {
                    BuildMask(mask, ed, eu, ev, dir, face, s);
                    MergeMask(mask, ed, eu, ev, dir, s);
                }
            }
        }

        mask.Dispose();
    }

    // Ключ грани: бит 0 — грань есть, биты 1-8 — цвет, биты 9-16 — AO четырёх углов
    void BuildMask(NativeArray<int> mask, int3 ed, int3 eu, int3 ev, int dir, int face, int s)
    {
        for (int j = 0; j < chunkSize; j++)
        {
            for (int i = 0; i < chunkSize; i++)
            {
                int3 p = chunkOrigin + ed * s + eu * i + ev * j;
                int key = 0;

                byte block = GetBlock(p);
                if (block != VoxelBlocks.Air)
                {
                    int3 q = p + ed * dir;
                    if (GetBlock(q) == VoxelBlocks.Air)
                    {
                        int color = faceColors[block * 3 + face];
                        int ao0 = Ao(q, eu, ev, -1, -1);
                        int ao1 = Ao(q, eu, ev, 1, -1);
                        int ao2 = Ao(q, eu, ev, 1, 1);
                        int ao3 = Ao(q, eu, ev, -1, 1);
                        key = 1 | (color << 1) | (ao0 << 9) | (ao1 << 11) | (ao2 << 13) | (ao3 << 15);
                    }
                }

                mask[i + j * chunkSize] = key;
            }
        }
    }

    void MergeMask(NativeArray<int> mask, int3 ed, int3 eu, int3 ev, int dir, int s)
    {
        for (int j = 0; j < chunkSize; j++)
        {
            int i = 0;
            while (i < chunkSize)
            {
                int key = mask[i + j * chunkSize];
                if (key == 0)
                {
                    i++;
                    continue;
                }

                int w = 1;
                while (i + w < chunkSize && mask[i + w + j * chunkSize] == key)
                    w++;

                int h = 1;
                while (j + h < chunkSize && RowMatches(mask, key, i, j + h, w))
                    h++;

                EmitQuad(key, ed, eu, ev, dir, s, i, j, w, h);

                for (int l = 0; l < h; l++)
                    for (int k = 0; k < w; k++)
                        mask[i + k + (j + l) * chunkSize] = 0;

                i += w;
            }
        }
    }

    bool RowMatches(NativeArray<int> mask, int key, int i, int row, int w)
    {
        for (int k = 0; k < w; k++)
        {
            if (mask[i + k + row * chunkSize] != key)
                return false;
        }
        return true;
    }

    void EmitQuad(int key, int3 ed, int3 eu, int3 ev, int dir, int s, int i, int j, int w, int h)
    {
        var origin = (float3)(ed * (s + (dir > 0 ? 1 : 0)) + eu * i + ev * j);
        var du = (float3)(eu * w);
        var dv = (float3)(ev * h);
        var normal = (float3)(ed * dir);

        byte color = (byte)((key >> 1) & 0xFF);
        int ao0 = (key >> 9) & 3;
        int ao1 = (key >> 11) & 3;
        int ao2 = (key >> 13) & 3;
        int ao3 = (key >> 15) & 3;

        uint start = (uint)vertices.Length;
        vertices.Add(Vertex(origin, normal, color, ao0));
        vertices.Add(Vertex(origin + du, normal, color, ao1));
        vertices.Add(Vertex(origin + du + dv, normal, color, ao2));
        vertices.Add(Vertex(origin + dv, normal, color, ao3));

        positions.Add(origin);
        positions.Add(origin + du);
        positions.Add(origin + du + dv);
        positions.Add(origin + dv);

        // cross(eu, ev) == ed, поэтому порядок 0-1-2 смотрит в +ed.
        // Диагональ выбираем так, чтобы затенение не растекалось по всему квадрату.
        bool flip = ao0 + ao2 < ao1 + ao3;
        if (dir > 0)
        {
            if (!flip) { Tri(start, 0, 1, 2); Tri(start, 0, 2, 3); }
            else       { Tri(start, 1, 2, 3); Tri(start, 1, 3, 0); }
        }
        else
        {
            if (!flip) { Tri(start, 0, 2, 1); Tri(start, 0, 3, 2); }
            else       { Tri(start, 1, 3, 2); Tri(start, 1, 0, 3); }
        }
    }

    void Tri(uint start, uint a, uint b, uint c)
    {
        indices.Add(start + a);
        indices.Add(start + b);
        indices.Add(start + c);
    }

    static VoxelVertex Vertex(float3 position, float3 normal, byte color, int ao)
    {
        return new VoxelVertex
        {
            position = position,
            normal = normal,
            color = new Color32(color, (byte)(ao * 85), 0, 255),
        };
    }

    // Затенение угла: 3 — открыт, 0 — зажат с двух сторон
    int Ao(int3 q, int3 eu, int3 ev, int su, int sv)
    {
        int side1 = GetBlock(q + eu * su) != VoxelBlocks.Air ? 1 : 0;
        int side2 = GetBlock(q + ev * sv) != VoxelBlocks.Air ? 1 : 0;
        int corner = GetBlock(q + eu * su + ev * sv) != VoxelBlocks.Air ? 1 : 0;

        if (side1 == 1 && side2 == 1)
            return 0;

        return 3 - (side1 + side2 + corner);
    }

    // Ниже арены — сплошная порода (нижние грани не строятся),
    // за краями и выше — воздух (края карты видны как срез)
    byte GetBlock(int3 p)
    {
        if (p.y < 0)
            return VoxelBlocks.Bedrock;

        if (p.x < 0 || p.z < 0 || p.x >= dims.x || p.y >= dims.y || p.z >= dims.z)
            return VoxelBlocks.Air;

        return voxels[(p.y * dims.z + p.z) * dims.x + p.x];
    }

    static int3 Axis(int d)
    {
        if (d == 0) return new int3(1, 0, 0);
        if (d == 1) return new int3(0, 1, 0);
        return new int3(0, 0, 1);
    }
}
