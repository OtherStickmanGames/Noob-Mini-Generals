using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using System.Linq;

public class Unit : MonoBehaviour
{
    [SerializeField] UnitType unitType;
    [SerializeField] TrooperType trooperType;
    [SerializeField] float damage = 1f;
    [SerializeField] float distanceToAttack = 5f;
    [SerializeField] float speedRot = 1f;
    [SerializeField] float attackRate = 1;
    [SerializeField] float resourceExtractionRate = 1f;
    public Projectile projectilePrefab;

    public Team Team => team;
    public UnitType UnitType => unitType;
    public TrooperType TrooperType => trooperType;
    public HealthComponent Health => health;
    [field:SerializeField]
    public UnitMode UnitMode { get; set; }


    HealthComponent health;
    NavMeshAgent navMeshAgent;
    Player general;
    Building pitBuilding;
    Pit pit;
    Unit target;
    Building targetBuilding;
    Unit destinationTarget;

    [SerializeField] Team team;

    float currentExtractionRate;
    float fireRateStepMultiplier;
    float findTargetRate;
    float currentRate;
    float lifetime;

    bool moveToDefTarget;

    public void Init(Team team, Player general)
    {
        this.team = team;
        this.general = general;

        fireRateStepMultiplier = 1f;
        findTargetRate = 8;
        gameObject.name = gameObject.name.Insert(0, $"{team} ");

        navMeshAgent = GetComponent<NavMeshAgent>();
        health = GetComponent<HealthComponent>();

        health.valueChanged += Health_Changed;

        SetDestination(transform.position + transform.forward);
        LeanTween.delayedCall(1f, () => 
        { 
            if (navMeshAgent) navMeshAgent.isStopped = true; 
        });

        CheckAvailableCards();
    }

    private void Health_Changed(HealthComponent health)
    {
        if (health.Value <= 0)
        {
            if(UnitType == UnitType.Worker)
            {
                pitBuilding.Workers--;
            }
            general.allUnits.Remove(this);
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        lifetime += Time.deltaTime;
        currentRate += Time.deltaTime * fireRateStepMultiplier;
        currentRate += Random.Range(0f, 0.03f);

        if (lifetime < 1.5f)
            return;

        if (unitType == UnitType.Worker)
        {
            if (!pitBuilding)
            {
                FindPit();
            }
            else
            {
                MoveToPit();
            }
        }
        else
        {
            //=========================================
            if (UnitMode == UnitMode.Def)
            {
                navMeshAgent.isStopped = true;

                CheckNearestEnemy();
                AttackUnit();

                if (!target)
                {
                    CheckNearestEnemyBuilds();
                    AttackBuilding();
                }
            }
            //=========================================
            if (UnitMode == UnitMode.AttackAnyUnits)
            {
                navMeshAgent.isStopped = false;

                FindNearestTarget();
                MoveToTarget();
                AttackUnit();
            }
            //=========================================
            if (UnitMode == UnitMode.AttackAnyBuilds)
            {
                navMeshAgent.isStopped = false;

                FindNearestTargetBuilding();
                MoveToTargetBuilding();
                AttackBuilding();
            }
            //=========================================
            if (UnitMode == UnitMode.AttackAnyTarget)
            {
                navMeshAgent.isStopped = false;

                FindNearestTarget();
                MoveToTarget();
                AttackUnit();

                if (!target)
                {
                    FindNearestTargetBuilding();
                    MoveToTargetBuilding();
                    AttackBuilding();
                }
            }
            //=========================================
            if (UnitMode == UnitMode.DefPit)
            {
                Vector3 pos = default;
                if (!moveToDefTarget)
                {
                    moveToDefTarget = true;
        
                    pos = GetDefPitPosition();
                    pos.y = transform.position.y;
                    SetDestination(pos);
                }

                var dist = Vector3.Distance(pos, transform.position);
                if (dist < 0.3f)
                {
                    SetDestination(transform.position);
                    UnitMode = UnitMode.Def;
                }
            }
        }





        if (Input.GetKeyDown(KeyCode.B))
        {
            UnitMode = UnitMode.DefPit;
            //GetDefPitPosition();
        }
    }

    
    Vector3 GetDefPitPosition()
    {
        var pits = general.Buildings.FindAll(b => b.BuildingType == BuildingType.Pit);
        var randomPit = pits[Random.Range(0, pits.Count)];
        var pos = randomPit.transform.position + (randomPit.transform.forward * 3) + (randomPit.transform.right * Random.Range(-1.8f, 1.8f)) + Vector3.up;

        return pos;
    }

    void MoveToTarget()
    {
        if (!target)
            return;

        var distance = Vector3.Distance(transform.position, target.transform.position);
        if (distance > distanceToAttack)
        {
            SetDestination(target.transform.position);
        }
        else
        {
            SetDestination(transform.position);
            
        }
    }

    void MoveToTargetBuilding()
    {
        if (!targetBuilding)
            return;

        var distance = Vector3.Distance(transform.position, targetBuilding.transform.position);
        if (distance > distanceToAttack)
        {
            SetDestination(targetBuilding.transform.position);
        }
        else
        {
            SetDestination(transform.position);
        }
    }

    public void AttackUnit()
    {
        if (!target)
            return;

        var distance = Vector3.Distance(transform.position, target.transform.position);
        if (distance <= distanceToAttack)
        {
            var direction = target.transform.position - transform.position;
            transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * speedRot);

            if (currentRate > attackRate)
            {
                var rot = Quaternion.LookRotation(target.transform.position - transform.position);
                var pos = transform.position + Vector3.up;
                var projectile = Instantiate(projectilePrefab, pos, rot);
                projectile.Init(team, target.health, damage);

                currentRate = 0;
            }
        }
    }

    private void AttackBuilding()
    {
        if (!targetBuilding)
            return;

        var distance = Vector3.Distance(transform.position, targetBuilding.transform.position);
        if (distance <= distanceToAttack)
        {
            var direction = targetBuilding.transform.position - transform.position;
            transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * speedRot);

            if (currentRate > attackRate)
            {
                var rot = Quaternion.LookRotation(targetBuilding.transform.position - transform.position);
                var pos = transform.position + Vector3.up;
                var projectile = Instantiate(projectilePrefab, pos, rot);
                projectile.Init(team, targetBuilding.Health, damage);

                currentRate = 0;
            }
        }
    }

    public void SetDestination(Vector3 pos)
    {
        //print($"{gameObject}#{pos} =-=-=- {target}");
        navMeshAgent.SetDestination(pos);
        navMeshAgent.isStopped = false;
    }

    void FindNearestTarget()
    {
        findTargetRate += Time.deltaTime;

        if (target && findTargetRate < 1.5f)
            return;

        target = null;

        var units = general.Enemy.allUnits;
        float minDist = float.MaxValue;
        foreach (var enemy in units)
        {
            var dist = Vector3.Distance(enemy.transform.position, transform.position);
            if(dist < minDist)
            {
                target = enemy;
                minDist = dist;
            }
        }


        findTargetRate = 0;
    }

    void FindNearestTargetBuilding()
    {
        findTargetRate += Time.deltaTime;

        if (targetBuilding && findTargetRate < 1.5f)
            return;

        var builds = general.Enemy.Buildings.FindAll(b => b.Health);
        float minDist = float.MaxValue;
        foreach (var enemy in builds)
        {
            var dist = Vector3.Distance(enemy.transform.position, transform.position);
            if (dist < minDist)
            {
                targetBuilding = enemy;
                minDist = dist;
            }
        }

        findTargetRate = 0;
    }

    void CheckNearestEnemy()
    {
        var units = general.Enemy.allUnits;
        foreach (var enemy in units)
        {
            var dist = Vector3.Distance(enemy.transform.position, transform.position);
            if (dist < distanceToAttack)
            {
                target = enemy;
                
                return;
            }
        }

        target = null;
    }

    void CheckNearestEnemyBuilds()
    {
        var builds = general.Enemy.Buildings.FindAll(b => b.Health);
        foreach (var enemy in builds)
        {
            var dist = Vector3.Distance(enemy.transform.position, transform.position);
            if (dist < distanceToAttack)
            {
                targetBuilding = enemy;

                return;
            }
        }
    }


    // ============ Worker =============
    void FindPit()
    {
        var pits = general.Buildings.FindAll(b => b.BuildingType == BuildingType.Pit);
        var minWorkers = 8888888;

        foreach (var item in pits)
        {
            if (item.Workers == 0)
            {
                pitBuilding = item;
                pit = pitBuilding.GetComponent<Pit>();
                pitBuilding.Workers++;
                return;
            }

            if(item.Workers < minWorkers)
            {
                minWorkers = item.Workers;
                pitBuilding = item;
            }
        }

        pit = pitBuilding.GetComponent<Pit>();
        pitBuilding.Workers++;
    }

    void MoveToPit()
    {
        var pos = pitBuilding.transform.position - (pitBuilding.transform.forward * 0.58f);
        var distance = Vector3.Distance(pos, transform.position);
        if(distance > 1.5f)
        {
            SetDestination(pos);
        }
        else
        {
            navMeshAgent.isStopped = true;
            currentExtractionRate += Time.deltaTime;
            if(currentExtractionRate > resourceExtractionRate)
            {
                if (pit.PitType == PitType.Gold)
                {
                    general.CountGold += 5;
                }
                else
                {
                    general.CountIron += 1;    
                }

                currentExtractionRate = 0;
            }
        }
    }
    //=========================================================

    void CheckAvailableCards()
    {
        var allFireRate = general.usedCards.Find(c => c.cardType == CardType.AllFareRate);

        if (allFireRate != null)
        {
            var value = (float)(allFireRate.values[0] + 100) / 100f;
            fireRateStepMultiplier = value;
            print(value + " fenfsenfiosenfu");
        }

        var increaseHealth = general.usedCards.Find(c => c.cardType == CardType.TroopersIncreaseHealth);

        if (UnitType == UnitType.Trooper && increaseHealth != null)
        {
            health.Init(health.MaxValue + increaseHealth.values[0]);
        }
    }
}

public enum Team
{
    One,
    Two
}

public enum UnitMode
{
    Def,
    AttackUnits,
    AttackBuilds,
    AttackAnyUnits,
    AttackAnyBuilds,
    AttackAnyTarget,
    DefPit,
    DefMain,
}

public enum UnitType
{
    Worker,
    Trooper,
}

public enum TrooperType
{
    None,
    Simple,
    Middle,
    LightTank,
    MiddleTank,
    HeavyTrooper,
}
