using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Generals
{
    /// <summary>То, во что можно стрелять: бойцы и здания</summary>
    public interface IDamageable
    {
        Faction Faction { get; }
        bool IsAlive { get; }
        float Health { get; }
        float MaxHealth { get; }
        /// <summary>Time.time последнего урона; -бесконечность — урона не было</summary>
        float LastDamageTime { get; }
        /// <summary>Скорость — для упреждения</summary>
        Vector3 Velocity { get; }
        Transform transform { get; }

        /// <summary>Куда целиться стрелку из точки from</summary>
        Vector3 AimPoint(Vector3 from);
        /// <summary>Точка на земле рядом с целью, куда уходит промах</summary>
        Vector3 MissPoint(Vector3 from);
        void TakeDamage(float amount);
    }

    /// <summary>Общее для стрельбы: проверки линии огня, разброс, урон по площади</summary>
    public static class Combat
    {
        static readonly RaycastHit[] hits = new RaycastHit[32];
        static readonly List<IDamageable> splashTargets = new();

        class HitDistanceComparer : IComparer<RaycastHit>
        {
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
        static readonly HitDistanceComparer byDistance = new();

        /// <summary>Цель есть, её объект не уничтожен и она жива</summary>
        public static bool IsAlive(IDamageable target) =>
            target is Object o && o != null && target.IsAlive;

        public static IDamageable DamageableOf(Collider collider) =>
            collider.GetComponentInParent<IDamageable>();

        /// <summary>
        /// Все попадания луча, по возрасту расстояния. Триггеры тоже (бойцы — триггеры).
        /// Результат — в общем буфере Hits, действует до следующего вызова.
        /// </summary>
        public static int SortedRaycast(Vector3 origin, Vector3 direction, float distance)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, hits, distance,
                                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            Array.Sort(hits, 0, count, byDistance);
            return count;
        }

        public static RaycastHit[] Hits => hits;

        /// <summary>
        /// Есть ли линия огня от from до цели: свои бойцы и здания не мешают (снаряд через них
        /// проходит), рельеф, стены и чужие постройки на пути — мешают.
        /// </summary>
        public static bool HasLineOfFire(Vector3 from, IDamageable target, Faction own)
        {
            var to = target.AimPoint(from);
            var delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f)
                return true;

            int count = SortedRaycast(from, delta / distance, distance);
            for (int i = 0; i < count; i++)
            {
                var damageable = DamageableOf(hits[i].collider);
                if (damageable == target)
                    return true;
                if (damageable != null && damageable.Faction == own)
                    continue;
                // Прочие триггеры (не бойцы) лучу не мешают
                if (damageable == null && hits[i].collider.isTrigger)
                    continue;
                return false;
            }
            return true;
        }

        /// <summary>Случайное направление в конусе вокруг direction</summary>
        public static Vector3 RandomInCone(Vector3 direction, float angle)
        {
            if (angle <= 0f)
                return direction;
            var axis = Vector3.Cross(direction, Random.onUnitSphere);
            if (axis.sqrMagnitude < 1e-6f)
                return direction;
            // Ближе к центру конуса чаще, чем к краю
            float a = angle * Mathf.Sqrt(Random.value);
            return Quaternion.AngleAxis(a, axis.normalized) * direction;
        }

        /// <summary>Урон по площади врагам owner: в центре полный, к краю — 40%</summary>
        public static void Splash(Vector3 center, float radius, float damage, Faction owner, IDamageable skip)
        {
            var enemy = MatchManager.Instance.GetFaction(1 - owner.team);
            splashTargets.Clear();
            foreach (var u in enemy.units)
                if (IsAlive(u) && (IDamageable)u != skip)
                    splashTargets.Add(u);
            foreach (var s in enemy.structures)
                if (IsAlive(s) && (IDamageable)s != skip)
                    splashTargets.Add(s);

            foreach (var t in splashTargets)
            {
                float d = t is Structure s ? s.DistanceTo(center) : Vector3.Distance(t.transform.position, center);
                if (d >= radius)
                    continue;
                // Урон может убить цель — проверяем, жива ли она ещё (здание могло уже рухнуть)
                if (IsAlive(t))
                    t.TakeDamage(damage * Mathf.Lerp(1f, 0.4f, d / radius));
            }
            splashTargets.Clear();
        }
    }
}
