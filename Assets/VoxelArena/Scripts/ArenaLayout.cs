using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public enum ResourceKind
{
    /// <summary>Месторождение базового ресурса внутри стен</summary>
    BaseDeposit,
    /// <summary>Точка захвата с ценным ресурсом в поле</summary>
    CapturePoint,
}

public struct ResourcePoint
{
    public int2 cell;
    public ResourceKind kind;
    /// <summary>Для месторождений — уровень стен, внутри которого оно лежит (1..3). Команда: 0 — база один, 1 — база два.</summary>
    public int wallLevel;
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

    public int sizeX;
    public int sizeZ;
    public int[] height;    // число сплошных блоков в столбце
    public int[] waterTop;  // 0 — нет воды, иначе вода стоит до этого y (не включая)
    public byte[] flags;

    public int2 baseOne;
    public int2 baseTwo;
    /// <summary>Стены 1-го уровня обеих баз</summary>
    public readonly List<WallCell> walls = new();
    /// <summary>Середина проёма ворот: [0] — база один, [1] — база два</summary>
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
    public float reachableLand;
}

/// <summary>
/// Генератор раскладки: сначала структура (уровни, базы, вода, рампы по графу связности),
/// потом детали. Вся карта симметрична поворотом на 180° вокруг центра.
/// Прототип и проверка связности отлаживались на Python на тех же шагах.
/// </summary>
public static class ArenaLayoutGenerator
{
    public static ArenaLayout Generate(ArenaGenSettings settings, ArenaBiome biome, int sizeX, int sizeZ)
    {
        return new Builder(settings, biome, sizeX, sizeZ).Run();
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
        readonly int sx, sz, n;
        readonly int[] levelHeights;
        readonly System.Random rnd;
        readonly float2 levelOffset, waterOffset, forestOffset;

        int[] level;
        bool[] baseMask, reserved, water, shore, rampMask;
        int[] height;
        int2 baseA, baseB;

        // Компоненты (участки)
        int[] comp;
        readonly List<int> compSize = new();
        readonly List<int> compCell = new();
        int[] parent;

        readonly List<ResourcePoint> resources = new();
        readonly List<WallCell> walls = new();
        readonly List<int2> gates = new();
        bool[] wallMask;
        int baseHalf;  // полуразмер ровной площадки базы
        int rampCount;

        public Builder(ArenaGenSettings settings, ArenaBiome biome, int sizeX, int sizeZ)
        {
            s = settings;
            this.biome = biome;
            sx = sizeX;
            sz = sizeZ;
            n = sx * sz;
            levelHeights = new[] { s.lowHeight, s.midHeight, s.highHeight };
            rnd = new System.Random(s.seed);

            var random = new Unity.Mathematics.Random(math.hash(new int2(s.seed, 7)) | 1u);
            levelOffset = random.NextFloat2(new float2(-5000f), new float2(5000f));
            waterOffset = random.NextFloat2(new float2(-5000f), new float2(5000f));
            forestOffset = random.NextFloat2(new float2(-5000f), new float2(5000f));
        }

        int Idx(int x, int z) => z * sx + x;
        bool Inside(int x, int z) => x >= 0 && z >= 0 && x < sx && z < sz;
        int Mirror(int i) => n - 1 - i;
        int2 Mirror(int2 c) => new(sx - 1 - c.x, sz - 1 - c.y);

        public ArenaLayout Run()
        {
            int cx = (sx - 1) / 2;
            baseHalf = s.wallLevel3Size / 2 + s.wallThickness + s.basePadding;
            baseA = new int2(cx, baseHalf + 2);
            baseB = Mirror(baseA);

            BuildLevels();
            SmoothLevels();
            RemoveSmallRegions();
            PlaceBaseContent();
            PlaceCapturePoints();
            PlaceWater();

            int flattened = 0;
            bool connected = false;
            for (int attempt = 0; attempt < 6 && !connected; attempt++)
                connected = PlaceRamps(ref flattened);

            var layout = new ArenaLayout
            {
                sizeX = sx,
                sizeZ = sz,
                baseOne = baseA,
                baseTwo = baseB,
                regionCount = compSize.Count,
                rampCount = rampCount,
                flattenedRegions = flattened,
            };
            layout.resources.AddRange(resources);
            layout.walls.AddRange(walls);
            layout.gates.AddRange(gates);

            PlaceTreesAndRocks(layout);
            FillLayout(layout);
            CheckReachability(layout);
            return layout;
        }

        // ---------- 1. Уровни ----------

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

        void BuildLevels()
        {
            var values = SymmetricNoise(s.levelNoiseScale, 3, levelOffset);

            // Пороги по долям площади, чтобы соотношение уровней не зависело от сида
            var sorted = (float[])values.Clone();
            Array.Sort(sorted);
            float t1 = sorted[Mathf.Clamp((int)(n * s.lowShare), 0, n - 1)];
            float t2 = sorted[Mathf.Clamp((int)(n * (1f - s.highShare)), 0, n - 1)];

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

            ForceBases();
        }

        void ForceBases()
        {
            for (int i = 0; i < n; i++)
                if (baseMask[i])
                    level[i] = 1;
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
                ForceBases();
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

                ForceBases();
            }
        }

        // ---------- 4. База: стены, ворота, месторождения ----------

        void PlaceBaseContent()
        {
            reserved = (bool[])baseMask.Clone();
            wallMask = new bool[n];

            // Стены 1-го уровня базы A, ворота смотрят на противника (+z). База B — зеркально.
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
                    var cell = baseA + new int2(dx, dz);
                    AddWall(cell, outer);
                    AddWall(Mirror(cell), outer);
                }
            }

            var gateA = baseA + new int2(0, h);
            gates.Add(gateA);
            gates.Add(Mirror(gateA));

            // Месторождения базового ресурса: по два внутри каждого уровня стен.
            // Бугор 3x3 не должен налезать на стены ни одного уровня (при 24/36/48 и толщине 2).
            AddDeposit(new int2(-8, -7), 1);
            AddDeposit(new int2(8, -7), 1);
            AddDeposit(new int2(-16, 5), 2);
            AddDeposit(new int2(16, 5), 2);
            AddDeposit(new int2(-22, -12), 3);
            AddDeposit(new int2(22, -12), 3);
        }

        void AddWall(int2 cell, bool outer)
        {
            walls.Add(new WallCell { cell = cell, outer = outer });
            wallMask[Idx(cell.x, cell.y)] = true;
        }

        void AddDeposit(int2 offset, int wallLevel)
        {
            var cell = baseA + offset;
            resources.Add(new ResourcePoint { cell = cell, kind = ResourceKind.BaseDeposit, wallLevel = wallLevel, team = 0 });
            resources.Add(new ResourcePoint { cell = Mirror(cell), kind = ResourceKind.BaseDeposit, wallLevel = wallLevel, team = 1 });
        }

        // ---------- Точки захвата ----------

        void PlaceCapturePoints()
        {
            int cx = (sx - 1) / 2;
            var targets = new[]
            {
                new int2(cx, baseA.y + baseHalf + 14),                          // ближняя
                new int2((int)(cx - sx * 0.32f), (int)(sz * 0.4f)),             // фланги
                new int2((int)(cx + sx * 0.32f), (int)(sz * 0.4f)),
                new int2((int)(cx + sx * 0.18f), sz / 2 - 4),                   // спорная у центра
            };

            foreach (var target in targets)
            {
                if (!SnapCapturePoint(target, out var cell))
                    continue;

                foreach (var c in new[] { cell, Mirror(cell) })
                {
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
            for (int r = 0; r < 16; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (math.max(math.abs(dx), math.abs(dz)) != r)
                            continue;
                        var c = target + new int2(dx, dz);
                        if (Uniform9(c))
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

        bool Uniform9(int2 c)
        {
            if (!Inside(c.x, c.y))
                return false;
            int l = level[Idx(c.x, c.y)];
            for (int dz = -4; dz <= 4; dz++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = c.x + dx, z = c.y + dz;
                    if (!Inside(x, z) || level[Idx(x, z)] != l || reserved[Idx(x, z)])
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
                land[i] = !water[i];

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

            int baseComp = comp[Idx(baseA.x, baseA.y)];
            if (AllConnected(baseComp))
                return true;

            // Запасной путь 1: узкие рампы к оторванным участкам
            foreach (long key in pairs)
            {
                int a = (int)(key / 1_000_000), b = (int)(key % 1_000_000);
                bool aLinked = Find(a) == Find(baseComp);
                bool bLinked = Find(b) == Find(baseComp);
                if (aLinked == bLinked || !TryAddRamp(pairSites[key], 3))
                    continue;

                Union(a, b);
                Union(MirrorComp(a), MirrorComp(b));
            }

            if (AllConnected(baseComp))
                return true;

            // Запасной путь 2: оторванный участок выравнивается под соседний связанный уровень
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

        // ---------- 7. Деревья и камни ----------

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
                    blocked[i] |= reserved[i] || water[i] || shore[i];
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
                if (!Free(i, 2, blocked, occupied) || !Flat3(i) || !Free(mi, 2, blocked, occupied) || !Flat3(mi))
                    continue;

                foreach (int c in new[] { i, mi })
                {
                    var cell = new int2(c % sx, c / sx);
                    (tree ? layout.trees : layout.rocks).Add(cell);
                    Mark(c, 2, occupied);
                }
            }
        }

        bool Free(int i, int r, bool[] blocked, bool[] occupied)
        {
            int x = i % sx, z = i / sx;
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
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

        void Mark(int i, int r, bool[] occupied)
        {
            int x = i % sx, z = i / sx;
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                    if (Inside(x + dx, z + dz))
                        occupied[Idx(x + dx, z + dz)] = true;
        }

        // ---------- Итог ----------

        void FillLayout(ArenaLayout layout)
        {
            layout.height = new int[n];
            layout.waterTop = new int[n];
            layout.flags = new byte[n];

            for (int i = 0; i < n; i++)
            {
                byte flags = 0;
                if (shore[i])
                    flags |= ArenaLayout.FlagSand;
                if (rampMask[i])
                    flags |= ArenaLayout.FlagRamp;

                if (water[i])
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

        // Проверка: от базы по суше с шагом не больше блока до всего остального
        void CheckReachability(ArenaLayout layout)
        {
            var seen = new bool[n];
            var queue = new int[n];
            int head = 0, tail = 0;
            int start = Idx(baseA.x, baseA.y);
            queue[tail++] = start;
            seen[start] = true;

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
                    if (seen[j] || water[j] || wallMask[j] || math.abs(layout.height[j] - layout.height[i]) > 1)
                        continue;
                    seen[j] = true;
                    queue[tail++] = j;
                }
            }

            int landCells = 0, reached = 0;
            for (int i = 0; i < n; i++)
            {
                if (water[i] || wallMask[i])
                    continue;
                landCells++;
                if (seen[i])
                    reached++;
            }

            layout.basesConnected = seen[Idx(baseB.x, baseB.y)];
            layout.capturePointsReachable = true;
            foreach (var r in layout.resources)
                if (r.kind == ResourceKind.CapturePoint && !seen[Idx(r.cell.x, r.cell.y)])
                    layout.capturePointsReachable = false;
            layout.reachableLand = landCells > 0 ? (float)reached / landCells : 0f;
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
