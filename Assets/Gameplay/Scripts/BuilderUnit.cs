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
        const float EdgeMargin = 0.7f;
        const float RetargetInterval = 0.5f;

        public Faction Faction { get; private set; }
        public Structure Target { get; private set; }

        NavMeshAgent agent;
        float retargetTimer;
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
                    MoveTo(Target.ClosestEdgePoint(transform.position, EdgeMargin));
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
            else if (!agent.pathPending && agent.remainingDistance < 0.3f)
            {
                // Дошёл, но до стены здания далеко (путь упёрся) — ещё раз к ближайшему краю
                MoveTo(Target.ClosestEdgePoint(transform.position, EdgeMargin));
            }
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
