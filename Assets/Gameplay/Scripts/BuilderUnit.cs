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
        const float WorkDistance = 1.2f;
        const float EdgeMargin = 0.8f;
        const float RetargetInterval = 0.5f;
        // Точки работы вокруг стройки
        const float CandidateSpacing = 1f;
        const float CornerInset = 0.2f;
        const float SampleRadius = 0.4f;

        NavMeshPath path;

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

            if (Target.DistanceTo(transform.position) <= WorkDistance)
            {
                agent.isStopped = true;
                var look = Target.transform.position - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), Time.deltaTime * 8f);

                Target.AddWork(Time.deltaTime);
            }
            else if (!agent.pathPending &&
                     (agent.remainingDistance < 0.3f || agent.pathStatus != NavMeshPathStatus.PathComplete))
            {
                // Дошёл, но до здания далеко, или путь оборвался (новое здание, пролом) — точка заново
                repathTimer -= Time.deltaTime;
                if (repathTimer <= 0f)
                {
                    repathTimer = RetargetInterval;
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
                agent.isStopped = false;
                agent.SetDestination(point);
                return;
            }

            var site = Target;
            Release();
            retargetTimer = RetargetInterval;
            MatchManager.Instance.ReportUnreachableSite(site);
        }

        /// <summary>
        /// Точки вдоль всех четырёх сторон здания на расстоянии работы; из тех, что на NavMesh на
        /// уровне земли и до которых есть полный путь, — самая короткая по пути.
        /// Ближайшая точка края по прямой не годится: с той стороны может быть узкий проход
        /// до соседнего здания, а ближайший кусок NavMesh — на верху стены.
        /// </summary>
        bool FindWorkPoint(Structure site, out Vector3 point)
        {
            point = default;
            float best = float.MaxValue;
            var center = site.transform.position;
            var half = site.HalfExtents;

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

                    if (!NavMesh.SamplePosition(OnGround(candidate), out var hit, SampleRadius, NavMesh.AllAreas) ||
                        site.DistanceTo(hit.position) > WorkDistance)
                        continue;

                    if (!NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, path) ||
                        path.status != NavMeshPathStatus.PathComplete)
                        continue;

                    float pathLength = PathLength(path);
                    if (pathLength < best)
                    {
                        best = pathLength;
                        point = hit.position;
                    }
                }
            }
            return best < float.MaxValue;
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

        static float PathLength(NavMeshPath path)
        {
            var corners = path.corners;
            float sum = 0f;
            for (int i = 1; i < corners.Length; i++)
                sum += Vector3.Distance(corners[i - 1], corners[i]);
            return sum;
        }

        void MoveTo(Vector3 point)
        {
            if (!NavMesh.SamplePosition(point, out var hit, 3f, NavMesh.AllAreas))
                return;
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }

        void Release()
        {
            if (Target != null && Target.AssignedBuilder == this)
                Target.AssignedBuilder = null;
            Target = null;
            retargetTimer = 0f;
        }

        void OnDestroy()
        {
            Release();
        }
    }
}
