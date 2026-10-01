using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public enum ResourceKind
{
    /// <summary>Точка захвата с ценным ресурсом в поле (внутри стен источников нет)</summary>
    CapturePoint,
}

public struct ResourcePoint
{
    public int2 cell;
    public ResourceKind kind;
    /// <summary>0 — база один, 1 — база два, -1 — ничья (точка захвата)</summary>
    public int team;
}

public struct WallCell
{
    public int2 cell;
    public bool outer;  // внешний ряд — на нём зубцы
}

/// <summary>
/// Раскладка карты по столбцам (x, z): высоты, вода, рампы, базы, ресурсы, деревья, камни.
/// Индекс столбца: z * sizeX + x.
/// </summary>
public class ArenaLayout
{
    public const byte FlagSand = 1;   // сверху песок (берег, дно озера)
    public const byte FlagCliff = 2;  // край обрыва: под верхним блоком сразу камень
    public const byte FlagRamp = 4;
    public const byte FlagRock = 8;       // горы по краю: камень до самого верха
    public const byte FlagSoftTop = 16;   // пологий склон горы: сверху трава

    public int sizeX;
    public int sizeZ;
    public int[] height;    // число сплошных блоков в столбце
    public int[] waterTop;  // 0 — нет воды, иначе вода стоит до этого y (не включая)
    public byte[] flags;

    public int2 baseOne;
    public int2 baseTwo;
    /// <summary>Сторона ворот базы один: 0 +z, 1 +x, 2 -z, 3 -x (у базы два — противоположная)</summary>
    public int gateSide;
    /// <summary>
    /// Внутри стен какого уровня лежит клетка: 0 — ни в каких, 1..3 — база один, 5..7 — база два (4 + уровень).
    /// Хранится наименьший уровень, в который клетка попадает.
    /// </summary>
    public byte[] baseZone;
    /// <summary>Ровная площадка базы (под стены 3-го уровня и отступ): 0 — нет, 1 — база один, 2 — база два</summary>
    public byte[] baseArea;
    /// <summary>Направление от базы один к базе два (единичный вектор в плоскости x, z)</summary>
    public float2 baseAxis;
    /// <summary>Стены 1-го уровня обеих баз</summary>
    public readonly List<WallCell> walls = new();
    /// <summary>Клетка сразу снаружи ворот: [0] — база один, [1] — база два</summary>
    public readonly List<int2> gates = new();
    public readonly List<ResourcePoint> resources = new();
    public readonly List<int2> trees = new();
    public readonly List<int2> rocks = new();

    // Для лога
    public int regionCount;
    public int rampCount;
    public int flattenedRegions;
    public bool basesConnected;
    public bool capturePointsReachable;
}

/// <summary>
/// Генератор раскладки: сначала структура (горы по краю, уровни, базы, вода, рампы по графу
/// связности), потом коридоры и детали. Карта симметрична поворотом на 180° вокруг центра.
/// Шаги 1:1 повторяют прототип на Python, на котором проверялась проходимость.
/// </summary>
public static class ArenaLayoutGenerator
{
    public static ArenaLayout Generate(ArenaGenSettings settings, ArenaBiome biome, int sizeX, int sizeY, int sizeZ)
    {
        return new Builder(settings, biome, sizeX, sizeY, sizeZ).Run();
    }

    struct RampSite
    {
        public int x, z, dx, dz;  // (x, z) — нижняя клетка у обрыва, d — шаг к верхней
    }

    class Builder
    {
        static readonly int2[] Dirs = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

        readonly ArenaGenSettings s;
        readonly ArenaBiome biome;
        readonly int sx, sy, sz, n;
        readonly int[] levelHeights;
        readonly System.Random rnd;
        readonly float2 levelOffset, waterOffset, forestOffset, mountainOffset, borderOffset;

        int[] level;
        int[] edge;          // расстояние до края карты
        int[] mountainDepth; // расстояние вглубь гор от подножия
        bool[] border, baseMask, reserved, water, shore, rampMask, wallMask, corridor;
        int[] height;

        int2 baseA, baseB;
        float2 axis;       // от A к B
        int gateSide;      // 0: +z, 1: +x, 2: -z, 3: -x
        int baseHalf;      // полуразмер ровной площадки базы
        float baseDistance;
        int2 gateOutA, gateOutB;

        // Участки
        int[] comp;
        readonly List<int> compSize = new();
        readonly List<int> compCell = new();
        int[] parent;

        readonly List<ResourcePoint> resources = new();
        readonly List<WallCell> walls = new();
        readonly List<int2> captures = new();
        int rampCount;

        public Builder(ArenaGenSettings settings, ArenaBiome biome, int sizeX, int sizeY, int sizeZ)
        {
            s = settings;
            this.biome = biome;
            sx = sizeX;
            sy = sizeY;
            sz = sizeZ;
            n = sx * sz;
            levelHeights = new[] { s.lowHeight, s.midHeight, s.highHeight };
            rnd = new System.Random(s.seed);

            var random = new Unity.Mathematics.Random(math.hash(new int2(s.seed, 7)) | 1u);
            levelOffset = random.NextFloat2(new float2(-5000f), new float2(5000f));
            waterOffset = random.NextFloat2(new float2(-5000f), new float2(5000f));
            forestOffset = random.NextFloat2(new float2(-5000f), new float2(5000f));
            mountainOffset = random.NextFloat2(new float2(-5000f), new float2(5000f));
            borderOffset = random.NextFloat2(new float2(-5000f), new float2(5000f));
        }

        int Idx(int x, int z) => z * sx + x;
        int Idx(int2 c) => c.y * sx + c.x;
        bool Inside(int x, int z) => x >= 0 && z >= 0 && x < sx && z < sz;
        bool Inside(int2 c) => Inside(c.x, c.y);
        int Mirror(int i) => n - 1 - i;
        int2 Mirror(int2 c) => new(sx - 1 - c.x, sz - 1 - c.y);

        public ArenaLayout Run()
        {
            BuildBorder();
            PlaceBases();
            ClearBorderAroundBases();
            BuildLevels();
            SmoothLevels();
            RemoveSmallRegions();
            PlaceBaseContent();
            PlaceWater();

            int flattened = 0;
            bool connected = false;
            for (int attempt = 0; attempt < 6 && !connected; attempt++)
                connected = PlaceRamps(ref flattened);

            // Точки захвата — после рамп: выбираются только там, куда от ворот можно дойти
            PlaceCapturePoints();
            BuildMountains();
            BuildCorridors();

            var layout = new ArenaLayout
            {
                sizeX = sx,
                sizeZ = sz,
                baseOne = baseA,
                baseTwo = baseB,
                baseAxis = axis,
                gateSide = gateSide,
                regionCount = compSize.Count,
                rampCount = rampCount,
                flattenedRegions = flattened,
            };
            layout.resources.AddRange(resources);
            layout.walls.AddRange(walls);
            layout.gates.Add(gateOutA);
            layout.gates.Add(gateOutB);

            PlaceTreesAndRocks(layout);
            FillLayout(layout);
            FillBaseZones(layout);
            CheckReachability(layout);
            return layout;
        }

        // ---------- Шум ----------

        float Fbm(float2 p, float scale, int octaves, float2 offset)
        {
            float sum = 0f, amplitude = 1f, norm = 0f, frequency = scale;
            for (int o = 0; o < octaves; o++)
            {
                sum += noise.snoise(p * frequency + offset) * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }
            return sum / norm;
        }

        // Среднее двух зеркальных точек — симметричная непрерывная функция
        float[] SymmetricNoise(float scale, int octaves, float2 offset)
        {
            var values = new float[n];
            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    var p = new float2(x, z);
                    var q = new float2(sx - 1 - x, sz - 1 - z);
                    values[Idx(x, z)] = 0.5f * (Fbm(p, scale, octaves, offset) + Fbm(q, scale, octaves, offset));
                }
            }
            return values;
        }

        // ---------- Горы по краю ----------

        int EdgeDistance(int x, int z) => math.min(math.min(x, z), math.min(sx - 1 - x, sz - 1 - z));

        void BuildBorder()
        {
            // Ширина гор меняется по симметричному шуму — край карты неровный, с заливами
            var widthNoise = SymmetricNoise(s.borderNoiseScale, 3, borderOffset);
            edge = new int[n];
            border = new bool[n];
            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    int i = Idx(x, z);
                    edge[i] = EdgeDistance(x, z);
                    float t = math.saturate(widthNoise[i] * 1.6f + 0.45f);
                    border[i] = edge[i] < math.lerp(s.borderMinWidth, s.borderMaxWidth, t);
                }
            }
        }

        // Горы не заходят на площадку базы и отступ вокруг неё
        void ClearBorderAroundBases()
        {
            foreach (var b in new[] { baseA, baseB })
            {
                int r = baseHalf + 6;
                for (int z = b.y - r; z <= b.y + r; z++)
                    for (int x = b.x - r; x <= b.x + r; x++)
                        if (Inside(x, z) && edge[Idx(x, z)] >= s.borderMinWidth)
                            border[Idx(x, z)] = false;
            }
        }

        // ---------- Базы на случайной оси через центр ----------

        void PlaceBases()
        {
            baseHalf = s.wallLevel3Size / 2 + s.wallThickness + s.basePadding;

            float angle = (float)(rnd.NextDouble() * math.PI);
            axis = new float2(math.cos(angle), math.sin(angle));

            // Как можно дальше друг от друга, но площадка базы целиком внутри гор
            var center = new float2((sx - 1) * 0.5f, (sz - 1) * 0.5f);
            // За базой остаётся поле шириной baseBackSpace до самых широких гор
            float limitX = sx * 0.5f - s.borderMaxWidth - baseHalf - s.baseBackSpace;
            float limitZ = sz * 0.5f - s.borderMaxWidth - baseHalf - s.baseBackSpace;
            float dx = math.abs(axis.x) > 1e-3f ? limitX / math.abs(axis.x) : float.MaxValue;
            float dz = math.abs(axis.y) > 1e-3f ? limitZ / math.abs(axis.y) : float.MaxValue;
            baseDistance = math.min(dx, dz);

            baseA = (int2)math.round(center - axis * baseDistance);
            baseB = Mirror(baseA);

            // Ворота на стороне, которая больше всего смотрит на противника
            if (math.abs(axis.x) > math.abs(axis.y))
                gateSide = axis.x > 0 ? 1 : 3;
            else
                gateSide = axis.y > 0 ? 0 : 2;
        }

        // Поворот смещения: канонический +z (сторона ворот) в сторону gateSide
        int2 Rotate(int2 d)
        {
            return gateSide switch
            {
                0 => d,
                1 => new int2(d.y, -d.x),
                2 => new int2(-d.x, -d.y),
                _ => new int2(-d.y, d.x),
            };
        }

        // ---------- 1. Уровни ----------

        void BuildLevels()
        {
            var values = SymmetricNoise(s.levelNoiseScale, 3, levelOffset);

            // Пороги по долям площади внутри гор, чтобы соотношение уровней не зависело от сида
            var inner = new List<float>();
            for (int i = 0; i < n; i++)
                if (!border[i])
                    inner.Add(values[i]);
            inner.Sort();
            float t1 = inner[Mathf.Clamp((int)(inner.Count * s.lowShare), 0, inner.Count - 1)];
            float t2 = inner[Mathf.Clamp((int)(inner.Count * (1f - s.highShare)), 0, inner.Count - 1)];

            level = new int[n];
            for (int i = 0; i < n; i++)
                level[i] = values[i] < t1 ? 0 : values[i] < t2 ? 1 : 2;

            baseMask = new bool[n];
            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    bool inA = math.abs(x - baseA.x) <= baseHalf && math.abs(z - baseA.y) <= baseHalf;
                    bool inB = math.abs(x - baseB.x) <= baseHalf && math.abs(z - baseB.y) <= baseHalf;
                    baseMask[Idx(x, z)] = inA || inB;
                }
            }

            ForceLevels();
        }

        void ForceLevels()
        {
            for (int i = 0; i < n; i++)
            {
                if (baseMask[i])
                    level[i] = 1;
                if (border[i])
                    level[i] = 2;
            }
        }

        // ---------- 2. Сглаживание: самый частый уровень в окне 5x5 ----------

        void SmoothLevels()
        {
            var counts = new int[3];
            for (int iteration = 0; iteration < 3; iteration++)
            {
                var next = (int[])level.Clone();
                for (int z = 0; z < sz; z++)
                {
                    for (int x = 0; x < sx; x++)
                    {
                        counts[0] = counts[1] = counts[2] = 0;
                        for (int dz = -2; dz <= 2; dz++)
                            for (int dx = -2; dx <= 2; dx++)
                                if (Inside(x + dx, z + dz))
                                    counts[level[Idx(x + dx, z + dz)]]++;

                        int current = level[Idx(x, z)];
                        int best = math.max(counts[0], math.max(counts[1], counts[2]));
                        if (counts[current] == best)
                            continue;

                        // При равенстве — меньший уровень (правило одно для обеих половин карты)
                        next[Idx(x, z)] = counts[0] == best ? 0 : counts[1] == best ? 1 : 2;
                    }
                }
                level = next;
                ForceLevels();
            }
        }

        // ---------- Участки ----------

        void BuildComponents(bool[] valid)
        {
            comp = new int[n];
            for (int i = 0; i < n; i++)
                comp[i] = -1;
            compSize.Clear();
            compCell.Clear();

            var queue = new int[n];
            for (int start = 0; start < n; start++)
            {
                if (comp[start] >= 0 || (valid != null && !valid[start]))
                    continue;

                int id = compSize.Count;
                int key = level[start];
                int head = 0, tail = 0;
                queue[tail++] = start;
                comp[start] = id;

                while (head < tail)
                {
                    int i = queue[head++];
                    int x = i % sx, z = i / sx;
                    foreach (var d in Dirs)
                    {
                        int xx = x + d.x, zz = z + d.y;
                        if (!Inside(xx, zz))
                            continue;
                        int j = Idx(xx, zz);
                        if (comp[j] >= 0 || level[j] != key || (valid != null && !valid[j]))
                            continue;
                        comp[j] = id;
                        queue[tail++] = j;
                    }
                }

                compSize.Add(tail);
                compCell.Add(start);
            }
        }

        // ---------- 3. Мелкие участки вливаются в соседей ----------

        void RemoveSmallRegions()
        {
            for (int pass = 0; pass < 6; pass++)
            {
                BuildComponents(null);

                var target = new Dictionary<int, int[]>();
                for (int c = 0; c < compSize.Count; c++)
                    if (compSize[c] < s.minRegionCells)
                        target[c] = new int[3];

                if (target.Count == 0)
                    return;

                for (int i = 0; i < n; i++)
                {
                    if (!target.TryGetValue(comp[i], out var counts))
                        continue;
                    int x = i % sx, z = i / sx;
                    foreach (var d in Dirs)
                    {
                        int xx = x + d.x, zz = z + d.y;
                        if (Inside(xx, zz) && comp[Idx(xx, zz)] != comp[i])
                            counts[level[Idx(xx, zz)]]++;
                    }
                }

                // Все мелкие участки меняются одновременно — симметрия сохраняется
                var newLevel = new Dictionary<int, int>();
                foreach (var pair in target)
                {
                    var counts = pair.Value;
                    int best = math.max(counts[0], math.max(counts[1], counts[2]));
                    if (best == 0)
                        continue;
                    newLevel[pair.Key] = counts[0] == best ? 0 : counts[1] == best ? 1 : 2;
                }

                for (int i = 0; i < n; i++)
                    if (newLevel.TryGetValue(comp[i], out int l))
                        level[i] = l;

                ForceLevels();
            }
        }

        // ---------- 4. База: стены и ворота ----------

        void PlaceBaseContent()
        {
            reserved = new bool[n];
            for (int i = 0; i < n; i++)
                reserved[i] = baseMask[i] || border[i];
            wallMask = new bool[n];

            // Стены 1-го уровня базы A в каноническом виде (ворота на +z), затем поворот к противнику.
            // База B — зеркальная копия.
            int h = s.wallLevel1Size / 2;
            int t = s.wallThickness;
            int g = s.gateWidth / 2;
            for (int dz = -h - t; dz < h + t; dz++)
            {
                for (int dx = -h - t; dx < h + t; dx++)
                {
                    bool inner = dx >= -h && dx < h && dz >= -h && dz < h;
                    bool gate = dz >= h && dx >= -g && dx < g;
                    if (inner || gate)
                        continue;

                    bool outer = dx == -h - t || dx == h + t - 1 || dz == -h - t || dz == h + t - 1;
                    var cell = baseA + Rotate(new int2(dx, dz));
                    AddWall(cell, outer);
                    AddWall(Mirror(cell), outer);
                }
            }

            gateOutA = baseA + Rotate(new int2(0, h + t));
            gateOutB = Mirror(gateOutA);
        }

        void AddWall(int2 cell, bool outer)
        {
            walls.Add(new WallCell { cell = cell, outer = outer });
            wallMask[Idx(cell)] = true;
        }

        // ---------- Точки захвата ----------

        bool[] captureReach;

        void PlaceCapturePoints()
        {
            captureReach = Walk(gateOutA, wallMask, null);

            var center = new float2((sx - 1) * 0.5f, (sz - 1) * 0.5f);
            var perp = new float2(-axis.y, axis.x);
            float size = math.min(sx, sz);

            var targets = new[]
            {
                (float2)baseA + axis * (baseHalf + 14),                                       // ближняя
                center - axis * baseDistance * 0.35f + perp * size * 0.30f,                   // фланги
                center - axis * baseDistance * 0.35f - perp * size * 0.30f,
                center + perp * size * 0.12f,                                                 // спорная у центра
            };

            foreach (var target in targets)
            {
                if (!SnapCapturePoint((int2)math.round(target), out var cell))
                    continue;

                foreach (var c in new[] { cell, Mirror(cell) })
                {
                    captures.Add(c);
                    resources.Add(new ResourcePoint { cell = c, kind = ResourceKind.CapturePoint, team = -1 });
                    for (int dz = -4; dz <= 4; dz++)
                        for (int dx = -4; dx <= 4; dx++)
                            reserved[Idx(c.x + dx, c.y + dz)] = true;
                }
            }
        }

        // Ближайшая к цели клетка, вокруг которой 9x9 одного уровня и ничего не занято
        bool SnapCapturePoint(int2 target, out int2 cell)
        {
            for (int r = 0; r < 40; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (math.max(math.abs(dx), math.abs(dz)) != r)
                            continue;
                        var c = target + new int2(dx, dz);
                        if (CaptureSpot(c))
                        {
                            cell = c;
                            return true;
                        }
                    }
                }
            }

            cell = default;
            return false;
        }

        // Ровная площадка 9x9 на суше, без рамп, берега и резерва, и до неё (и до зеркальной) можно дойти
        bool CaptureSpot(int2 c)
        {
            if (!Inside(c) || !captureReach[Idx(c)] || !captureReach[Idx(Mirror(c))])
                return false;
            int h = height[Idx(c)];
            for (int dz = -4; dz <= 4; dz++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = c.x + dx, z = c.y + dz;
                    if (!Inside(x, z))
                        return false;
                    int i = Idx(x, z);
                    if (height[i] != h || water[i] || border[i] || reserved[i] || rampMask[i] || shore[i])
                        return false;
                }
            }
            return true;
        }

        // ---------- 5. Вода в низинах ----------

        void PlaceWater()
        {
            var wet = SymmetricNoise(s.waterNoiseScale, 2, waterOffset);
            float threshold = s.waterThreshold + ArenaBiomes.WaterThresholdBonus(biome);

            water = new bool[n];
            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    int i = Idx(x, z);
                    if (level[i] != 0 || wet[i] < threshold)
                        continue;

                    // Вокруг 7x7 — только низина без резерва
                    bool ok = true;
                    for (int dz = -3; dz <= 3 && ok; dz++)
                    {
                        for (int dx = -3; dx <= 3 && ok; dx++)
                        {
                            int xx = x + dx, zz = z + dz;
                            ok = Inside(xx, zz) && level[Idx(xx, zz)] == 0 && !reserved[Idx(xx, zz)];
                        }
                    }
                    water[i] = ok;
                }
            }

            // Мелкие лужи убираем
            BuildComponents(water);
            for (int i = 0; i < n; i++)
                if (water[i] && compSize[comp[i]] < s.minLakeCells)
                    water[i] = false;

            shore = new bool[n];
            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    if (water[Idx(x, z)])
                        continue;
                    for (int dz = -2; dz <= 2; dz++)
                        for (int dx = -2; dx <= 2; dx++)
                            if (Inside(x + dx, z + dz) && water[Idx(x + dx, z + dz)])
                                shore[Idx(x, z)] = true;
                }
            }
        }

        // ---------- 6. Рампы по графу связности ----------

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        void Union(int a, int b) => parent[Find(a)] = Find(b);

        int MirrorComp(int c) => comp[Mirror(compCell[c])];

        bool IsLand(int i) => !water[i] && !border[i];

        // Клетки рампы: половина на нижнем уровне, половина на верхнем, подъём 1 блок на 2 клетки
        void RampCells(RampSite site, int width, List<(int index, bool lowSide, int height)> cells, out bool inside)
        {
            cells.Clear();
            inside = true;

            int lo = level[Idx(site.x, site.z)];
            int hi = level[Idx(site.x + site.dx, site.z + site.dz)];
            int loH = levelHeights[lo];
            int step = levelHeights[hi] - loH;
            int half = step;
            int px = -site.dz, pz = site.dx;

            for (int t = -half; t < half; t++)
            {
                for (int w = -(width / 2); w <= width / 2; w++)
                {
                    int x = site.x + site.dx * (t + 1) + px * w;
                    int z = site.z + site.dz * (t + 1) + pz * w;
                    if (!Inside(x, z))
                    {
                        inside = false;
                        return;
                    }

                    // (t + half + 0.5) * step / (2 * half) никогда не попадает ровно на .5
                    float h = loH + step * (t + half + 0.5f) / (2f * half);
                    cells.Add((Idx(x, z), t < 0, (int)math.round(h)));
                }
            }
        }

        readonly List<(int index, bool lowSide, int height)> cellsA = new();
        readonly List<(int index, bool lowSide, int height)> cellsB = new();

        bool RampValid(RampSite site, int width, List<(int index, bool lowSide, int height)> cells)
        {
            RampCells(site, width, cells, out bool inside);
            if (!inside)
                return false;

            int lo = level[Idx(site.x, site.z)];
            int hi = level[Idx(site.x + site.dx, site.z + site.dz)];

            foreach (var (index, lowSide, _) in cells)
            {
                if (water[index] || reserved[index] || rampMask[index])
                    return false;
                if (level[index] != (lowSide ? lo : hi))
                    return false;
            }
            return true;
        }

        void StampRamp(List<(int index, bool lowSide, int height)> cells)
        {
            foreach (var (index, _, h) in cells)
            {
                height[index] = h;
                rampMask[index] = true;
                shore[index] = false;
            }
            rampCount++;
        }

        bool TryAddRamp(List<RampSite> sites, int width)
        {
            var order = new List<RampSite>(sites);
            Shuffle(order);

            foreach (var site in order)
            {
                if (!RampValid(site, width, cellsA))
                    continue;

                int i = Idx(site.x, site.z);
                int mi = Mirror(i);
                if (mi == i)
                {
                    StampRamp(cellsA);
                    return true;
                }

                var mirror = new RampSite { x = mi % sx, z = mi / sx, dx = -site.dx, dz = -site.dz };
                if (!RampValid(mirror, width, cellsB))
                    continue;

                // Рампа и её зеркальная копия не должны налезать друг на друга
                var used = new HashSet<int>();
                foreach (var c in cellsA)
                    used.Add(c.index);
                bool overlap = false;
                foreach (var c in cellsB)
                    overlap |= used.Contains(c.index);
                if (overlap)
                    continue;

                StampRamp(cellsA);
                StampRamp(cellsB);
                return true;
            }
            return false;
        }

        /// <summary>Возвращает true, если вся суша связана с базой.</summary>
        bool PlaceRamps(ref int flattened)
        {
            var land = new bool[n];
            for (int i = 0; i < n; i++)
                land[i] = IsLand(i);

            BuildComponents(land);

            height = new int[n];
            for (int i = 0; i < n; i++)
                height[i] = levelHeights[level[i]];
            rampMask = new bool[n];
            rampCount = 0;

            // Кандидаты: пары (нижний участок, верхний участок) через уступ в один уровень
            var pairSites = new Dictionary<long, List<RampSite>>();
            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    int i = Idx(x, z);
                    if (!land[i])
                        continue;
                    foreach (var d in Dirs)
                    {
                        int xx = x + d.x, zz = z + d.y;
                        if (!Inside(xx, zz))
                            continue;
                        int j = Idx(xx, zz);
                        if (!land[j] || level[j] - level[i] != 1)
                            continue;

                        long key = (long)comp[i] * 1_000_000 + comp[j];
                        if (!pairSites.TryGetValue(key, out var list))
                            pairSites[key] = list = new List<RampSite>();
                        list.Add(new RampSite { x = x, z = z, dx = d.x, dz = d.y });
                    }
                }
            }

            parent = new int[compSize.Count];
            for (int c = 0; c < parent.Length; c++)
                parent[c] = c;

            var pairs = new List<long>(pairSites.Keys);
            pairs.Sort();
            Shuffle(pairs);

            // Остов: рампа там, где участки ещё не связаны
            var withRamp = new HashSet<long>();
            foreach (long key in pairs)
            {
                int a = (int)(key / 1_000_000), b = (int)(key % 1_000_000);
                if (Find(a) == Find(b))
                    continue;
                if (!TryAddRamp(pairSites[key], s.rampWidth))
                    continue;

                Union(a, b);
                Union(MirrorComp(a), MirrorComp(b));
                withRamp.Add(key);
            }

            // Лишние рампы — чтобы к каждому месту было больше одного пути
            foreach (long key in pairs)
            {
                if (withRamp.Contains(key) || rnd.NextDouble() >= s.extraRampChance)
                    continue;
                if (TryAddRamp(pairSites[key], s.rampWidth))
                    withRamp.Add(key);
            }

            int baseComp = comp[Idx(baseA)];
            if (AllConnected(baseComp))
                return true;

            // Запасной путь: оторванный участок выравнивается под соседний связанный уровень
            var counts = new Dictionary<int, int[]>();
            for (int c = 0; c < compSize.Count; c++)
                if (Find(c) != Find(baseComp))
                    counts[c] = new int[3];

            for (int i = 0; i < n; i++)
            {
                if (!land[i] || !counts.TryGetValue(comp[i], out var cnt))
                    continue;
                int x = i % sx, z = i / sx;
                foreach (var d in Dirs)
                {
                    int xx = x + d.x, zz = z + d.y;
                    if (!Inside(xx, zz))
                        continue;
                    int j = Idx(xx, zz);
                    if (land[j] && comp[j] != comp[i] && Find(comp[j]) == Find(baseComp))
                        cnt[level[j]]++;
                }
            }

            var newLevel = new Dictionary<int, int>();
            foreach (var pair in counts)
            {
                var cnt = pair.Value;
                int best = math.max(cnt[0], math.max(cnt[1], cnt[2]));
                if (best > 0)
                    newLevel[pair.Key] = cnt[0] == best ? 0 : cnt[1] == best ? 1 : 2;
            }

            for (int i = 0; i < n; i++)
                if (land[i] && newLevel.TryGetValue(comp[i], out int l))
                    level[i] = l;

            flattened += counts.Count;
            return false;
        }

        bool AllConnected(int baseComp)
        {
            int root = Find(baseComp);
            for (int c = 0; c < compSize.Count; c++)
                if (Find(c) != root)
                    return false;
            return true;
        }

        // ---------- Проходимость по сетке ----------

        /// <summary>
        /// Полуширина прохода в клетках: проход должен быть шириной минимум 2·r+1 клеток. Считается
        /// по самому широкому агенту — технике (танк ~2 м): навмеш сужает проходы на радиус агента
        /// с каждой стороны, и более узкие места для неё закрываются.
        /// </summary>
        const int ClearanceRadius = 2;
        // Коридор без деревьев: проход техники плюс полуразмер дерева (дерево — препятствие 5×5)
        const int CorridorRadius = ClearanceRadius + 2;
        // Центр точки захвата занят рудным бугром: обход доходит до клетки не дальше этого
        const int CaptureReachRadius = 6;

        /// <summary>
        /// Обход от старта: шаг по высоте не больше блока. Клетка проходима, если в квадрате
        /// ClearanceRadius вокруг неё нет препятствий и обрывов (перепад больше блока между соседями).
        /// </summary>
        bool[] Walk(int2 start, bool[] obstacles, int[] previous)
        {
            var seen = new bool[n];
            if (previous != null)
                for (int i = 0; i < n; i++)
                    previous[i] = -1;
            MarkCliffs();

            var queue = new int[n];
            int head = 0, tail = 0;
            int s0 = Idx(start);
            queue[tail++] = s0;
            seen[s0] = true;

            while (head < tail)
            {
                int i = queue[head++];
                int x = i % sx, z = i / sx;
                foreach (var d in Dirs)
                {
                    int xx = x + d.x, zz = z + d.y;
                    if (!Inside(xx, zz))
                        continue;
                    int j = Idx(xx, zz);
                    if (seen[j] || math.abs(height[j] - height[i]) > 1 || !Walkable(xx, zz, obstacles))
                        continue;
                    seen[j] = true;
                    if (previous != null)
                        previous[j] = i;
                    queue[tail++] = j;
                }
            }
            return seen;
        }

        bool[] cliff;

        // Обрыв: у клетки есть сосед выше или ниже больше чем на блок (рампа — ступени по блоку)
        void MarkCliffs()
        {
            cliff ??= new bool[n];
            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    int i = Idx(x, z);
                    bool c = false;
                    if (x > 0) c |= math.abs(height[i] - height[i - 1]) > 1;
                    if (x < sx - 1) c |= math.abs(height[i] - height[i + 1]) > 1;
                    if (z > 0) c |= math.abs(height[i] - height[i - sx]) > 1;
                    if (z < sz - 1) c |= math.abs(height[i] - height[i + sx]) > 1;
                    cliff[i] = c;
                }
            }
        }

        bool Blocked(int i, bool[] obstacles) => water[i] || border[i] || obstacles[i];

        bool Walkable(int x, int z, bool[] obstacles)
        {
            for (int dz = -ClearanceRadius; dz <= ClearanceRadius; dz++)
            {
                for (int dx = -ClearanceRadius; dx <= ClearanceRadius; dx++)
                {
                    int xx = x + dx, zz = z + dz;
                    if (!Inside(xx, zz))
                        return false;
                    int j = Idx(xx, zz);
                    if (Blocked(j, obstacles) || cliff[j])
                        return false;
                }
            }
            return true;
        }

        // Ближайшая к цели клетка, до которой дошёл обход (центр точки захвата занят бугром)
        int NearestSeen(int2 goal, bool[] seen, int radius)
        {
            for (int r = 0; r <= radius; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        var c = goal + new int2(dx, dz);
                        if (Inside(c) && seen[Idx(c)])
                            return Idx(c);
                    }
                }
            }
            return -1;
        }

        // ---------- Горы ----------

        void BuildMountains()
        {
            // Расстояние вглубь гор от подножия
            mountainDepth = new int[n];
            var queue = new int[n];
            int head = 0, tail = 0;
            for (int i = 0; i < n; i++)
            {
                if (!border[i])
                    continue;
                int x = i % sx, z = i / sx;
                foreach (var d in Dirs)
                {
                    int xx = x + d.x, zz = z + d.y;
                    if (Inside(xx, zz) && !border[Idx(xx, zz)])
                    {
                        mountainDepth[i] = 1;
                        queue[tail++] = i;
                        break;
                    }
                }
            }
            while (head < tail)
            {
                int i = queue[head++];
                int x = i % sx, z = i / sx;
                foreach (var d in Dirs)
                {
                    int xx = x + d.x, zz = z + d.y;
                    if (!Inside(xx, zz))
                        continue;
                    int j = Idx(xx, zz);
                    if (border[j] && mountainDepth[j] == 0)
                    {
                        mountainDepth[j] = mountainDepth[i] + 1;
                        queue[tail++] = j;
                    }
                }
            }

            // Подъём от подножия, гребни (ridged noise) и отдельные вершины
            for (int i = 0; i < n; i++)
            {
                if (!border[i])
                    continue;
                var p = new float2(i % sx, i / sx);
                float t = math.saturate((float)mountainDepth[i] / s.mountainRise);
                t = t * t * (3f - 2f * t);
                float ridge = 1f - math.abs(noise.snoise(p * 0.05f + mountainOffset));
                float peaks = noise.snoise(p * 0.12f + mountainOffset * 1.7f);
                float h = s.highHeight + 1 + t * (s.mountainHeight * (0.3f + 0.7f * ridge)) + peaks * 3f;
                height[i] = math.clamp((int)math.round(h), s.highHeight + 1, sy - 3);
            }
        }

        // ---------- 7. Коридоры: от ворот к воротам противника и к каждой точке захвата ----------

        void BuildCorridors()
        {
            corridor = new bool[n];
            var previous = new int[n];
            var seen = Walk(gateOutA, wallMask, previous);

            var goals = new List<int2> { gateOutB };
            goals.AddRange(captures);

            foreach (var goal in goals)
            {
                int cur = NearestSeen(goal, seen, CaptureReachRadius);
                while (cur >= 0)
                {
                    MarkCorridor(cur);
                    MarkCorridor(Mirror(cur));
                    cur = previous[cur];
                }
            }
        }

        void MarkCorridor(int i)
        {
            int x = i % sx, z = i / sx;
            for (int dz = -CorridorRadius; dz <= CorridorRadius; dz++)
                for (int dx = -CorridorRadius; dx <= CorridorRadius; dx++)
                    if (Inside(x + dx, z + dz))
                        corridor[Idx(x + dx, z + dz)] = true;
        }

        // ---------- 8. Деревья и камни ----------

        void PlaceTreesAndRocks(ArenaLayout layout)
        {
            var forest = SymmetricNoise(s.forestNoiseScale, 2, forestOffset);
            float density = s.forestDensity * ArenaBiomes.ForestDensityScale(biome);
            float rockChance = s.rockChance * ArenaBiomes.RockChanceScale(biome);

            var blocked = new bool[n];
            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    int i = Idx(x, z);
                    blocked[i] |= reserved[i] || water[i] || shore[i] || corridor[i] || border[i];
                    if (!rampMask[i])
                        continue;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (Inside(x + dx, z + dz))
                                blocked[Idx(x + dx, z + dz)] = true;
                }
            }

            var occupied = new bool[n];
            var candidates = new List<int>();
            for (int i = 0; i < n / 2; i++)
                candidates.Add(i);
            Shuffle(candidates);

            foreach (int i in candidates)
            {
                bool tree;
                if (forest[i] > s.forestThreshold && rnd.NextDouble() < density)
                    tree = true;
                else if (rnd.NextDouble() < rockChance)
                    tree = false;
                else
                    continue;

                int mi = Mirror(i);
                if (!Free(i, blocked, occupied) || !Flat3(i) || !Free(mi, blocked, occupied) || !Flat3(mi))
                    continue;

                foreach (int c in new[] { i, mi })
                {
                    var cell = new int2(c % sx, c / sx);
                    (tree ? layout.trees : layout.rocks).Add(cell);
                    Mark(c, occupied);
                }
            }

            PlaceMountainTrees(layout, forest, density);
        }

        // Пологий склон: соседи отличаются по высоте не больше чем на блок
        bool Gentle(int x, int z)
        {
            int h = height[Idx(x, z)];
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (!Inside(x + dx, z + dz) || math.abs(height[Idx(x + dx, z + dz)] - h) > 1)
                        return false;
            return true;
        }

        // Лес на нижних пологих склонах гор. Горы непроходимы, симметрия тут не нужна.
        void PlaceMountainTrees(ArenaLayout layout, float[] forest, float density)
        {
            var taken = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (!border[i] || mountainDepth[i] < 2 || mountainDepth[i] > 10)
                    continue;
                if (forest[i] < s.forestThreshold - 0.1f || rnd.NextDouble() > density * 0.35)
                    continue;

                int x = i % sx, z = i / sx;
                if (!Gentle(x, z))
                    continue;

                bool free = true;
                for (int dz = -2; dz <= 2 && free; dz++)
                    for (int dx = -2; dx <= 2 && free; dx++)
                        free = Inside(x + dx, z + dz) && !taken[Idx(x + dx, z + dz)];
                if (!free)
                    continue;

                layout.trees.Add(new int2(x, z));
                for (int dz = -2; dz <= 2; dz++)
                    for (int dx = -2; dx <= 2; dx++)
                        taken[Idx(x + dx, z + dz)] = true;
            }
        }

        // Дерево или камень занимает квадрат 5x5
        bool Free(int i, bool[] blocked, bool[] occupied)
        {
            int x = i % sx, z = i / sx;
            for (int dz = -2; dz <= 2; dz++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int xx = x + dx, zz = z + dz;
                    if (!Inside(xx, zz))
                        return false;
                    int j = Idx(xx, zz);
                    if (blocked[j] || occupied[j])
                        return false;
                }
            }
            return true;
        }

        bool Flat3(int i)
        {
            int x = i % sx, z = i / sx;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (!Inside(x + dx, z + dz) || height[Idx(x + dx, z + dz)] != height[i])
                        return false;
            return true;
        }

        void Mark(int i, bool[] occupied)
        {
            int x = i % sx, z = i / sx;
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                    if (Inside(x + dx, z + dz))
                        occupied[Idx(x + dx, z + dz)] = true;
        }

        // ---------- Итог ----------

        void FillLayout(ArenaLayout layout)
        {
            layout.height = new int[n];
            layout.waterTop = new int[n];
            layout.flags = new byte[n];

            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    int i = Idx(x, z);
                    byte flags = 0;
                    if (shore[i])
                        flags |= ArenaLayout.FlagSand;
                    if (rampMask[i])
                        flags |= ArenaLayout.FlagRamp;

                    if (border[i])
                    {
                        layout.height[i] = height[i];
                        flags |= ArenaLayout.FlagRock;
                        if (Gentle(x, z))
                            flags |= ArenaLayout.FlagSoftTop;
                    }
                    else if (water[i])
                    {
                        // Дно на 3 блока ниже берега, вода на блок ниже берега
                        int ground = levelHeights[level[i]];
                        layout.height[i] = ground - 3;
                        layout.waterTop[i] = ground - 1;
                        flags |= ArenaLayout.FlagSand;
                    }
                    else
                    {
                        layout.height[i] = height[i];
                    }

                    layout.flags[i] = flags;
                }
            }

            // Край обрыва: сосед ниже на 2 и больше — стенка будет каменной
            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    int i = Idx(x, z);
                    if (water[i])
                        continue;
                    foreach (var d in Dirs)
                    {
                        int xx = x + d.x, zz = z + d.y;
                        if (Inside(xx, zz) && layout.height[Idx(xx, zz)] <= layout.height[i] - 2)
                            layout.flags[i] |= ArenaLayout.FlagCliff;
                    }
                }
            }
        }

        // Финальная проверка со всеми препятствиями: стены, деревья, камни, рудные бугры
        void CheckReachability(ArenaLayout layout)
        {
            var obstacles = (bool[])wallMask.Clone();
            foreach (var cell in layout.trees)
                MarkSquare(obstacles, cell, 2);
            foreach (var cell in layout.rocks)
                MarkSquare(obstacles, cell, 2);
            foreach (var r in layout.resources)
                MarkSquare(obstacles, r.cell, 2);

            var seen = Walk(gateOutA, obstacles, null);

            layout.basesConnected = NearestSeen(gateOutB, seen, 1) >= 0;
            layout.capturePointsReachable = true;
            foreach (var c in captures)
                if (NearestSeen(c, seen, CaptureReachRadius) < 0)
                    layout.capturePointsReachable = false;
        }

        void FillBaseZones(ArenaLayout layout)
        {
            layout.baseZone = new byte[n];
            layout.baseArea = new byte[n];

            // Сначала 3-й уровень, потом меньшие поверх: в клетке остаётся наименьший уровень
            int[] sizes = { s.wallLevel3Size, s.wallLevel2Size, s.wallLevel1Size };
            for (int k = 0; k < 3; k++)
            {
                int level = 3 - k;
                int h = sizes[k] / 2;
                for (int dz = -h; dz < h; dz++)
                {
                    for (int dx = -h; dx < h; dx++)
                    {
                        var cell = baseA + Rotate(new int2(dx, dz));
                        layout.baseZone[Idx(cell)] = (byte)level;
                        layout.baseZone[Idx(Mirror(cell))] = (byte)(4 + level);
                    }
                }
            }

            for (int dz = -baseHalf; dz <= baseHalf; dz++)
            {
                for (int dx = -baseHalf; dx <= baseHalf; dx++)
                {
                    var cell = baseA + new int2(dx, dz);
                    if (!Inside(cell))
                        continue;
                    layout.baseArea[Idx(cell)] = 1;
                    layout.baseArea[Idx(Mirror(cell))] = 2;
                }
            }
        }

        void MarkSquare(bool[] mask, int2 c, int r)
        {
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                    if (Inside(c.x + dx, c.y + dz))
                        mask[Idx(c.x + dx, c.y + dz)] = true;
        }

        void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
