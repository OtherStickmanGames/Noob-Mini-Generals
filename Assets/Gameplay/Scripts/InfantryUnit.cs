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
        public const float ChestHeight = 1.05f;
        /// <summary>Сколько секунд почти без движения по пути считается «застрял»</summary>
        const float StuckTime = 1.5f;
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
        float stuckTime;
        Vector3 destination = new(float.MaxValue, 0f, 0f);

        // Атака построек: цель и своя позиция вокруг неё
        IAreaTarget attackTarget;
        bool hasSlot;
        Vector3 slot;
        int blockedChecks;

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
            SetAttackTarget(null);
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
            CheckStuck();
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
            // К постройке — лицом к её ближнему краю (точка прицела у неё каждый выстрел своя)
            var look = (Target is IAreaTarget area ? area.ClosestEdgePoint(transform.position, 0f) : Target.AimPoint(transform.position))
                       - transform.position;
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

            // Атака: бойцы рядом важнее построек
            var unit = NearestEnemyUnit(enemy, transform.position, UnitCatalog.InfantrySight);
            if (unit != null)
            {
                Engage(unit);
                return;
            }

            // Постройка — случайная из ближайших (здание или участок стены), бьём до разрушения
            // Шёл на участок стены, но уже прошёл внутрь (в пролом или ворота) — теперь здания
            bool wallBehind = attackTarget is WallSegment && match.IsInsideWalls(transform.position, enemy) &&
                              enemy.structures.Count > 0;
            if (!Combat.IsAlive(attackTarget) || wallBehind)
                SetAttackTarget(match.ChooseAttackTarget(this, null));
            if (attackTarget != null)
                EngageArea(attackTarget);
            else
                Hold(transform.position);
        }

        void SetAttackTarget(IAreaTarget target)
        {
            if (hasSlot)
                MatchManager.Instance.ReleaseAttackSlot(this, attackTarget);
            hasSlot = false;
            attackTarget = target;
        }

        // Постройку бьют со своей позиции вокруг неё (MatchManager.ClaimAttackSlot): так отряд
        // расходится кольцом и не встаёт толпой в воротах, загораживая проход остальным
        void EngageArea(IAreaTarget target)
        {
            var match = MatchManager.Instance;
            Target = target;

            if (!hasSlot)
            {
                hasSlot = match.ClaimAttackSlot(this, target, weapon.Def.range, null, out slot);
                if (!hasSlot)
                {
                    // Вокруг этой цели места нет (всё занято или не видно) — в следующий раз другая
                    SetAttackTarget(match.ChooseAttackTarget(this, target));
                    Firing = false;
                    MoveTo(target.ClosestEdgePoint(transform.position, 1f));
                    return;
                }
            }

            if ((Flat(slot) - Flat(transform.position)).sqrMagnitude < 0.7f * 0.7f)
            {
                // Точка прицела каждый раз случайная (целый воксель цели) и может оказаться закрыта —
                // с позиции уходим, только если цель не видна несколько проверок подряд
                if (Combat.HasLineOfFire(Muzzle, target, Faction))
                    blockedChecks = 0;
                else
                    blockedChecks++;

                if (blockedChecks < 4)
                {
                    Firing = true;
                    if (agent.isOnNavMesh)
                        agent.isStopped = true;
                    return;
                }
                blockedChecks = 0;
                match.ReleaseAttackSlot(this, target);
                hasSlot = false;
            }

            Firing = false;
            MoveTo(slot);
        }

        // Боец идёт, но почти не движется (упёрся в других) — дольше StuckTime: другая позиция
        void CheckStuck()
        {
            bool moving = !Firing && agent.isOnNavMesh && !agent.isStopped && !agent.pathPending &&
                          agent.hasPath && agent.remainingDistance > 0.8f;
            if (!moving || agent.velocity.sqrMagnitude > 0.3f * 0.3f)
            {
                stuckTime = 0f;
                return;
            }

            stuckTime += Time.deltaTime;
            if (stuckTime < StuckTime)
                return;
            stuckTime = 0f;

            destination = new Vector3(float.MaxValue, 0f, 0f);
            if (hasSlot && Combat.IsAlive(attackTarget))
            {
                var old = slot;
                hasSlot = MatchManager.Instance.ClaimAttackSlot(this, attackTarget, weapon.Def.range, old, out slot);
            }
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

        // Боец противника: на дальности и с линией огня — встать и стрелять; иначе подойти ближе
        void Engage(InfantryUnit target)
        {
            Target = target;
            float distance = Vector3.Distance(Flat(target.transform.position), Flat(transform.position));
            if (distance <= weapon.Def.range * 0.9f && Combat.HasLineOfFire(Muzzle, target, Faction))
            {
                Firing = true;
                if (agent.isOnNavMesh)
                    agent.isStopped = true;
                return;
            }

            Firing = false;
            MoveTo(target.transform.position);
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

        public void TakeDamage(float amount, Vector3 point, Vector3 direction)
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
            if (hasSlot && MatchManager.Instance != null)
                MatchManager.Instance.ReleaseAttackSlot(this, attackTarget);
        }
    }
}
