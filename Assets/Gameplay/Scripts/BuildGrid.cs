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

        public bool CanPlace(Faction faction, StructureDef def, int2 min, out string reason, out CapturePoint capturePoint)
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

            reason = null;
            return true;
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
