using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Generals
{
    /// <summary>
    /// Пехотинец — боец отряда (Squad). Решения принимает отряд, боец их выполняет и стреляет
    /// свободно: есть враг в дальности и на линии огня — встаёт и стреляет по ближайшему; иначе —
    /// приказ отряда: стоять в строю, идти строем на врага или бить постройку со своей позиции
    /// вокруг неё (MatchManager.ClaimAttackSlot — чтобы не толпиться, например, в воротах).
    /// </summary>
    public class InfantryUnit : MonoBehaviour, IDamageable
    {
        const float ThinkInterval = 0.25f;
        const float RepathDistance = 0.75f;
        // Позицию вокруг постройки ищем, когда до неё не дальше дальности оружия + столько метров
        const float SlotSearchDistance = 10f;
        public const float ChestHeight = 1.05f;
        /// <summary>Сколько секунд почти без движения по пути считается «застрял»</summary>
        const float StuckTime = 1.5f;
        /// <summary>Стреляет, только довернувшись к цели точнее этого, градусы</summary>
        const float FireAngle = 20f;

        public Faction Faction { get; private set; }
        public Squad Squad { get; private set; }
        public float Health { get; private set; }
        public float MaxHealth => UnitCatalog.InfantryHealth;
        public bool IsAlive => Health > 0f;
        public float LastDamageTime { get; private set; } = float.NegativeInfinity;
        public Vector3 Velocity => agent != null && agent.enabled ? agent.velocity : Vector3.zero;

        public BarracksBehavior Behavior => Squad.Behavior;
        /// <summary>Оружие бойца: винтовка или спецоружие отряда</summary>
        public SquadWeapon WeaponType { get; private set; }

        /// <summary>По кому стреляет: вражеский боец или постройка; null — ни по кому</summary>
        public IDamageable Target { get; private set; }
        /// <summary>Стоит и стреляет</summary>
        public bool Firing { get; private set; }

        NavMeshAgent agent;
        Weapon weapon;
        float thinkTimer;
        float navCheckTimer;
        float stuckTime;
        Vector3 destination = new(float.MaxValue, 0f, 0f);

        // Своя позиция вокруг постройки, которую бьёт отряд
        IAreaTarget slotTarget;
        bool hasSlot;
        Vector3 slot;
        int blockedChecks;

        static readonly List<(float distance, InfantryUnit unit)> nearby = new();

        public void Init(Faction faction, Squad squad, Material material, SquadWeapon weaponType)
        {
            Faction = faction;
            Squad = squad;
            WeaponType = weaponType;
            Health = UnitCatalog.InfantryHealth;
            weapon = new Weapon(WeaponCatalog.Def(weaponType));
            name = $"{UnitCatalog.InfantryName} {faction.team} ({WeaponCatalog.Def(weaponType).name})";
            // Гранатомёт — с плеча, снайперка — выше груди
            muzzleOffset = weaponType switch
            {
                SquadWeapon.GrenadeLauncher => new Vector3(0.4f, 1.62f, 0.9f),
                SquadWeapon.Sniper => new Vector3(0.15f, 1.35f, 0.95f),
                _ => new Vector3(0.15f, ChestHeight, 0.6f),
            };

            var mesh = VoxelModels.Infantry(faction.team, weaponType);
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
            // Разный приоритет, чтобы толпа расходилась, а не упиралась друг в друга
            agent.avoidancePriority = Random.Range(40, 60);

            // Коллайдер — чтобы в бойца попадали снаряды; триггер, поэтому тапом он не выбирается
            var capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 1f, 0f);
            capsule.height = 2f;
            capsule.radius = 0.35f;
            capsule.isTrigger = true;

            thinkTimer = Random.value * ThinkInterval;
        }

        /// <summary>Отряд сменил поведение — сразу пересмотреть, что делать</summary>
        public void OrderChanged()
        {
            thinkTimer = 0f;
            Target = null;
            Firing = false;
            ReleaseSlot();
        }

        public void ReleaseSlot()
        {
            if (hasSlot && MatchManager.Instance != null)
                MatchManager.Instance.ReleaseAttackSlot(this, slotTarget);
            hasSlot = false;
            slotTarget = null;
            blockedChecks = 0;
        }

        Vector3 Muzzle => transform.position + transform.rotation * muzzleOffset;
        Vector3 muzzleOffset;

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

            // Стоит — поворачивается к цели и стреляет. К постройке — лицом к её ближнему краю
            // (точка прицела у неё каждый выстрел своя)
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
            // Свободная стрельба: враг в дальности и на линии огня — важнее приказа отряда
            var enemyUnit = EnemyInRange();
            if (enemyUnit != null)
            {
                StandAndFire(enemyUnit);
                return;
            }

            // Турель бьёт по отряду и достаёт до неё — стреляем сразу, не идя на позицию
            var turret = TurretInRange();
            if (turret != null)
            {
                StandAndFire(turret);
                return;
            }

            switch (Squad.Order)
            {
                case SquadOrder.AttackArea when Combat.IsAlive(Squad.AreaTarget):
                    EngageArea(Squad.AreaTarget);
                    break;

                case SquadOrder.EngageUnits when weapon.Def.range < UnitCatalog.InfantryRange && Combat.IsAlive(Squad.Threat):
                    // Короткое оружие (огнемёт) из строя на подходе не достаёт — идёт прямо на врага
                    ReleaseSlot();
                    Target = null;
                    Firing = false;
                    MoveTo(Squad.Threat.transform.position);
                    break;

                default:
                    // Строй: у поста, на месте или на подходе к врагу
                    ReleaseSlot();
                    Target = null;
                    Firing = false;
                    MoveTo(Squad.FormationPoint(this));
                    break;
            }
        }

        // Ближайший живой враг в дальности стрельбы и на линии огня; текущая цель — если ещё годится
        InfantryUnit EnemyInRange()
        {
            float range = weapon.Def.range * 0.9f;
            if (Target is InfantryUnit current && Combat.IsAlive(current) &&
                FlatDistance(current.transform.position) <= range && Combat.HasLineOfFire(Muzzle, current, Faction))
                return current;

            var enemy = MatchManager.Instance.GetFaction(1 - Faction.team);
            nearby.Clear();
            foreach (var u in enemy.units)
            {
                if (!Combat.IsAlive(u))
                    continue;
                float d = FlatDistance(u.transform.position);
                if (d <= range)
                    nearby.Add((d, u));
            }
            nearby.Sort((a, b) => a.distance.CompareTo(b.distance));

            // Линию огня проверяем только у нескольких ближайших
            for (int i = 0; i < nearby.Count && i < 3; i++)
                if (Combat.HasLineOfFire(Muzzle, nearby[i].unit, Faction))
                    return nearby[i].unit;
            return null;
        }

        // Турель, которая стреляет по нашему отряду, в дальности и на линии огня
        Structure TurretInRange()
        {
            var enemy = MatchManager.Instance.GetFaction(1 - Faction.team);
            var turret = Squad.TurretFiringAt(enemy, transform.position);
            if (turret == null || turret.DistanceTo(transform.position) > weapon.Def.range * 0.9f ||
                !Combat.HasLineOfFire(Muzzle, turret, Faction))
                return null;
            return turret;
        }

        void StandAndFire(IDamageable target)
        {
            Target = target;
            Firing = true;
            if (agent.isOnNavMesh)
                agent.isStopped = true;
        }

        // Постройку бьют со своей позиции вокруг неё: отряд встаёт кучно (каждый берёт ближайшую
        // к себе свободную), а не толпой в первой точке, откуда достаёт
        void EngageArea(IAreaTarget target)
        {
            var match = MatchManager.Instance;
            if (slotTarget != target)
                ReleaseSlot();

            if (!hasSlot)
            {
                // Позицию ищем уже вблизи цели: там пути по NavMesh короткие. Издалека — просто идём
                // к цели (путь агента считается по кадрам), и так же ждём, если бюджет кадра исчерпан
                if (target.DistanceTo(transform.position) > weapon.Def.range + SlotSearchDistance ||
                    !match.CanClaimSlotThisFrame())
                {
                    Target = null;
                    Firing = false;
                    MoveTo(target.ClosestEdgePoint(transform.position, weapon.Def.range * 0.6f));
                    return;
                }

                hasSlot = match.ClaimAttackSlot(this, target, weapon.Def.range, null, out slot);
                if (!hasSlot)
                {
                    // Вокруг цели места нет (всё занято или не видно) — отряд берёт другую
                    Squad.RetargetArea(this, target);
                    Target = null;
                    Firing = false;
                    MoveTo(target.ClosestEdgePoint(transform.position, 1f));
                    return;
                }
                slotTarget = target;
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
                    StandAndFire(target);
                    return;
                }
                ReleaseSlot();
            }

            Target = null;
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
            if (hasSlot && Combat.IsAlive(slotTarget))
            {
                var old = slot;
                hasSlot = MatchManager.Instance.ClaimAttackSlot(this, slotTarget, weapon.Def.range, old, out slot);
                if (!hasSlot)
                    slotTarget = null;
            }
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

        float FlatDistance(Vector3 point) => Vector3.Distance(Flat(point), Flat(transform.position));

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
            ReleaseSlot();
        }
    }
}
