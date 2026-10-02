using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Generals
{
    /// <summary>
    /// Строитель. Игрок им не управляет: он сам берёт ближайшую незанятую стройку своей стороны,
    /// идёт к ней и строит. Без работы возвращается к главному зданию.
    /// </summary>
    public class BuilderUnit : MonoBehaviour
    {
        const float WorkDistance = 2f;
        const float Speed = 3.5f;
        const float RetargetInterval = 0.5f;
        // Агент встаёт у точки с такой погрешностью: точка работы выбирается с запасом на неё,
        // а стоящий у своей точки строитель работает, даже если до здания чуть дальше WorkDistance
        const float ArriveTolerance = 0.35f;
        // Точка работы — не дальше этого от здания
        const float MaxPointDistance = WorkDistance - ArriveTolerance;
        // Кандидаты — на таком расстоянии от края здания: дальше края NavMesh у здания (вырез на радиус
        // агента 0.5 м) и заметно ближе MaxPointDistance, чтобы сдвиг к NavMesh не выводил их за предел
        const float EdgeMargin = 0.75f;
        // Точки работы вокруг стройки
        const float CandidateSpacing = 1f;
        const float CornerInset = 0.2f;
        const float SampleRadius = 0.3f;
        // По одной неудаче на сторону здания
        const int MaxPathFailures = 4;

        NavMeshPath path;
        Vector3? workPoint;
        // Точки этой стройки, до которых строитель так и не дошёл — больше их не выбирать
        readonly List<Vector3> failedPoints = new();
        // Строит прямо сейчас (стоит у стройки)
        bool working;
        // Последний поиск точки: кандидатов, не на NavMesh, отброшено, без пути — для сообщения в консоль
        Unity.Mathematics.int4 searchStats;
        int searchOnWall, searchFailed;
        // Точка работы — конец неполного пути у самой стройки: неполный путь агента тогда не обрыв
        bool pointByPartialPath;
        float searchFar;
        readonly List<(float distance, Vector3 position)>[] sideCandidates =
            { new(), new(), new(), new() };

        public Faction Faction { get; private set; }
        public Structure Target { get; private set; }
        /// <summary>Куда идёт работать (для отладки и автотеста)</summary>
        public Vector3? WorkPoint => workPoint;
        public bool Working => working;

        NavMeshAgent agent;
        float retargetTimer;
        float repathTimer;
        bool goingHome;

        public void Init(Faction faction, Material material)
        {
            Faction = faction;
            name = $"Строитель {faction.team}";

            var mesh = VoxelModels.Builder(faction.team);
            var size = VoxelModels.Size(mesh);
            var model = new GameObject("Model");
            model.transform.SetParent(transform, false);
            model.transform.localScale = Vector3.one * VoxelModels.VoxelSize;
            model.transform.localPosition = new Vector3(-size.x, 0f, -size.z) * (VoxelModels.VoxelSize * 0.5f);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterial = material;

            path = new NavMeshPath();
            agent = gameObject.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 2f;
            // Строитель идёт сам по маршруту (FollowRoute), агент только следует за ним
            agent.updatePosition = false;
            agent.updateRotation = false;
            // Сквозь своих и чужих бойцов, как рабочие в RTS: толпа у ворот не должна задерживать стройку
            // (автотест: строитель вставал в толпе и одну за другой отбрасывал все точки у стройки)
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        }

        void Update()
        {
            if (Target != null && Target.IsBuilt)
                Release();

            if (Target == null)
            {
                retargetTimer -= Time.deltaTime;
                if (retargetTimer > 0f)
                    return;
                retargetTimer = RetargetInterval;

                Target = MatchManager.Instance.ClaimSite(this);
                if (Target != null)
                {
                    goingHome = false;
                    GoToWorkPoint();
                }
                else if (!goingHome && Faction.headquarters != null)
                {
                    goingHome = true;
                    MoveTo(Faction.headquarters.ClosestEdgePoint(transform.position, 2f));
                }
                return;
            }

            // Работает, пока у стройки; начав, продолжает с запасом: небольшой сдвиг не обрывает работу
            // (автотест: строитель начал в 2 м, его сдвинуло на 2.1 — и он 4 минуты стоял, не работая)
            float reach = working ? WorkDistance + 0.5f : WorkDistance;
            if (Target.DistanceTo(transform.position) <= reach || AtWorkPoint())
            {
                working = true;
                route.Clear();
                var look = Target.transform.position - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), Time.deltaTime * 8f);

                Target.AddWork(Time.deltaTime);
                return;
            }
            working = false;

            if (FollowRoute())
                return;

            // Маршрут кончился (или упёрся), а до стройки далеко — разобраться, не чаще RetargetInterval
            repathTimer -= Time.deltaTime;
            if (repathTimer > 0f)
                return;
            repathTimer = RetargetInterval;

            // К той же точке есть полный путь от текущего места — новый маршрут
            if (workPoint.HasValue && NavMesh.CalculatePath(transform.position, workPoint.Value, Nav.Infantry, path) &&
                LeadsTo(path, workPoint.Value))
            {
                SetRoute(path);
                return;
            }

            // Точка недостижима — больше её не брать
            if (workPoint.HasValue)
            {
                failedPoints.Add(workPoint.Value);
                if (failedPoints.Count <= 2)
                    Debug.Log($"[Стройка] Строитель стороны {Faction.team} отбросил точку {workPoint.Value} у «{Target.Def.name}»: " +
                              $"до точки {Vector3.Distance(transform.position, workPoint.Value):0.00}, до стройки " +
                              $"{Target.DistanceTo(transform.position):0.00}");
            }
            GoToWorkPoint();
        }

        // ---------- Движение по маршруту ----------
        // Строитель идёт сам по углам заранее найденного полного пути (NavMesh.CalculatePath; путь лежит
        // на сетке), агент только следует за ним. Собственный поиск пути агента не используется: у агента
        // путь ищется по общей очереди с ограничением на узлы и на длинных маршрутах обрывался или
        // минутами висел в очереди (автотест: pathPending), а строителю путь и так известен.

        readonly List<Vector3> route = new();
        int routeIndex;
        float rayTimer;

        void SetRoute(NavMeshPath newPath)
        {
            route.Clear();
            route.AddRange(newPath.corners);
            routeIndex = 1;
            // Маршрут начинается на том куске NavMesh, к которому его привязал поиск; агент мог остаться
            // на соседнем, отрезанном (стройка вырезала себя рядом) — шаг до начала маршрута
            if (route.Count > 0 && (route[0] - transform.position).sqrMagnitude < 1.5f * 1.5f)
            {
                transform.position = route[0];
                agent.nextPosition = route[0];
            }
        }

        // Шаг по маршруту; false — маршрута нет или он пройден
        bool FollowRoute()
        {
            if (routeIndex >= route.Count)
                return false;

            var target = route[routeIndex];
            // На маршруте заложили здание (оно вырезало себя из NavMesh) — маршрут пересчитать
            rayTimer -= Time.deltaTime;
            if (rayTimer <= 0f)
            {
                rayTimer = 0.25f;
                if (NavMesh.Raycast(transform.position, target, out _, Nav.Infantry))
                {
                    route.Clear();
                    return false;
                }
            }
            var position = Vector3.MoveTowards(transform.position, target, Speed * Time.deltaTime);
            var direction = target - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 720f * Time.deltaTime);
            transform.position = position;
            agent.nextPosition = position;
            if ((position - target).sqrMagnitude < 0.01f)
                routeIndex++;
            return true;
        }

        // Идёт к ближайшей по пути точке у края стройки. Пути нет ни к одной — отпускает стройку.
        void GoToWorkPoint()
        {
            if (FindWorkPoint(Target, out var point))
            {
                Target.UnreachableReported = false;
                working = false;
                workPoint = point;
                SetRoute(path);
                return;
            }

            var site = Target;
            Release();
            retargetTimer = RetargetInterval;
            MatchManager.Instance.ReportUnreachableSite(site,
                $"точек {searchStats.x}, не на NavMesh {searchStats.y}, далеко {searchStats.z} (например {searchFar:0.00} м), на стене {searchOnWall}, уже не дошёл {searchFailed}, " +
                $"путь не найден {searchStats.w}; строитель в {transform.position}");
        }

        /// <summary>
        /// Точки вдоль всех четырёх сторон здания на расстоянии работы (на NavMesh у земли, не на
        /// стене). Проверяются по кругу сторон — ближайшая точка каждой стороны, потом следующие, —
        /// первая, до которой есть путь, и есть точка работы. Ближайшая точка края без проверки пути
        /// не годится: с той стороны может быть узкий проход до соседнего здания или стены (карман
        /// NavMesh). Поиск пути к недостижимой точке обходит весь NavMesh, поэтому неудач — не больше
        /// MaxPathFailures; по кругу сторон карман с одной стороны не съедает их все. Неполный путь
        /// годится, если кончается у самой стройки.
        /// </summary>
        bool FindWorkPoint(Structure site, out Vector3 point)
        {
            pointByPartialPath = false;
            searchStats = default;
            searchOnWall = searchFailed = 0;
            searchFar = 0f;
            var match = MatchManager.Instance;
            var center = site.transform.position;
            var half = site.HalfExtents;
            for (int side = 0; side < 4; side++)
                sideCandidates[side].Clear();

            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                float sign = side % 2 == 0 ? 1f : -1f;
                float length = alongX ? half.x : half.y;
                int steps = Mathf.Max(1, Mathf.CeilToInt(length * 2f / CandidateSpacing));

                for (int k = 0; k <= steps; k++)
                {
                    float t = Mathf.Lerp(-length + CornerInset, length - CornerInset, k / (float)steps);
                    var candidate = alongX
                        ? center + new Vector3(t, 0f, sign * (half.y + EdgeMargin))
                        : center + new Vector3(sign * (half.x + EdgeMargin), 0f, t);

                    searchStats.x++;
                    if (!NavMesh.SamplePosition(OnGround(candidate), out var hit, SampleRadius, Nav.Infantry))
                    {
                        searchStats.y++;
                        continue;
                    }
                    if (site.DistanceTo(hit.position) > MaxPointDistance)
                    {
                        searchStats.z++;
                        searchFar = site.DistanceTo(hit.position);
                        continue;
                    }
                    if (match.IsOnWall(hit.position))
                    {
                        searchOnWall++;
                        continue;
                    }
                    if (Failed(hit.position))
                    {
                        searchFailed++;
                        continue;
                    }
                    sideCandidates[side].Add(((hit.position - transform.position).sqrMagnitude, hit.position));
                }
                sideCandidates[side].Sort((a, b) => a.distance.CompareTo(b.distance));
            }

            int failures = 0;
            for (int index = 0; failures < MaxPathFailures; index++)
            {
                bool any = false;
                for (int side = 0; side < 4 && failures < MaxPathFailures; side++)
                {
                    var list = sideCandidates[side];
                    if (index >= list.Count)
                        continue;
                    any = true;

                    var position = list[index].position;
                    if (!NavMesh.CalculatePath(transform.position, position, Nav.Infantry, path))
                    {
                        failures++;
                        continue;
                    }
                    if (LeadsTo(path, position))
                    {
                        point = position;
                        return true;
                    }
                    searchStats.w++;

                    // Неполный путь кончается в ближайшей достижимой точке — вдруг она у самой стройки
                    var corners = path.corners;
                    if (corners.Length > 0)
                    {
                        var end = corners[^1];
                        if (site.DistanceTo(end) <= MaxPointDistance && !match.IsOnWall(end))
                        {
                            point = end;
                            pointByPartialPath = true;
                            return true;
                        }
                    }
                    failures++;
                }
                if (!any)
                    break;
            }
            point = default;
            return false;
        }

        // Путь полный и кончается у самой точки: точка внутри вырезанной стройкой области NavMesh
        // «прилипает» к ближайшему месту на NavMesh, и путь туда тоже «полный» — но ведёт не к ней
        // (автотест: строитель минутами шёл туда, где уже стоял, в 3 м от своей точки)
        static bool LeadsTo(NavMeshPath path, Vector3 point)
        {
            if (path.status != NavMeshPathStatus.PathComplete)
                return false;
            var corners = path.corners;
            return corners.Length > 0 && (corners[^1] - point).sqrMagnitude < 0.5f * 0.5f;
        }

        bool Failed(Vector3 point)
        {
            foreach (var p in failedPoints)
                if ((p - point).sqrMagnitude < 0.8f * 0.8f)
                    return true;
            return false;
        }

        bool AtWorkPoint()
        {
            if (!workPoint.HasValue)
                return false;
            var d = workPoint.Value - transform.position;
            d.y = 0f;
            return d.sqrMagnitude <= ArriveTolerance * ArriveTolerance;
        }

        // Точка на поверхности столбца арены: у турели на стене строитель работает снизу, у подножия
        static Vector3 OnGround(Vector3 point)
        {
            var arena = MatchManager.Instance.Arena;
            var voxel = arena.WorldToVoxel(point);
            int x = Mathf.Clamp(Mathf.FloorToInt(voxel.x), 0, arena.Dims.x - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(voxel.z), 0, arena.Dims.z - 1);
            point.y = arena.ColumnTop(x, z).y;
            return point;
        }

        void MoveTo(Vector3 point)
        {
            if (NavMesh.SamplePosition(point, out var hit, 3f, Nav.Infantry) &&
                NavMesh.CalculatePath(transform.position, hit.position, Nav.Infantry, path))
                SetRoute(path);
        }

        void Release()
        {
            if (Target != null && Target.AssignedBuilder == this)
                Target.AssignedBuilder = null;
            Target = null;
            workPoint = null;
            working = false;
            failedPoints.Clear();
            retargetTimer = 0f;
        }

        void OnDestroy()
        {
            Release();
        }
    }
}
