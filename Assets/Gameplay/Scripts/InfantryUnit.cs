using UnityEngine;
using UnityEngine.AI;

namespace Generals
{
    /// <summary>
    /// Пехотинец. Игрок им не управляет: поведение берётся у его казарм.
    /// Оборона — держит свой пост у ворот базы и бросается на врагов, подошедших к посту.
    /// Атака — идёт на ближайших вражеских бойцов, а если рядом их нет — на здания противника.
    /// Подойдя на дальность стрельбы, останавливается лицом к цели (стрельба и урон — шаг 5 среза).
    /// </summary>
    public class InfantryUnit : MonoBehaviour
    {
        const float ThinkInterval = 0.25f;
        const float RepathDistance = 0.75f;

        public Faction Faction { get; private set; }
        /// <summary>Казармы бойца; null — боец без казарм (отладочный), поведение своё</summary>
        public Barracks Barracks { get; private set; }
        public float Health { get; private set; }

        public BarracksBehavior Behavior => Barracks != null ? Barracks.Behavior : ownBehavior;

        /// <summary>Текущая цель: вражеский боец или здание; null — цели нет</summary>
        public Component Target { get; private set; }

        NavMeshAgent agent;
        BarracksBehavior ownBehavior;
        float thinkTimer;
        Vector3 destination = new(float.MaxValue, 0f, 0f);

        public void Init(Faction faction, Barracks barracks, BarracksBehavior behavior, Material material)
        {
            Faction = faction;
            Barracks = barracks;
            ownBehavior = behavior;
            Health = UnitCatalog.InfantryHealth;
            name = $"{UnitCatalog.InfantryName} {faction.team}";

            var mesh = VoxelModels.Infantry(faction.team);
            var size = VoxelModels.Size(mesh);
            var model = new GameObject("Model");
            model.transform.SetParent(transform, false);
            model.transform.localScale = Vector3.one * VoxelModels.VoxelSize;
            model.transform.localPosition = new Vector3(-size.x, 0f, -size.z) * (VoxelModels.VoxelSize * 0.5f);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterial = material;

            agent = gameObject.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 2f;
            agent.speed = UnitCatalog.InfantrySpeed;
            agent.angularSpeed = 540f;
            agent.acceleration = 12f;
            agent.stoppingDistance = 0.2f;
            // Разный приоритет, чтобы толпа у ворот расходилась, а не упиралась друг в друга
            agent.avoidancePriority = Random.Range(40, 60);

            // Коллайдер — чтобы бойца можно было выбрать и чтобы в него попадали (шаг 5)
            var capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 1f, 0f);
            capsule.height = 2f;
            capsule.radius = 0.35f;
            capsule.isTrigger = true;

            thinkTimer = Random.value * ThinkInterval;
        }

        /// <summary>Казармы сменили поведение — сразу пересмотреть цель</summary>
        public void BehaviorChanged()
        {
            thinkTimer = 0f;
            Target = null;
        }

        void Update()
        {
            thinkTimer -= Time.deltaTime;
            if (thinkTimer <= 0f)
            {
                thinkTimer = ThinkInterval;
                Think();
            }

            // Стоит на дальности стрельбы — поворачивается к цели
            if (Target != null && agent.isStopped)
            {
                var look = Target.transform.position - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), Time.deltaTime * 10f);
            }
        }

        void Think()
        {
            var match = MatchManager.Instance;
            var enemy = match.GetFaction(1 - Faction.team);

            if (Behavior == BarracksBehavior.Defend)
            {
                var post = match.DefendPost(this);
                var threat = NearestEnemyUnit(enemy, post, UnitCatalog.DefendRadius);
                if (threat != null)
                    Engage(threat, threat.transform.position);
                else
                    Hold(post);
                return;
            }

            // Атака: бойцы рядом важнее зданий, из зданий — ближайшее
            var unit = NearestEnemyUnit(enemy, transform.position, UnitCatalog.InfantrySight);
            if (unit != null)
            {
                Engage(unit, unit.transform.position);
                return;
            }

            var structure = NearestEnemyStructure(enemy);
            if (structure != null)
                Engage(structure, structure.ClosestEdgePoint(transform.position, 0f));
            else
                Hold(transform.position);
        }

        InfantryUnit NearestEnemyUnit(Faction enemy, Vector3 around, float radius)
        {
            InfantryUnit best = null;
            float bestDistance = radius * radius;
            foreach (var u in enemy.units)
            {
                if (u == null)
                    continue;
                float d = (u.transform.position - around).sqrMagnitude;
                if (d < bestDistance)
                {
                    best = u;
                    bestDistance = d;
                }
            }
            return best;
        }

        Structure NearestEnemyStructure(Faction enemy)
        {
            Structure best = null;
            float bestDistance = float.MaxValue;
            foreach (var s in enemy.structures)
            {
                if (s == null)
                    continue;
                float d = s.DistanceTo(transform.position);
                if (d < bestDistance)
                {
                    best = s;
                    bestDistance = d;
                }
            }
            return best;
        }

        // Подойти на дальность стрельбы и встать
        void Engage(Component target, Vector3 aimPoint)
        {
            Target = target;
            var flat = aimPoint - transform.position;
            flat.y = 0f;
            float distance = target is Structure s ? s.DistanceTo(transform.position) : flat.magnitude;

            if (distance <= UnitCatalog.InfantryRange * 0.9f)
            {
                agent.isStopped = true;
                return;
            }
            MoveTo(aimPoint);
        }

        void Hold(Vector3 point)
        {
            Target = null;
            MoveTo(point);
        }

        void MoveTo(Vector3 point)
        {
            agent.isStopped = false;
            if ((point - destination).sqrMagnitude < RepathDistance * RepathDistance)
                return;
            if (!NavMesh.SamplePosition(point, out var hit, 3f, NavMesh.AllAreas))
                return;
            destination = point;
            agent.SetDestination(hit.position);
        }

        void OnDestroy()
        {
            Faction?.units.Remove(this);
        }
    }
}
