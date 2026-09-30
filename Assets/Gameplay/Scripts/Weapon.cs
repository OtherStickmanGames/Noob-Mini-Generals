using UnityEngine;

namespace Generals
{
    /// <summary>
    /// Оружие бойца или турели: перезарядка и выстрел. Попадание решается броском точности:
    /// в цель — с упреждением по её скорости, промах — в землю рядом с целью (там остаётся воронка).
    /// Снаряд летит по-настоящему, поэтому и «попадание» может не долететь (цель ушла, встал камень).
    /// </summary>
    public class Weapon
    {
        public readonly WeaponDef Def;
        float cooldown;

        public Weapon(WeaponDef def)
        {
            Def = def;
            // Бойцы одного отряда не стреляют залпом
            cooldown = Random.Range(0f, def.interval);
        }

        public bool Ready => cooldown <= 0f;

        public void Tick(float deltaTime)
        {
            if (cooldown > 0f)
                cooldown -= deltaTime;
        }

        public void Fire(Faction owner, Vector3 muzzle, IDamageable target)
        {
            Vector3 aim;
            if (Random.value < Def.accuracy)
            {
                aim = target.AimPoint(muzzle);
                float flightTime = Vector3.Distance(muzzle, aim) / Def.projectileSpeed;
                aim += target.Velocity * flightTime;
            }
            else
            {
                aim = target.MissPoint(muzzle);
            }

            var direction = Combat.RandomInCone((aim - muzzle).normalized, Def.spread);
            MatchManager.Instance.Projectiles.Fire(Def, owner, muzzle, direction, Def.range * 1.5f + 4f);
            MatchManager.Instance.Effects.MuzzleFlash(muzzle, direction, Def.kind == ProjectileKind.Shell ? 1.8f : 1f);
            cooldown = Def.interval * Random.Range(0.9f, 1.1f);
        }
    }
}
