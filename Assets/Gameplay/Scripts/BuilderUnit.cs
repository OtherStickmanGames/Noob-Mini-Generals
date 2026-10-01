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
        float stuckTime;
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
            agent.speed = 3.5f;
            agent.angularSpeed = 540f;
            agent.acceleration = 12f;
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

            if (Target.DistanceTo(transform.position) <= WorkDistance || AtWorkPoint())
            {
                agent.isStopped = true;
                var look = Target.transform.position - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), Time.deltaTime * 8f);

                Target.AddWork(Time.deltaTime);
            }
            else if (!agent.pathPending &&
                     (agent.remainingDistance < 0.3f || (agent.pathStatus != NavMeshPathStatus.PathComplete && !pointByPartialPath) || Stuck()))
            {
                // Дошёл, но до здания далеко, стоит на месте (толпа, вставший в проходе) или путь оборвался
                // (новое здание, пролом) — другая точка; эту больше не брать (автотест: строитель сотни
                // секунд стоял в 1.7 и 5.2 м от стройки, снова и снова выбирая ту же точку)
                repathTimer -= Time.deltaTime;
                if (repathTimer <= 0f)
                {
                    repathTimer = RetargetInterval;
                    // Путь агента оборвался (после пересборки NavMesh агент ищет путь сам, с ограничением на
                    // число узлов, и длинный путь через ворота вокруг стены у него неполный) — полный путь
                    // проверочным поиском от текущего места, к той же точке
                    if (workPoint.HasValue && !pointByPartialPath && agent.pathStatus != NavMeshPathStatus.PathComplete &&
                        NavMesh.CalculatePath(transform.position, workPoint.Value, Nav.Infantry, path) &&
                        path.status == NavMeshPathStatus.PathComplete)
                    {
                        // Путь есть — идти к той же точке (готовым путём или, если агент его не принял, своим)
                        if (!agent.SetPath(path))
                            agent.SetDestination(workPoint.Value);
                        stuckTime = 0f;
                        return;
                    }
                    // Точка недостижима, только если дошёл до конца пути, а стройка далеко, или пути к ней
                    // нет; просто стоял — пробует ту же снова
                    if (workPoint.HasValue && stuckTime <= 2f)
                    {
                        failedPoints.Add(workPoint.Value);
                        if (failedPoints.Count <= 2)
                            Debug.Log($"[Стройка] Строитель стороны {Faction.team} отбросил точку {workPoint.Value} у «{Target.Def.name}»: " +
                                      $"путь агента {agent.pathStatus}, осталось {agent.remainingDistance:0.00}, до точки " +
                                      $"{Vector3.Distance(transform.position, workPoint.Value):0.00}, до стройки {Target.DistanceTo(transform.position):0.00}, " +
                                      $"цель агента {agent.destination}, углов пути {agent.path.corners.Length}, на NavMesh {agent.isOnNavMesh}");
                    }
                    stuckTime = 0f;
                    GoToWorkPoint();
                }
            }
        }

        // Идёт к ближайшей по пути точке у края стройки. Пути нет ни к одной — отпускает стройку.
        void GoToWorkPoint()
        {
            if (FindWorkPoint(Target, out var point))
            {
                Target.UnreachableReported = false;
                workPoint = point;
                agent.isStopped = false;
                // Найденный полный путь — агенту напрямую: свой путь агент ищет с ограничением на число
                // узлов, и длинный путь (через ворота вокруг стены) у него обрывался на полдороге
                if (!agent.SetPath(path))
                    agent.SetDestination(point);
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
                    if (path.status == NavMeshPathStatus.PathComplete)
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

        bool Failed(Vector3 point)
        {
            foreach (var p in failedPoints)
                if ((p - point).sqrMagnitude < 0.8f * 0.8f)
                    return true;
            return false;
        }

        // Должен идти (путь есть, до точки далеко), но почти не двигается дольше 2 с
        bool Stuck()
        {
            bool moving = agent.hasPath && !agent.isStopped && agent.remainingDistance > 0.5f;
            if (!moving || agent.velocity.sqrMagnitude > 0.2f * 0.2f)
            {
                stuckTime = 0f;
                return false;
            }
            stuckTime += Time.deltaTime;
            return stuckTime > 2f;
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
            if (!NavMesh.SamplePosition(point, out var hit, 3f, Nav.Infantry))
                return;
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }

        void Release()
        {
            if (Target != null && Target.AssignedBuilder == this)
                Target.AssignedBuilder = null;
            Target = null;
            workPoint = null;
            failedPoints.Clear();
            retargetTimer = 0f;
        }

        void OnDestroy()
        {
            Release();
        }
    }
}
