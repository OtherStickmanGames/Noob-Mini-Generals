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
        /// <summary>Урон в точке point от снаряда, летевшего в direction (здание там крошится)</summary>
        void TakeDamage(float amount, Vector3 point, Vector3 direction);
    }

    /// <summary>
    /// Цель-прямоугольник на земле: здание или участок стены. Бойцы встают вокруг неё на дальности
    /// стрельбы (MatchManager.ClaimAttackSlot).
    /// </summary>
    public interface IAreaTarget : IDamageable
    {
        /// <summary>Половина размера по x и z, м; центр — transform.position (на земле)</summary>
        Vector2 HalfExtents { get; }
        /// <summary>Высота над землёй, м</summary>
        float Height { get; }
        float DistanceTo(Vector3 point);
        Vector3 ClosestEdgePoint(Vector3 from, float margin);
        /// <summary>Точка на поверхности цели, ближайшая к from (туда приходится взрыв)</summary>
        Vector3 ClosestSurfacePoint(Vector3 from);
    }

    /// <summary>Общее для стрельбы: проверки линии огня, разброс, урон по площади</summary>
    public static class Combat
    {
        // ---------- Прямоугольник цели ----------

        /// <summary>Ближайшая к точке позиция на краю прямоугольника с отступом (по горизонтали)</summary>
        public static Vector3 RectEdgePoint(Vector3 center, Vector2 half, Vector3 from, float margin)
        {
            float hx = half.x + margin, hz = half.y + margin;
            var p = new Vector3(Mathf.Clamp(from.x, center.x - hx, center.x + hx), center.y,
                                Mathf.Clamp(from.z, center.z - hz, center.z + hz));

            // Точка внутри прямоугольника — выталкиваем на ближайшую сторону
            float dx = hx - Mathf.Abs(p.x - center.x), dz = hz - Mathf.Abs(p.z - center.z);
            if (dx > 0f && dz > 0f)
            {
                if (dx < dz)
                    p.x = center.x + Mathf.Sign(p.x - center.x + 0.0001f) * hx;
                else
                    p.z = center.z + Mathf.Sign(p.z - center.z + 0.0001f) * hz;
            }
            return p;
        }

        /// <summary>Расстояние по горизонтали от точки до прямоугольника</summary>
        public static float RectDistance(Vector3 center, Vector2 half, Vector3 point)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(point.x - center.x) - half.x);
            float dz = Mathf.Max(0f, Mathf.Abs(point.z - center.z) - half.y);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Куда целиться в цель-прямоугольник: край со стороны стрелка, на уровне груди</summary>
        public static Vector3 AreaAimPoint(IAreaTarget target, Vector3 from, float visibleHeight)
        {
            var edge = target.ClosestEdgePoint(from, -0.15f);
            edge.y = target.transform.position.y + Mathf.Min(visibleHeight * 0.5f, 1.2f);
            return edge;
        }

        /// <summary>Промах по цели-прямоугольнику — в землю у её края со стороны стрелка, чуть вбок</summary>
        public static Vector3 AreaMissPoint(IAreaTarget target, Vector3 from)
        {
            var edge = target.ClosestEdgePoint(from, Random.Range(0.3f, 1.2f));
            var side = Vector3.Cross(Vector3.up, (edge - from).normalized);
            edge += side * Random.Range(-1.2f, 1.2f);
            edge.y = target.transform.position.y - 0.1f;
            return edge;
        }

        /// <summary>Ближайшая к from точка на поверхности коробки цели</summary>
        public static Vector3 AreaSurfacePoint(IAreaTarget target, Vector3 from, float visibleHeight)
        {
            var p = target.ClosestEdgePoint(from, -0.05f);
            float bottom = target.transform.position.y;
            p.y = Mathf.Clamp(from.y, bottom + 0.1f, bottom + Mathf.Max(0.2f, visibleHeight - 0.1f));
            return p;
        }

        // ---------- Попадания ----------
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

        /// <summary>Во что попал луч: боец, здание или участок стены (стена — воксели арены)</summary>
        public static IDamageable DamageableAt(in RaycastHit hit)
        {
            var damageable = DamageableOf(hit.collider);
            if (damageable != null)
                return damageable;
            var match = MatchManager.Instance;
            if (hit.collider.transform.IsChildOf(match.Arena.transform))
                return match.WallSegmentAt(hit.point - hit.normal * 0.1f);
            return null;
        }

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
                var damageable = DamageableAt(hits[i]);
                if (damageable == target)
                    return true;
                // Свои бойцы и здания снаряд пролетает, свою стену — нет
                if (damageable != null && damageable.Faction == own && damageable is not WallSegment)
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

        /// <summary>Урон оружия по цели: по зданиям и стенам, по технике — с множителями оружия</summary>
        public static float DamageTo(WeaponDef def, IDamageable target, float damage) => target switch
        {
            IAreaTarget => damage * def.structureDamage,
            VehicleUnit => damage * def.vehicleDamage,
            _ => damage,
        };

        /// <summary>Объект цели не уничтожен (жива она или нет — неважно)</summary>
        public static bool Exists(IDamageable target) => target is Object o && o != null;

        // ---------- Вражеские юниты рядом ----------

        static readonly List<(float score, IDamageable unit)> nearbyUnits = new();

        /// <summary>
        /// Насколько охотнее оружие бьёт технику: противотанковое — машины «вдвое ближе», пули —
        /// «вдвое дальше» (стреляют по машине, только если пехоты рядом нет)
        /// </summary>
        public static float VehicleWeight(WeaponDef def) =>
            def.vehicleDamage >= 1f ? 0.5f : def.vehicleDamage < 0.5f ? 2f : 1f;

        /// <summary>
        /// Живые вражеские бойцы и машины не дальше radius по горизонтали от point (и не ближе minRange),
        /// по возрастанию расстояния, умноженного у машин на vehicleWeight. Общий список — действует до
        /// следующего вызова.
        /// </summary>
        public static List<(float score, IDamageable unit)> EnemyUnitsNear(Faction enemy, Vector3 point, float radius,
                                                                            float minRange, float vehicleWeight)
        {
            nearbyUnits.Clear();
            foreach (var u in enemy.units)
                AddNearby(u, point, radius, minRange, 1f);
            foreach (var v in enemy.vehicles)
                AddNearby(v, point, radius, minRange, vehicleWeight);
            nearbyUnits.Sort((a, b) => a.score.CompareTo(b.score));
            return nearbyUnits;
        }

        static void AddNearby(IDamageable unit, Vector3 point, float radius, float minRange, float weight)
        {
            if (!IsAlive(unit))
                return;
            var delta = unit.transform.position - point;
            delta.y = 0f;
            float d = delta.magnitude;
            if (d <= radius && d >= minRange)
                nearbyUnits.Add((d * weight, unit));
        }

        /// <summary>Урон по площади врагам owner: в центре полный, к краю — 40%</summary>
        public static void Splash(Vector3 center, WeaponDef def, Faction owner, IDamageable skip)
        {
            float radius = def.splashRadius;
            var enemy = MatchManager.Instance.GetFaction(1 - owner.team);
            splashTargets.Clear();
            foreach (var u in enemy.units)
                if (IsAlive(u) && (IDamageable)u != skip)
                    splashTargets.Add(u);
            foreach (var v in enemy.vehicles)
                if (IsAlive(v) && (IDamageable)v != skip)
                    splashTargets.Add(v);
            foreach (var s in enemy.structures)
                if (IsAlive(s) && (IDamageable)s != skip)
                    splashTargets.Add(s);
            foreach (var w in enemy.walls)
                if (IsAlive(w) && (IDamageable)w != skip)
                    splashTargets.Add(w);

            foreach (var t in splashTargets)
            {
                var area = t as IAreaTarget;
                float d = area != null ? area.DistanceTo(center) : Vector3.Distance(t.transform.position, center);
                if (d >= radius)
                    continue;
                // Здание и стена крошатся с ближайшей к взрыву стороны
                var point = area != null ? area.ClosestSurfacePoint(center) : t.transform.position;
                // Урон может убить цель — проверяем, жива ли она ещё (здание могло уже рухнуть)
                if (IsAlive(t))
                    t.TakeDamage(DamageTo(def, t, def.damage) * Mathf.Lerp(1f, 0.4f, d / radius), point, point - center);
            }
            splashTargets.Clear();
        }
    }
}
