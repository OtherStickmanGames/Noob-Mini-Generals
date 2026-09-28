using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Projectile : MonoBehaviour
{
    [SerializeField] float speed = 10;
    public Team Team;
    HealthComponent target;

    float distToDamage;
    float lifetime;
    float damage;
    Vector3 prevPos;

    public void Init(Team team, HealthComponent target, float damage, float distToDamage = 1.3f)
    {
        Team = team;
        this.target = target;
        this.damage = damage;
        this.distToDamage = distToDamage;
        prevPos = transform.position;
    }

    private void Update()
    {
        transform.position += speed * Time.deltaTime * transform.forward;
        lifetime += Time.deltaTime;
        //var dir = transform.position - prevPos;
        //var hits = Physics.RaycastAll(transform.position, dir);
        //foreach (var item in hits)
        //{
        //    print(item);
        //}

        if (!target)
        {
            Destroy(gameObject);
            return;
        }

        var distance = Vector3.Distance(transform.position, target.transform.position);
        //print(distance);
        if (distance < distToDamage)
        {
            Destroy(gameObject);
            target.Value -= damage;
        }
        if (lifetime > 8)
        {
            Destroy(gameObject);
        }
    }
}
