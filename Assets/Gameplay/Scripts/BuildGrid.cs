using Unity.Mathematics;

namespace Generals
{
    /// <summary>
    /// Сетка застройки по клеткам земли: занятость и проверка, можно ли поставить здание.
    /// </summary>
    public class BuildGrid
    {
        readonly VoxelArena arena;
        readonly ArenaLayout layout;
        readonly int sx, sz;
        readonly bool[] occupied;
        // Клетки точек захвата (3x3) — на них можно ставить только шахту
        readonly bool[] depositCells;
        // Номер точки захвата в layout.resources + 1 для каждой клетки (0 — не точка)
        readonly int[] depositIndex;
        // Клетки стен; турель можно ставить на верх стены (между зубцами)
        readonly bool[] wallCells;
        // Высота верха стены без зубцов для базы один и два
        readonly int[] wallTop = { int.MaxValue, int.MaxValue };

        public BuildGrid(VoxelArena arena)
        {
            this.arena = arena;
            layout = arena.Layout;
            sx = layout.sizeX;
            sz = layout.sizeZ;
            occupied = new bool[sx * sz];
            depositCells = new bool[sx * sz];
            depositIndex = new int[sx * sz];
            wallCells = new bool[sx * sz];

            foreach (var w in layout.walls)
            {
                int i = Idx(w.cell.x, w.cell.y);
                wallCells[i] = true;
                int team = layout.baseArea[i] - 1;
                if (team >= 0)
                    wallTop[team] = math.min(wallTop[team], arena.SurfaceY(w.cell.x, w.cell.y));
            }

            for (int k = 0; k < layout.resources.Count; k++)
            {
                var r = layout.resources[k];
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int i = Idx(r.cell.x + dx, r.cell.y + dz);
                        depositCells[i] = true;
                        depositIndex[i] = k + 1;
                    }
                }
            }
        }

        int Idx(int x, int z) => z * sx + x;
        bool Inside(int x, int z) => x >= 0 && z >= 0 && x < sx && z < sz;

        /// <summary>Угол прямоугольника здания по клетке, в которую тапнули (здание по центру)</summary>
        public static int2 MinFromCenter(StructureDef def, int2 center) => center - def.footprint / 2;

        public void SetOccupied(int2 min, int2 size, bool value)
        {
            for (int z = min.y; z < min.y + size.y; z++)
                for (int x = min.x; x < min.x + size.x; x++)
                    if (Inside(x, z))
                        occupied[Idx(x, z)] = value;
        }

        /// <summary>
        /// Можно ли поставить здание. checkPassages = false — без проверки проходов (она дороже): для
        /// перебора многих мест, а проходы — потом у выбранного (KeepsPassages)
        /// </summary>
        public bool CanPlace(Faction faction, StructureDef def, int2 min, out string reason, out CapturePoint capturePoint,
                             bool checkPassages = true)
        {
            capturePoint = null;
            var size = def.footprint;

            for (int z = min.y; z < min.y + size.y; z++)
            {
                for (int x = min.x; x < min.x + size.x; x++)
                {
                    if (!Inside(x, z))
                    {
                        reason = "За краем карты";
                        return false;
                    }
                    if (occupied[Idx(x, z)])
                    {
                        reason = "Место занято";
                        return false;
                    }
                }
            }

            if (def.rule == PlacementRule.Deposit)
                return CanPlaceMine(faction, def, min, out reason, out capturePoint);

            for (int z = min.y; z < min.y + size.y; z++)
            {
                for (int x = min.x; x < min.x + size.x; x++)
                {
                    reason = CellReason(faction, def.rule, x, z);
                    if (reason != null)
                        return false;
                }
            }

            // Ровно: вся площадка на одной высоте
            int y = arena.SurfaceY(min.x, min.y);
            for (int z = min.y; z < min.y + size.y; z++)
            {
                for (int x = min.x; x < min.x + size.x; x++)
                {
                    if (arena.SurfaceY(x, z) != y)
                    {
                        reason = "Земля неровная";
                        return false;
                    }
                }
            }

            reason = checkPassages ? PassageReason(faction, def, min) : null;
            return reason == null;
        }

        /// <summary>Здание здесь не перекроет проходы на базе (см. PassageReason)</summary>
        public bool KeepsPassages(Faction faction, StructureDef def, int2 min, out string reason)
        {
            reason = def.rule == PlacementRule.Deposit ? null : PassageReason(faction, def, min);
            return reason == null;
        }

        // ---------- Проходы ----------

        // Просвет вокруг клетки, по которой пройдёт агент (NavMesh сужает проходы на его радиус с
        // каждой стороны): пехоте (радиус 0.5 м) нужен проход в 3 клетки, технике (1.1 м) — в 5
        const int InfantryClearance = 1;
        const int VehicleClearance = 2;
        // С базы нужно уметь выйти: заливка от ворот должна дойти до края области базы
        const int BaseMargin = 4;

        bool[] blockedCells, walkCells, reached;
        int[] floodQueue;
        int2 boxMin, boxSize;

        /// <summary>
        /// Не перекроет ли здание проходы (как правило «нельзя загородить» в RTS со стройкой): с базы
        /// можно выйти через ворота, к каждому своему зданию на базе есть подход (строителю и
        /// выходящим бойцам), от машинного завода есть проезд к воротам под ширину машины.
        /// null — всё в порядке, иначе — причина для подсказки.
        /// </summary>
        string PassageReason(Faction faction, StructureDef def, int2 min)
        {
            int team = faction.team;
            PrepareBox(team);
            MarkBlocked(min, def.footprint);

            var gate = layout.gates[team];
            var structures = MatchManager.Instance.GetFaction(team).structures;

            Flood(gate, InfantryClearance, out bool exit);
            if (!exit)
                return "Перекроет выход с базы";
            if (!Reachable(min, def.footprint, InfantryClearance + 1))
                return "Сюда не подойдёт строитель";
            foreach (var s in structures)
                if (s != null && InBox(s.MinCell) && !Reachable(s.MinCell, s.Def.footprint, InfantryClearance + 1 + (OnWall(s) ? 2 : 0)))
                    return $"Перекроет проход к «{s.Def.name}»";

            bool anyFactory = def.type == StructureType.Factory;
            foreach (var s in structures)
                anyFactory |= s != null && s.Def.type == StructureType.Factory;
            if (!anyFactory)
                return null;

            Flood(gate, VehicleClearance, out _);
            if (def.type == StructureType.Factory && !Reachable(min, def.footprint, VehicleClearance + 2))
                return "Заводу нужен проезд к воротам для техники";
            foreach (var s in structures)
                if (s != null && s.Def.type == StructureType.Factory && !Reachable(s.MinCell, s.Def.footprint, VehicleClearance + 2))
                    return "Перекроет проезд от завода к воротам";
            return null;
        }

        bool OnWall(Structure s) => wallCells[Idx(s.MinCell.x, s.MinCell.y)];

        // Область базы стороны (где своя зона застройки или площадка у стен) с запасом по краям;
        // в ней — непроходимые клетки: стены, вода, деревья и камни, здания
        void PrepareBox(int team)
        {
            if (boxes == null)
                FindBoxes();
            (boxMin, boxSize) = boxes[team];

            int n = boxSize.x * boxSize.y;
            if (blockedCells == null || blockedCells.Length < n)
            {
                blockedCells = new bool[n];
                walkCells = new bool[n];
                reached = new bool[n];
                floodQueue = new int[n];
            }

            // Рельеф (стены, вода, деревья) — раз в кадр на сторону: место ищут перебором
            var terrain = terrainCells[team];
            if (terrain == null || terrainFrame[team] != UnityEngine.Time.frameCount)
            {
                terrain = terrainCells[team] ??= new bool[n];
                terrainFrame[team] = UnityEngine.Time.frameCount;
                for (int bz = 0; bz < boxSize.y; bz++)
                {
                    for (int bx = 0; bx < boxSize.x; bx++)
                    {
                        int x = boxMin.x + bx, z = boxMin.y + bz;
                        int i = Idx(x, z);
                        terrain[bz * boxSize.x + bx] = wallCells[i] || arena.IsWaterAt(x, z) ||
                                                       arena.SurfaceY(x, z) > layout.height[i];
                    }
                }
            }

            for (int bz = 0; bz < boxSize.y; bz++)
            {
                for (int bx = 0; bx < boxSize.x; bx++)
                {
                    int k = bz * boxSize.x + bx;
                    blockedCells[k] = terrain[k] || occupied[Idx(boxMin.x + bx, boxMin.y + bz)];
                }
            }
        }

        (int2 min, int2 size)[] boxes;
        readonly bool[][] terrainCells = new bool[2][];
        readonly int[] terrainFrame = { -1, -1 };

        // Область базы каждой стороны: площадка у стен с запасом BaseMargin
        void FindBoxes()
        {
            boxes = new (int2, int2)[2];
            for (int team = 0; team < 2; team++)
            {
                int2 lo = new(sx, sz), hi = new(-1, -1);
                for (int z = 0; z < sz; z++)
                {
                    for (int x = 0; x < sx; x++)
                    {
                        if (layout.baseArea[Idx(x, z)] != team + 1)
                            continue;
                        lo = math.min(lo, new int2(x, z));
                        hi = math.max(hi, new int2(x, z));
                    }
                }
                var bmin = math.max(lo - BaseMargin, 0);
                var bmax = math.min(hi + BaseMargin, new int2(sx - 1, sz - 1));
                boxes[team] = (bmin, bmax - bmin + 1);
            }
        }

        bool InBox(int2 cell) => math.all(cell >= boxMin) && math.all(cell < boxMin + boxSize);

        void MarkBlocked(int2 min, int2 size)
        {
            for (int z = min.y; z < min.y + size.y; z++)
                for (int x = min.x; x < min.x + size.x; x++)
                    if (InBox(new int2(x, z)))
                        blockedCells[(z - boxMin.y) * boxSize.x + (x - boxMin.x)] = true;
        }

        // Заливка от клетки по клеткам с просветом clearance; exit — дошла до края области (выход в поле)
        void Flood(int2 start, int clearance, out bool exit)
        {
            int w = boxSize.x, h = boxSize.y, n = w * h;
            for (int i = 0; i < n; i++)
            {
                int bx = i % w, bz = i / w;
                bool free = bx >= clearance && bz >= clearance && bx < w - clearance && bz < h - clearance;
                for (int dz = -clearance; dz <= clearance && free; dz++)
                    for (int dx = -clearance; dx <= clearance && free; dx++)
                        free = !blockedCells[(bz + dz) * w + bx + dx];
                walkCells[i] = free;
                reached[i] = false;
            }

            exit = false;
            // Ворота — клетка снаружи прохода; если она сама без просвета — ближайшая с просветом рядом
            int s = -1;
            var local = start - boxMin;
            for (int r = 0; r <= 3 && s < 0; r++)
                for (int dz = -r; dz <= r && s < 0; dz++)
                    for (int dx = -r; dx <= r && s < 0; dx++)
                    {
                        int bx = local.x + dx, bz = local.y + dz;
                        if (bx >= 0 && bz >= 0 && bx < w && bz < h && walkCells[bz * w + bx])
                            s = bz * w + bx;
                    }
            if (s < 0)
                return;

            int head = 0, tail = 0;
            floodQueue[tail++] = s;
            reached[s] = true;
            while (head < tail)
            {
                int i = floodQueue[head++];
                int bx = i % w, bz = i / w;
                if (bx <= clearance || bz <= clearance || bx >= w - 1 - clearance || bz >= h - 1 - clearance)
                    exit = true;
                Visit(bx + 1, bz, ref tail);
                Visit(bx - 1, bz, ref tail);
                Visit(bx, bz + 1, ref tail);
                Visit(bx, bz - 1, ref tail);
            }
        }

        void Visit(int bx, int bz, ref int tail)
        {
            if (bx < 0 || bz < 0 || bx >= boxSize.x || bz >= boxSize.y)
                return;
            int j = bz * boxSize.x + bx;
            if (reached[j] || !walkCells[j])
                return;
            reached[j] = true;
            floodQueue[tail++] = j;
        }

        // До прямоугольника можно дойти: заливка достала клетку не дальше distance от его края
        bool Reachable(int2 min, int2 size, int distance)
        {
            var lo = min - distance - boxMin;
            var hi = min + size - 1 + distance - boxMin;
            for (int bz = math.max(lo.y, 0); bz <= math.min(hi.y, boxSize.y - 1); bz++)
                for (int bx = math.max(lo.x, 0); bx <= math.min(hi.x, boxSize.x - 1); bx++)
                    if (reached[bz * boxSize.x + bx])
                        return true;
            return false;
        }

        /// <summary>
        /// Можно ли занять одну клетку зданием с таким правилом (без проверки ровности площадки).
        /// Для сетки застройки: зелёная клетка — можно, красная — нельзя.
        /// </summary>
        public bool IsCellBuildable(Faction faction, PlacementRule rule, int x, int z)
        {
            if (!Inside(x, z) || occupied[Idx(x, z)])
                return false;

            if (rule == PlacementRule.Deposit)
            {
                int k = depositIndex[Idx(x, z)] - 1;
                if (k < 0)
                    return false;
                var point = MatchManager.Instance.CapturePointAt(layout.resources[k].cell);
                return point != null && point.Owner == faction.team && point.Mine == null;
            }

            return CellReason(faction, rule, x, z) == null;
        }

        // Почему клетку нельзя занять зданием с этим правилом; null — можно
        string CellReason(Faction faction, PlacementRule rule, int x, int z)
        {
            int i = Idx(x, z);
            if (depositCells[i])
                return "Здесь точка захвата — только для шахты";

            if (rule == PlacementRule.InsideWalls && !InsideOwnWalls(faction, i))
                return "Строить можно только внутри стен";

            if (rule == PlacementRule.BaseArea && layout.baseArea[i] != faction.team + 1)
                return "Оборону можно ставить только у своей базы";

            if (arena.IsWaterAt(x, z))
                return "Здесь вода";

            int surface = arena.SurfaceY(x, z);

            // Оборону можно ставить на верх своей стены, но не на зубцы
            if (rule == PlacementRule.BaseArea && wallCells[i])
                return surface == wallTop[faction.team] ? null : "Здесь зубец стены";

            // Над землёй что-то стоит (стена, дерево, камень) или земля разрушена
            if (surface != layout.height[i])
                return "Место занято";

            return null;
        }

        bool InsideOwnWalls(Faction faction, int i)
        {
            int zone = layout.baseZone[i];
            if (zone == 0)
                return false;
            int team = zone >= 4 ? 1 : 0;
            int level = zone >= 4 ? zone - 4 : zone;
            return team == faction.team && level <= faction.wallLevel;
        }

        // Шахта: центр — на своей точке захвата, где ещё нет шахты
        bool CanPlaceMine(Faction faction, StructureDef def, int2 min, out string reason, out CapturePoint capturePoint)
        {
            capturePoint = null;
            var point = MatchManager.Instance.CapturePointAt(min + def.footprint / 2);
            if (point == null)
            {
                reason = "Шахта ставится на точку захвата";
                return false;
            }
            if (point.Owner != faction.team)
            {
                reason = "Сначала захватите точку";
                return false;
            }
            if (point.Mine != null)
            {
                reason = "На точке уже есть шахта";
                return false;
            }

            capturePoint = point;
            reason = null;
            return true;
        }

        /// <summary>
        /// Ближайшее к центру место, где здание можно поставить: расходящимися квадратами до radius клеток
        /// </summary>
        public bool FindNearest(Faction faction, StructureDef def, int2 center, int radius, out int2 min)
        {
            var start = MinFromCenter(def, center);
            for (int r = 0; r <= radius; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (math.max(math.abs(dx), math.abs(dz)) != r)
                            continue;
                        min = start + new int2(dx, dz);
                        if (CanPlace(faction, def, min, out _, out _))
                            return true;
                    }
                }
            }
            min = start;
            return false;
        }

        /// <summary>
        /// Ближайшее к центру место, не задевающее другие здания и край карты (правила зоны не проверяются).
        /// Куда поставить призрак здания, если подходящего места рядом нет.
        /// </summary>
        public bool FindNearestFree(StructureDef def, int2 center, int radius, out int2 min)
        {
            var start = MinFromCenter(def, center);
            for (int r = 0; r <= radius; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (math.max(math.abs(dx), math.abs(dz)) != r)
                            continue;
                        min = start + new int2(dx, dz);
                        if (IsFree(min, def.footprint))
                            return true;
                    }
                }
            }
            min = start;
            return false;
        }

        /// <summary>Вокруг прямоугольника на margin клеток нет других зданий (край карты не мешает)</summary>
        public bool HasClearance(int2 min, int2 size, int margin)
        {
            for (int z = min.y - margin; z < min.y + size.y + margin; z++)
                for (int x = min.x - margin; x < min.x + size.x + margin; x++)
                    if (Inside(x, z) && occupied[Idx(x, z)])
                        return false;
            return true;
        }

        bool IsFree(int2 min, int2 size)
        {
            for (int z = min.y; z < min.y + size.y; z++)
                for (int x = min.x; x < min.x + size.x; x++)
                    if (!Inside(x, z) || occupied[Idx(x, z)])
                        return false;
            return true;
        }

        /// <summary>Центр ближайшей к клетке точки захвата в радиусе — для подсказки при установке</summary>
        public bool SnapToResource(int2 cell, int radius, out int2 center)
        {
            center = cell;
            int best = int.MaxValue;
            foreach (var r in layout.resources)
            {
                int d = math.max(math.abs(r.cell.x - cell.x), math.abs(r.cell.y - cell.y));
                if (d <= radius && d < best)
                {
                    best = d;
                    center = r.cell;
                }
            }
            return best != int.MaxValue;
        }
    }
}
