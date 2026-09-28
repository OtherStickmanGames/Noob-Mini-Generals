using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Turrel : MonoBehaviour
{
    [SerializeField] float distanceToAttack = 8;
    [SerializeField] float fireRate = 0.3f;
    [SerializeField] float speedRot = 1f;
    [SerializeField] float damage = 1f;

    [SerializeField] Transform gun;
    [SerializeField] Projectile projectilePrefab;
    [SerializeField] Transform gunPoint_1;
    [SerializeField] Transform gunPoint_2;
    [SerializeField] Building building;
    

    float currentRate;
    float findTargetRate;
    float distanceToTarget;
    HealthComponent target;
    Transform lastGun;

    private void Start()
    {
        var usedCards = building.General.usedCards;
        var distanceCard = usedCards.Find(c => c.cardType == CardType.TurrelsAttackDistance);
        if(distanceCard != null)
        {
            distanceToAttack += distanceCard.values[0];
        }
    }

    private void Update()
    {
        FindTarget();

        if (target)
        {
            Attack();
        }
        else
        {

        }
    }

    void FindTarget()
    {
        findTargetRate += Time.deltaTime;

        if (findTargetRate < 1f)
            return;

        findTargetRate = 0;

        var unit = GameUtils.GetNearestEnemyAnyUnit(building.General, transform, distanceToAttack + 1);

        if (unit)
        {
            target = unit.Health;
        }
    }

    void Attack()
    {
        currentRate += Time.deltaTime;

        var direction = target.transform.position - gun.position;
        var rotation = Quaternion.LookRotation(direction);
        gun.rotation = Quaternion.Lerp(gun.rotation, rotation, Time.deltaTime * speedRot);

        var dist = Vector3.Distance(target.transform.position, transform.position);
        if (dist > distanceToAttack)
            return;
        
        if (fireRate < currentRate)
        {
            currentRate = 0;

            if(lastGun != gunPoint_1)
            {
                lastGun = gunPoint_1;
            }
            else
            {
                lastGun = gunPoint_2;
            }

            var projectile = Instantiate(projectilePrefab, lastGun.position, rotation);
            projectile.Init(building.General.Team, target, damage, 1.8f);
        }
    }
}
