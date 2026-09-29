using Unity.Collections;
using Unity.Mathematics;
using static VoxelBlocks;

/// <summary>
/// Ставит в воксели деревья, камни и рудные площадки из раскладки.
/// Всё из вокселей, поэтому разрушается взрывами наравне с рельефом.
/// </summary>
public static class ArenaDecorator
{
    public static void Decorate(NativeArray<byte> voxels, int3 dims, ArenaLayout layout, ArenaBiome biome, int seed, int wallHeight)
    {
        var writer = new Writer(voxels, dims);
        var rnd = new System.Random(seed * 31 + 5);

        foreach (var cell in layout.trees)
        {
            int ground = layout.height[cell.y * dims.x + cell.x];
            var root = new int3(cell.x, ground, cell.y);

            switch (biome)
            {
                case ArenaBiome.Winter:
                    Pine(writer, root, rnd);
                    break;
                case ArenaBiome.Desert:
                    Cactus(writer, root, rnd);
                    break;
                default:
                    RoundTree(writer, root, rnd, biome == ArenaBiome.Autumn);
                    break;
            }
        }

        foreach (var cell in layout.rocks)
        {
            int ground = layout.height[cell.y * dims.x + cell.x];
            Rock(writer, new int3(cell.x, ground, cell.y), rnd);
        }

        foreach (var wall in layout.walls)
        {
            int ground = layout.height[wall.cell.y * dims.x + wall.cell.x];
            int top = wallHeight + (wall.outer && Merlon(wall.cell) ? 1 : 0);
            for (int y = 0; y < top; y++)
                writer.Set(new int3(wall.cell.x, ground + y, wall.cell.y), Wall);
        }

        foreach (var resource in layout.resources)
        {
            int ground = layout.height[resource.cell.y * dims.x + resource.cell.x];
            var root = new int3(resource.cell.x, ground, resource.cell.y);

            if (resource.kind == ResourceKind.BaseDeposit)
                DepositMound(writer, root);
            else
                CapturePoint(writer, root);
        }
    }

    // Зубцы: два блока через два
    static bool Merlon(int2 cell) => ((cell.x + cell.y) / 2) % 2 == 0;

    // Месторождение на базе: руда 3x3 вровень с землёй и бугор в центре
    static void DepositMound(Writer w, int3 root)
    {
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                w.Set(root + new int3(dx, -1, dz), GoldOre);

        w.Set(root, GoldOre);
        w.Set(root + new int3(1, 0, 0), GoldOre);
        w.Set(root + new int3(0, 0, 1), GoldOre);
        w.Set(root + new int3(0, 1, 0), GoldOre);
    }

    // Точка захвата: светлое кольцо в земле радиусом 3 и рудный бугор в центре
    static void CapturePoint(Writer w, int3 root)
    {
        for (int dz = -3; dz <= 3; dz++)
        {
            for (int dx = -3; dx <= 3; dx++)
            {
                bool ring = math.max(math.abs(dx), math.abs(dz)) == 3;
                if (ring)
                    w.Set(root + new int3(dx, -1, dz), Marker);
            }
        }

        OreMound(w, root, IronOre);
    }

    // Лиственное дерево: ствол и круглая крона; осенью крона пёстрая
    static void RoundTree(Writer w, int3 root, System.Random rnd, bool autumn)
    {
        int trunk = 3 + rnd.Next(2);
        for (int y = 0; y < trunk; y++)
            w.Set(root + new int3(0, y, 0), Wood);

        var center = root + new int3(0, trunk, 0);
        float radius = 2.2f + (float)rnd.NextDouble() * 0.5f;
        int r = (int)math.ceil(radius);

        for (int dy = -1; dy <= r; dy++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    // Крона чуть сплюснута снизу
                    float d = math.length(new float3(dx, dy * (dy < 0 ? 1.6f : 1f), dz));
                    if (d > radius)
                        continue;

                    var p = center + new int3(dx, dy, dz);
                    if (w.Get(p) == Wood)
                        continue;

                    double alt = autumn ? 0.4 : 0.25;
                    w.SetIfAir(p, rnd.NextDouble() < alt ? LeavesAlt : Leaves);
                }
            }
        }
    }

    // Ель: ствол и конус из слоёв хвои, сверху снег
    static void Pine(Writer w, int3 root, System.Random rnd)
    {
        int trunk = 2;
        int layers = 5 + rnd.Next(2);
        for (int y = 0; y < trunk + layers - 1; y++)
            w.Set(root + new int3(0, y, 0), Wood);

        for (int l = 0; l < layers; l++)
        {
            int y = trunk + l;
            int r = math.max(0, 2 - l / 2);
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (math.abs(dx) + math.abs(dz) > r + 1)
                        continue;
                    var p = root + new int3(dx, y, dz);
                    if (dx == 0 && dz == 0 && l < layers - 1)
                        continue;
                    w.SetIfAir(p, Leaves);
                }
            }
        }

        // Снег на верхушке и на внешних краях слоёв
        w.SetIfAir(root + new int3(0, trunk + layers, 0), Snow);
        for (int l = 0; l < layers; l += 2)
        {
            int y = trunk + l + 1;
            int r = math.max(0, 2 - l / 2);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                    if (w.Get(root + new int3(dx, y - 1, dz)) == Leaves && rnd.NextDouble() < 0.5)
                        w.SetIfAir(root + new int3(dx, y, dz), Snow);
        }
    }

    // Кактус: столб и одна-две «руки»
    static void Cactus(Writer w, int3 root, System.Random rnd)
    {
        int height = 3 + rnd.Next(3);
        for (int y = 0; y < height; y++)
            w.Set(root + new int3(0, y, 0), Leaves);

        int arms = 1 + rnd.Next(2);
        for (int a = 0; a < arms; a++)
        {
            var side = (rnd.Next(4)) switch
            {
                0 => new int3(1, 0, 0),
                1 => new int3(-1, 0, 0),
                2 => new int3(0, 0, 1),
                _ => new int3(0, 0, -1),
            };
            int at = 1 + rnd.Next(math.max(1, height - 2));
            var p = root + new int3(0, at, 0) + side;
            w.SetIfAir(p, LeavesAlt);
            w.SetIfAir(p + new int3(0, 1, 0), LeavesAlt);
            if (rnd.Next(2) == 0)
                w.SetIfAir(p + new int3(0, 2, 0), LeavesAlt);
        }
    }

    // Валун: неровный сплюснутый ком камня
    static void Rock(Writer w, int3 root, System.Random rnd)
    {
        float radius = 1.4f + (float)rnd.NextDouble() * 1.2f;
        int r = (int)math.ceil(radius);
        var stretch = new float3(1f + (float)rnd.NextDouble() * 0.4f, 1.3f, 1f + (float)rnd.NextDouble() * 0.4f);

        for (int dy = -1; dy <= r; dy++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    float d = math.length(new float3(dx, dy, dz) * stretch);
                    if (d > radius + (float)rnd.NextDouble() * 0.4f)
                        continue;
                    w.Set(root + new int3(dx, dy, dz), Stone);
                }
            }
        }
    }

    // Рудная площадка: верхний слой вокруг — руда, в центре бугор из руды
    static void OreMound(Writer w, int3 root, byte ore)
    {
        for (int dz = -2; dz <= 2; dz++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                if (math.abs(dx) == 2 && math.abs(dz) == 2)
                    continue;
                var ground = root + new int3(dx, -1, dz);
                if (w.Get(ground) != Air)
                    w.Set(ground, ore);
            }
        }

        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                w.Set(root + new int3(dx, 0, dz), ore);

        w.Set(root + new int3(0, 1, 0), ore);
    }

    struct Writer
    {
        NativeArray<byte> voxels;
        readonly int3 dims;

        public Writer(NativeArray<byte> voxels, int3 dims)
        {
            this.voxels = voxels;
            this.dims = dims;
        }

        bool Inside(int3 p) => math.all(p >= new int3(0, 1, 0)) && math.all(p < dims);

        int Index(int3 p) => (p.y * dims.z + p.z) * dims.x + p.x;

        public byte Get(int3 p) => Inside(p) ? voxels[Index(p)] : Air;

        public void Set(int3 p, byte block)
        {
            if (Inside(p))
                voxels[Index(p)] = block;
        }

        public void SetIfAir(int3 p, byte block)
        {
            if (Inside(p) && voxels[Index(p)] == Air)
                voxels[Index(p)] = block;
        }
    }
}
