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
            if (Def.indirect)
            {
                FireArc(owner, muzzle, target);
                return;
            }

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
            // У огнемёта вспышки нет — видна сама струя
            if (Def.kind != ProjectileKind.Flame)
                MatchManager.Instance.Effects.MuzzleFlash(muzzle, direction, Def.kind == ProjectileKind.Shell ? 1.8f : 1f);
            cooldown = Def.interval * Random.Range(0.9f, 1.1f);
        }

        /// <summary>
        /// Навесной выстрел: снаряд по дуге падает в точку у цели — здание сверху (чуть внутрь от
        /// ближнего края), машину или бойца с упреждением. Чем дальше, тем выше дуга: через стены.
        /// </summary>
        void FireArc(Faction owner, Vector3 muzzle, IDamageable target)
        {
            Vector3 land;
            if (Random.value < Def.accuracy)
            {
                if (target is IAreaTarget area)
                {
                    land = area.ClosestEdgePoint(muzzle, -0.6f);
                    land.y = target.transform.position.y;
                }
                else
                {
                    land = target.transform.position;
                }
            }
            else
            {
                land = target.MissPoint(muzzle);
            }

            var delta = land - muzzle;
            var flat = new Vector3(delta.x, 0f, delta.z);
            float distance = flat.magnitude;
            float flightTime = ArcBaseTime + distance / Def.projectileSpeed;
            // Упреждение по скорости цели и разброс, растущий с расстоянием
            land += target.Velocity * flightTime;
            var scatter = Random.insideUnitCircle * (distance * Mathf.Tan(Def.spread * Mathf.Deg2Rad));
            land += new Vector3(scatter.x, 0f, scatter.y);
            delta = land - muzzle;

            var velocity = delta / flightTime + Vector3.up * (0.5f * Projectiles.Gravity * flightTime);
            MatchManager.Instance.Projectiles.FireArc(Def, owner, muzzle, velocity);
            MatchManager.Instance.Effects.MuzzleFlash(muzzle, velocity.normalized, 2.2f);
            cooldown = Def.interval * Random.Range(0.9f, 1.1f);
        }

        // Добавка к времени полёта навесного снаряда, с: даже вблизи дуга поднимается выше стены
        const float ArcBaseTime = 1.2f;
    }
}
