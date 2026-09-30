using UnityEngine;
using UnityEngine.AI;

namespace Generals
{
    /// <summary>
    /// Пехотинец. Игрок им не управляет: поведение берётся у его казарм.
    /// Оборона — держит свой пост у ворот базы и бросается на врагов, подошедших к посту.
    /// Атака — идёт на ближайших вражеских бойцов, а если рядом их нет — на здания противника.
    /// Подойдя на дальность стрельбы и увидев цель (линия огня свободна), встаёт и стреляет;
    /// нет линии огня — подходит ближе.
    /// </summary>
    public class InfantryUnit : MonoBehaviour, IDamageable
    {
        const float ThinkInterval = 0.25f;
        const float RepathDistance = 0.75f;
        const float ChestHeight = 1.05f;
        /// <summary>Стреляет, только довернувшись к цели точнее этого, градусы</summary>
        const float FireAngle = 20f;

        public Faction Faction { get; private set; }
        /// <summary>Казармы бойца; null — боец без казарм (отладочный или казармы разрушены), поведение своё</summary>
        public Barracks Barracks { get; private set; }
        public float Health { get; private set; }
        public float MaxHealth => UnitCatalog.InfantryHealth;
        public bool IsAlive => Health > 0f;
        public float LastDamageTime { get; private set; } = float.NegativeInfinity;
        public Vector3 Velocity => agent != null && agent.enabled ? agent.velocity : Vector3.zero;

        public BarracksBehavior Behavior => Barracks != null ? Barracks.Behavior : ownBehavior;

        /// <summary>Текущая цель: вражеский боец или здание; null — цели нет</summary>
        public IDamageable Target { get; private set; }
        /// <summary>Стоит на позиции и стреляет по цели</summary>
        public bool Firing { get; private set; }

        NavMeshAgent agent;
        Weapon weapon;
        BarracksBehavior ownBehavior;
        float thinkTimer;
        float navCheckTimer;
        Vector3 destination = new(float.MaxValue, 0f, 0f);

        public void Init(Faction faction, Barracks barracks, BarracksBehavior behavior, Material material)
        {
            Faction = faction;
            Barracks = barracks;
            ownBehavior = behavior;
            Health = UnitCatalog.InfantryHealth;
            weapon = new Weapon(WeaponCatalog.Rifle);
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

            // Коллайдер — чтобы в бойца попадали снаряды; триггер, поэтому тапом он не выбирается
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
            Firing = false;
        }

        /// <summary>Казармы разрушены: боец остаётся с их последним поведением</summary>
        public void DetachFromBarracks(BarracksBehavior lastBehavior)
        {
            ownBehavior = lastBehavior;
            Barracks = null;
        }

        Vector3 Muzzle => transform.position + transform.rotation * new Vector3(0.15f, ChestHeight, 0.5f);

        void Update()
        {
            if (!IsAlive)
                return;

            KeepOnNavMesh();
            weapon.Tick(Time.deltaTime);

            thinkTimer -= Time.deltaTime;
            if (thinkTimer <= 0f)
            {
                thinkTimer = ThinkInterval;
                Think();
            }

            if (!Firing || !Combat.IsAlive(Target))
                return;

            // Стоит на позиции — поворачивается к цели и стреляет
            var look = Target.AimPoint(transform.position) - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f)
                return;
            var desired = Quaternion.LookRotation(look);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, 540f * Time.deltaTime);

            if (weapon.Ready && Quaternion.Angle(transform.rotation, desired) < FireAngle)
                weapon.Fire(Faction, Muzzle, Target);
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
                    Engage(threat);
                else
                    Hold(post);
                return;
            }

            // Атака: бойцы рядом важнее зданий, из зданий — ближайшее
            var unit = NearestEnemyUnit(enemy, transform.position, UnitCatalog.InfantrySight);
            if (unit != null)
            {
                Engage(unit);
                return;
            }

            var structure = NearestEnemyStructure(enemy);
            if (structure != null)
                Engage(structure);
            else
                Hold(transform.position);
        }

        InfantryUnit NearestEnemyUnit(Faction enemy, Vector3 around, float radius)
        {
            InfantryUnit best = null;
            float bestDistance = radius * radius;
            foreach (var u in enemy.units)
            {
                if (!Combat.IsAlive(u))
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
                if (!Combat.IsAlive(s))
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

        // На дальности и с линией огня — встать и стрелять; иначе подойти ближе
        void Engage(IDamageable target)
        {
            Target = target;
            var structure = target as Structure;
            float distance = structure != null
                ? structure.DistanceTo(transform.position)
                : Vector3.Distance(Flat(target.transform.position), Flat(transform.position));

            if (distance <= weapon.Def.range * 0.9f && Combat.HasLineOfFire(Muzzle, target, Faction))
            {
                Firing = true;
                if (agent.isOnNavMesh)
                    agent.isStopped = true;
                return;
            }

            Firing = false;
            MoveTo(structure != null ? structure.ClosestEdgePoint(transform.position, 0.6f) : target.transform.position);
        }

        void Hold(Vector3 point)
        {
            Target = null;
            Firing = false;
            MoveTo(point);
        }

        void MoveTo(Vector3 point)
        {
            if (!agent.isOnNavMesh)
                return;
            agent.isStopped = false;
            if ((point - destination).sqrMagnitude < RepathDistance * RepathDistance)
                return;
            if (!NavMesh.SamplePosition(point, out var hit, 3f, NavMesh.AllAreas))
                return;
            destination = point;
            agent.SetDestination(hit.position);
        }

        // Земля под ногами может исчезнуть (воронка) — возвращаемся на навмеш
        void KeepOnNavMesh()
        {
            if (agent.isOnNavMesh)
                return;
            navCheckTimer -= Time.deltaTime;
            if (navCheckTimer > 0f)
                return;
            navCheckTimer = 0.5f;
            if (NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
                destination = new Vector3(float.MaxValue, 0f, 0f);
            }
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        // ---------- Бой ----------

        public Vector3 AimPoint(Vector3 from) => transform.position + Vector3.up * ChestHeight;

        public Vector3 MissPoint(Vector3 from)
        {
            var offset = Random.insideUnitCircle.normalized * Random.Range(0.5f, 1.4f);
            return transform.position + new Vector3(offset.x, -0.05f, offset.y);
        }

        public void TakeDamage(float amount)
        {
            if (!IsAlive)
                return;

            Health -= amount;
            LastDamageTime = Time.time;
            if (Health > 0f)
                return;

            Health = 0f;
            Faction.unitsLost++;
            agent.enabled = false;
            MatchManager.Instance.Effects.UnitDeath(transform.position, Faction.team);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            Faction?.units.Remove(this);
        }
    }
}
