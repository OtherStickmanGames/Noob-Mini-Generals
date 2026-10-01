using UnityEngine;
using UnityEngine.AI;

namespace Generals
{
    /// <summary>
    /// Машина (разведчик, танк, артиллерия) — выпускается машинным заводом, поведение («Оборона» /
    /// «Атака») — от завода, как у отрядов от казарм. Ходит по своему NavMesh (проходы на её ширину).
    /// Башня поворачивается сама и стреляет по цели в дальности: вражеские бойцы и машины важнее всего,
    /// потом турель, которая бьёт по машине, потом постройка, которую машина атакует. Разведчик и танк
    /// стреляют и на ходу, артиллерия — только стоя, навесом (линия огня не нужна) и не ближе
    /// минимальной дальности.
    /// Оборона — пост перед своими воротами, выезд на врага, подошедшего к посту.
    /// Атака — на вражеских бойцов и машины в обзоре, иначе на случайную из ближайших построек врага
    /// (своя позиция вокруг неё, как у пехоты — MatchManager.ClaimAttackSlot).
    /// </summary>
    public class VehicleUnit : MonoBehaviour, IDamageable
    {
        const float ThinkInterval = 0.25f;
        const float RepathDistance = 1f;
        // Позицию вокруг постройки ищем, когда до неё не дальше дальности оружия + столько метров
        const float SlotSearchDistance = 12f;
        // Позиции машин у постройки — не ближе этого друг к другу (и к позициям бойцов)
        const float SlotSpacing = 2.6f;
        const float StuckTime = 2f;
        /// <summary>Стреляет, когда ствол смотрит на цель точнее этого, градусы</summary>
        const float AimTolerance = 5f;

        public Faction Faction { get; private set; }
        public Factory Factory { get; private set; }
        public VehicleType Type { get; private set; }
        public VehicleDef Def { get; private set; }
        public float Health { get; private set; }
        public float MaxHealth => Def.health;
        public bool IsAlive => Health > 0f;
        public float LastDamageTime { get; private set; } = float.NegativeInfinity;
        public Vector3 Velocity => agent != null && agent.enabled ? agent.velocity : Vector3.zero;
        public BarracksBehavior Behavior => Factory != null ? Factory.Behavior : ownBehavior;
        /// <summary>По кому стреляет башня; null — ни по кому</summary>
        public IDamageable Target { get; private set; }
        /// <summary>Размер машины, м (x — ширина, y — высота, z — длина)</summary>
        public Vector3 Size { get; private set; }

        NavMeshAgent agent;
        Weapon weapon;
        Transform turret;
        Transform muzzle;
        BarracksBehavior ownBehavior;
        float thinkTimer;
        float navCheckTimer;
        float partialTimer;
        Vector3 navDestination;
        float stuckTime;
        Vector3 destination = new(float.MaxValue, 0f, 0f);
        bool stopped;

        // Постройка, которую машина атакует, и своя позиция вокруг неё
        IAreaTarget areaTarget;
        IAreaTarget slotTarget;
        bool hasSlot;
        Vector3 slot;
        int blockedChecks;

        public void Init(Faction faction, Factory factory, BarracksBehavior behavior, VehicleType type, Material material)
        {
            Faction = faction;
            Factory = factory;
            ownBehavior = behavior;
            Type = type;
            Def = VehicleCatalog.Get(type);
            Health = Def.health;
            weapon = new Weapon(Def.weapon);
            name = $"{Def.name} {faction.team}";
            float voxel = VoxelModels.VoxelSize;

            // Корпус: центр по x и z — в нуле объекта
            var hullMesh = VoxelModels.VehicleHull(type, faction.team);
            var hullSize = (Vector3)(Unity.Mathematics.float3)VoxelModels.Size(hullMesh);
            var hull = new GameObject("Hull");
            hull.transform.SetParent(transform, false);
            hull.transform.localScale = Vector3.one * voxel;
            hull.transform.localPosition = new Vector3(-hullSize.x, 0f, -hullSize.z) * (voxel * 0.5f);
            hull.AddComponent<MeshFilter>().sharedMesh = hullMesh;
            hull.AddComponent<MeshRenderer>().sharedMaterial = material;

            // Башня: ось — в точке крепления на корпусе
            var mount = VoxelModels.VehicleMount(type);
            turret = new GameObject("Turret").transform;
            turret.SetParent(transform, false);
            turret.localPosition = new Vector3(mount.x - hullSize.x * 0.5f, mount.y, mount.z - hullSize.z * 0.5f) * voxel;
            var turretMesh = VoxelModels.VehicleTurret(type, faction.team);
            var pivot = VoxelModels.VehicleTurretPivot(type);
            var turretModel = new GameObject("Model");
            turretModel.transform.SetParent(turret, false);
            turretModel.transform.localScale = Vector3.one * voxel;
            turretModel.transform.localPosition = -pivot * voxel;
            turretModel.AddComponent<MeshFilter>().sharedMesh = turretMesh;
            turretModel.AddComponent<MeshRenderer>().sharedMaterial = material;
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(turret, false);
            muzzle.localPosition = (VoxelModels.VehicleMuzzle(type) - pivot) * voxel;

            var turretSize = (Vector3)(Unity.Mathematics.float3)VoxelModels.Size(turretMesh);
            Size = new Vector3(hullSize.x, mount.y + turretSize.y, hullSize.z) * voxel;

            var nav = MatchManager.Instance.ArenaNav;
            agent = gameObject.AddComponent<NavMeshAgent>();
            agent.agentTypeID = nav.VehicleAgentTypeID;
            // Радиус — для расталкивания с другими; проходы задаёт NavMesh техники
            agent.radius = Mathf.Max(Size.x, Size.z) * 0.4f;
            agent.height = Size.y;
            agent.speed = Def.speed;
            agent.angularSpeed = Def.hullTurnSpeed;
            agent.acceleration = 8f;
            agent.stoppingDistance = 0.4f;
            agent.avoidancePriority = Random.Range(20, 35);
            agent.Warp(transform.position);

            // Коллайдер — чтобы в машину попадали снаряды; триггер, поэтому тапом она не выбирается
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, Size.y * 0.5f, 0f);
            box.size = Size;
            box.isTrigger = true;

            thinkTimer = Random.value * ThinkInterval;
        }

        /// <summary>Завод сменил поведение — сразу пересмотреть, что делать</summary>
        public void BehaviorChanged()
        {
            thinkTimer = 0f;
            areaTarget = null;
            ReleaseSlot();
        }

        /// <summary>Завод разрушен: машина остаётся с его последним поведением</summary>
        public void DetachFromFactory(BarracksBehavior lastBehavior)
        {
            ownBehavior = lastBehavior;
            Factory = null;
        }

        void Update()
        {
            if (!IsAlive)
                return;

            KeepOnNavMesh();
            ContinuePartialPath();
            CheckStuck();
            weapon.Tick(Time.deltaTime);

            thinkTimer -= Time.deltaTime;
            if (thinkTimer <= 0f)
            {
                thinkTimer = ThinkInterval;
                PickTarget();
                Think();
            }

            AimAndFire();
        }

        // ---------- Башня ----------

        void AimAndFire()
        {
            if (!Combat.IsAlive(Target))
            {
                Target = null;
                return;
            }

            var aim = Target is IAreaTarget area ? area.ClosestEdgePoint(turret.position, 0f) : Target.AimPoint(turret.position);
            var look = aim - turret.position;
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f)
                return;
            var desired = Quaternion.LookRotation(look);
            turret.rotation = Quaternion.RotateTowards(turret.rotation, desired, Def.turretTurnSpeed * Time.deltaTime);

            bool canFire = !Def.firesOnlyStopped || (stopped && agent.velocity.sqrMagnitude < 0.05f);
            if (canFire && weapon.Ready && Quaternion.Angle(turret.rotation, desired) < AimTolerance)
                weapon.Fire(Faction, muzzle.position, Target);
        }

        // Цель башни: бойцы и машины в дальности важнее, потом турель, которая бьёт по машине, потом
        // атакуемая постройка. Пули по машинам — только если пехоты рядом нет (Combat.VehicleWeight)
        void PickTarget()
        {
            var w = weapon.Def;
            float range = w.range * 0.95f;
            var enemy = MatchManager.Instance.GetFaction(1 - Faction.team);
            var from = muzzle.position;

            var units = Combat.EnemyUnitsNear(enemy, transform.position, range, w.minRange, Combat.VehicleWeight(w));
            for (int i = 0; i < units.Count && i < 3; i++)
            {
                if (w.indirect || Combat.HasLineOfFire(from, units[i].unit, Faction))
                {
                    Target = units[i].unit;
                    return;
                }
            }

            var turretStructure = TurretFiringAtMe(enemy);
            if (turretStructure != null && InRange(turretStructure) && (w.indirect || Combat.HasLineOfFire(from, turretStructure, Faction)))
            {
                Target = turretStructure;
                return;
            }

            if (Combat.IsAlive(areaTarget) && InRange(areaTarget) && (w.indirect || Combat.HasLineOfFire(from, areaTarget, Faction)))
            {
                Target = areaTarget;
                return;
            }
            Target = null;
        }

        bool InRange(IAreaTarget target)
        {
            float d = target.DistanceTo(transform.position);
            return d <= weapon.Def.range * 0.95f && d >= weapon.Def.minRange;
        }

        // Ближайшая вражеская турель, которая стреляет по этой машине
        Structure TurretFiringAtMe(Faction enemy)
        {
            Structure best = null;
            float bestDistance = float.MaxValue;
            foreach (var s in enemy.structures)
            {
                if (s.Turret == null || !Combat.IsAlive(s) || !ReferenceEquals(s.Turret.Target, this))
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

        // ---------- Движение ----------

        void Think()
        {
            var match = MatchManager.Instance;
            var enemy = match.GetFaction(1 - Faction.team);

            // Ведёт бой с бойцом или машиной — стоит и стреляет
            if (Target != null && !(Target is IAreaTarget))
            {
                Stop();
                return;
            }

            if (Behavior == BarracksBehavior.Defend)
            {
                areaTarget = null;
                ReleaseSlot();
                match.VehiclePost(this, out var post, out _);
                var threat = NearestEnemy(enemy, post, VehicleCatalog.DefendRadius);
                if (threat != null)
                    Approach(threat.transform.position);
                else
                    MoveTo(post);
                return;
            }

            // Атака: бойцы и машины врага в обзоре важнее построек
            var unit = NearestEnemy(enemy, transform.position, VehicleCatalog.Sight);
            if (unit != null)
            {
                Approach(unit.transform.position);
                return;
            }

            // Турель бьёт по машине издалека — едем на неё
            var turretStructure = TurretFiringAtMe(enemy);
            if (turretStructure != null)
                areaTarget = turretStructure;

            // Постройка — случайная из ближайших, бьём до разрушения; шли на участок стены, но уже
            // внутри стен — теперь здания
            bool wallBehind = areaTarget is WallSegment && match.IsInsideWalls(transform.position, enemy);
            if (!Combat.IsAlive(areaTarget) || wallBehind)
                areaTarget = match.ChooseAttackTarget(Faction, transform.position, null);

            if (areaTarget != null)
                EngageArea(areaTarget);
            else
                Stop();
        }

        IDamageable NearestEnemy(Faction enemy, Vector3 around, float radius)
        {
            var units = Combat.EnemyUnitsNear(enemy, around, radius, 0f, Combat.VehicleWeight(weapon.Def));
            return units.Count > 0 ? units[0].unit : null;
        }

        // Подъехать к врагу на ~80% дальности (артиллерии — не ближе минимальной)
        void Approach(Vector3 enemyPosition)
        {
            ReleaseSlot();
            var w = weapon.Def;
            var away = transform.position - enemyPosition;
            away.y = 0f;
            float keep = Mathf.Max(w.range * 0.8f, w.minRange + 1f);
            var point = away.sqrMagnitude > 0.01f ? enemyPosition + away.normalized * keep : enemyPosition;
            MoveTo(point);
        }

        // Постройку бьют со своей позиции вокруг неё; позицию ищем уже вблизи (пути короткие)
        void EngageArea(IAreaTarget target)
        {
            var match = MatchManager.Instance;
            var w = weapon.Def;
            if (slotTarget != target)
                ReleaseSlot();

            if (!hasSlot)
            {
                if (target.DistanceTo(transform.position) > w.range + SlotSearchDistance || !match.CanClaimSlotThisFrame())
                {
                    MoveTo(target.ClosestEdgePoint(transform.position, Mathf.Max(w.range * 0.6f, w.minRange + 1f)));
                    return;
                }

                float eyeHeight = muzzle.position.y - transform.position.y;
                hasSlot = match.ClaimAttackSlot(this, target, w, eyeHeight, SlotSpacing, true, null, out slot);
                if (!hasSlot)
                {
                    // Вокруг цели места нет — другая цель
                    areaTarget = match.ChooseAttackTarget(Faction, transform.position, target);
                    MoveTo(target.ClosestEdgePoint(transform.position, 2f));
                    return;
                }
                slotTarget = target;
            }

            if ((Flat(slot) - Flat(transform.position)).sqrMagnitude < 1.2f * 1.2f)
            {
                // С позиции цель не видна несколько проверок подряд — другая позиция
                blockedChecks = Target == (IDamageable)target ? 0 : blockedChecks + 1;
                if (blockedChecks < 6)
                {
                    Stop();
                    return;
                }
                ReleaseSlot();
            }
            MoveTo(slot);
        }

        public void ReleaseSlot()
        {
            if (hasSlot && MatchManager.Instance != null)
                MatchManager.Instance.ReleaseAttackSlot(this, slotTarget);
            hasSlot = false;
            slotTarget = null;
            blockedChecks = 0;
        }

        void Stop()
        {
            stopped = true;
            if (agent.isOnNavMesh)
                agent.isStopped = true;
            destination = new Vector3(float.MaxValue, 0f, 0f);
        }

        // Путь агент ищет с ограничением на число узлов: длинный путь (через ворота, вокруг стены)
        // обрывается на полдороге (PathPartial), хотя полный есть. Дошёл до конца обрывка — продолжение
        // от текущего места к той же цели (не чаще раза в 2 с: к недостижимой цели поиск дорогой)
        void ContinuePartialPath()
        {
            partialTimer -= Time.deltaTime;
            if (partialTimer > 0f || !agent.isOnNavMesh || agent.pathPending || agent.isStopped ||
                agent.pathStatus != NavMeshPathStatus.PathPartial)
                return;
            if (agent.remainingDistance > 1.5f || (navDestination - transform.position).sqrMagnitude < 4f)
                return;
            partialTimer = 2f;
            agent.SetDestination(navDestination);
        }

        void MoveTo(Vector3 point)
        {
            if (!agent.isOnNavMesh)
                return;
            stopped = false;
            agent.isStopped = false;
            if ((point - destination).sqrMagnitude < RepathDistance * RepathDistance)
                return;
            if (!NavMesh.SamplePosition(point, out var hit, 4f, Nav.Vehicles))
                return;
            destination = point;
            navDestination = hit.position;
            agent.SetDestination(hit.position);
        }

        // Едет, но почти не движется (упёрлась) — дольше StuckTime: другая позиция
        void CheckStuck()
        {
            bool moving = !stopped && agent.isOnNavMesh && !agent.isStopped && !agent.pathPending &&
                          agent.hasPath && agent.remainingDistance > 1.2f;
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
            if (hasSlot && Combat.IsAlive(slotTarget))
            {
                var old = slot;
                float eyeHeight = muzzle.position.y - transform.position.y;
                hasSlot = MatchManager.Instance.ClaimAttackSlot(this, slotTarget, weapon.Def, eyeHeight, SlotSpacing, true, old, out slot);
                if (!hasSlot)
                    slotTarget = null;
            }
        }

        // Земля под гусеницами может исчезнуть (воронка) — возвращаемся на навмеш
        void KeepOnNavMesh()
        {
            if (agent.isOnNavMesh)
                return;
            navCheckTimer -= Time.deltaTime;
            if (navCheckTimer > 0f)
                return;
            navCheckTimer = 0.5f;
            if (NavMesh.SamplePosition(transform.position, out var hit, 4f, Nav.Vehicles))
            {
                agent.Warp(hit.position);
                destination = new Vector3(float.MaxValue, 0f, 0f);
            }
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        // ---------- Бой ----------

        public Vector3 AimPoint(Vector3 from) => transform.position + Vector3.up * (Size.y * 0.5f);

        public Vector3 MissPoint(Vector3 from)
        {
            var offset = Random.insideUnitCircle.normalized * Random.Range(Size.z * 0.6f, Size.z * 0.6f + 1.2f);
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
            Faction.vehiclesLost++;
            agent.enabled = false;
            MatchManager.Instance.Effects.VehicleDestroyed(transform.position, transform.rotation, Size, Faction.team);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            Faction?.vehicles.Remove(this);
            ReleaseSlot();
        }
    }
}
