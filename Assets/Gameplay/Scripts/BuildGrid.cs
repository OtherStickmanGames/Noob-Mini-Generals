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
        // Клетки месторождений (3x3) — на них можно ставить только добытчик
        readonly bool[] depositCells;

        public BuildGrid(VoxelArena arena)
        {
            this.arena = arena;
            layout = arena.Layout;
            sx = layout.sizeX;
            sz = layout.sizeZ;
            occupied = new bool[sx * sz];
            depositCells = new bool[sx * sz];

            foreach (var r in layout.resources)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        depositCells[Idx(r.cell.x + dx, r.cell.y + dz)] = true;
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
                return CanPlaceExtractor(faction, def, min, out reason, out capturePoint);

            // Зона: внутри своих стен или на площадке своей базы
            for (int z = min.y; z < min.y + size.y; z++)
            {
                for (int x = min.x; x < min.x + size.x; x++)
                {
                    int i = Idx(x, z);
                    if (depositCells[i])
                    {
                        reason = "Здесь месторождение — только для добытчика";
                        return false;
                    }

                    if (def.rule == PlacementRule.InsideWalls && !InsideOwnWalls(faction, i))
                    {
                        reason = "Строить можно только внутри стен";
                        return false;
                    }

                    if (def.rule == PlacementRule.BaseArea && layout.baseArea[i] != faction.team + 1)
                    {
                        reason = "Оборону можно ставить только у своей базы";
                        return false;
                    }
                }
            }

            // Ровно и без воды
            int y = arena.SurfaceY(min.x, min.y);
            for (int z = min.y; z < min.y + size.y; z++)
            {
                for (int x = min.x; x < min.x + size.x; x++)
                {
                    if (arena.SurfaceY(x, z) != y || arena.IsWaterAt(x, z))
                    {
                        reason = "Земля неровная";
                        return false;
                    }
                }
            }

            reason = null;
            return true;
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

        // Добытчик: центр — на месторождении своей базы (внутри открытых стен) или на своей точке захвата
        bool CanPlaceExtractor(Faction faction, StructureDef def, int2 min, out string reason, out CapturePoint capturePoint)
        {
            capturePoint = null;
            var center = min + def.footprint / 2;

            foreach (var r in layout.resources)
            {
                if (!r.cell.Equals(center))
                    continue;

                if (r.kind == ResourceKind.BaseDeposit)
                {
                    if (r.team != faction.team)
                    {
                        reason = "Это месторождение противника";
                        return false;
                    }
                    if (r.wallLevel > faction.wallLevel)
                    {
                        reason = $"Откроется со стенами {r.wallLevel}-го уровня";
                        return false;
                    }
                    reason = null;
                    return true;
                }

                var point = MatchManager.Instance.CapturePointAt(center);
                if (point == null || point.Owner != faction.team)
                {
                    reason = "Сначала захватите точку";
                    return false;
                }
                if (point.Extractor != null)
                {
                    reason = "На точке уже есть добытчик";
                    return false;
                }
                capturePoint = point;
                reason = null;
                return true;
            }

            reason = "Добытчик ставится на месторождение или захваченную точку";
            return false;
        }

        /// <summary>Центр ближайшего к клетке месторождения или точки захвата в радиусе — для подсказки при установке</summary>
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
