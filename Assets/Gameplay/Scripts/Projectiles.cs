using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Generals
{
    /// <summary>
    /// Все летящие снаряды боя. Снаряд — просто данные (без GameObject): каждый кадр луч на длину
    /// шага; рисуются все одним вызовом на вид снаряда (Graphics.RenderMeshInstanced).
    /// Свои бойцы и здания снаряд пролетает насквозь. Попадание в землю или стену — воронка.
    /// </summary>
    public class Projectiles : MonoBehaviour
    {
        struct Shot
        {
            public WeaponDef def;
            public Faction owner;
            public Vector3 position;
            public Vector3 direction;
            public float traveled;
            public float maxDistance;
            public float trailTimer;
            // Навесной снаряд: летит по дуге со скоростью velocity под действием тяжести
            public bool ballistic;
            public Vector3 velocity;
        }

        // Вид трассера пули и снаряда турели
        const float BulletWidth = 0.07f;
        const float BulletLength = 0.9f;
        const float ShellSize = 0.2f;
        const float ShellLength = 0.36f;
        const float ShellTrailInterval = 0.03f;
        const int BatchSize = 1023;
        // Воронка — не глубже вокселя от исходной земли: агент перешагивает уступ в один воксель, а
        // поле глубоких воронок отрезало базу от выхода (автотест: армия не выходила из ворот)
        const int CraterDepth = 1;

        static readonly Color BulletColor = new(1f, 0.86f, 0.45f, 1f);
        static readonly Color ShellColor = new(1f, 0.55f, 0.2f, 1f);

        VoxelArena arena;
        Effects effects;
        readonly List<Shot> shots = new();
        readonly Matrix4x4[] bulletMatrices = new Matrix4x4[BatchSize];
        readonly Matrix4x4[] shellMatrices = new Matrix4x4[BatchSize];
        Material bulletMaterial;
        Material shellMaterial;

        public int Count => shots.Count;

        public void Init(VoxelArena arena, Effects effects)
        {
            this.arena = arena;
            this.effects = effects;
            bulletMaterial = Effects.CreateMaterial("Bullet", Effects.Mode.Emissive, BulletColor, false);
            shellMaterial = Effects.CreateMaterial("Shell", Effects.Mode.Emissive, ShellColor, false);
        }

        void OnDestroy()
        {
            Destroy(bulletMaterial);
            Destroy(shellMaterial);
        }

        public void Clear() => shots.Clear();

        public void Fire(WeaponDef def, Faction owner, Vector3 from, Vector3 direction, float maxDistance)
        {
            shots.Add(new Shot
            {
                def = def,
                owner = owner,
                position = from,
                direction = direction.normalized,
                maxDistance = maxDistance,
            });
        }

        /// <summary>Ускорение падения навесного снаряда, м/с²</summary>
        public const float Gravity = 9.81f;

        /// <summary>Навесной снаряд: стартует со скоростью velocity и летит по дуге</summary>
        public void FireArc(WeaponDef def, Faction owner, Vector3 from, Vector3 velocity)
        {
            shots.Add(new Shot
            {
                def = def,
                owner = owner,
                position = from,
                direction = velocity.normalized,
                ballistic = true,
                velocity = velocity,
                // Дуга длиннее дальности; ограничение — только на случай промаха мимо карты
                maxDistance = def.range * 4f + 20f,
            });
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var shot = shots[i];
                if (shot.ballistic)
                {
                    shot.velocity += Vector3.down * (Gravity * dt);
                    shot.direction = shot.velocity.normalized;
                }
                float step = (shot.ballistic ? shot.velocity.magnitude : shot.def.projectileSpeed) * dt;
                if (TryHit(shot, step))
                {
                    RemoveAt(i);
                    continue;
                }

                shot.position += shot.direction * step;
                shot.traveled += step;

                if (shot.def.kind == ProjectileKind.Shell)
                {
                    shot.trailTimer -= dt;
                    if (shot.trailTimer <= 0f)
                    {
                        shot.trailTimer = ShellTrailInterval;
                        effects.Trail(shot.position - shot.direction * ShellLength);
                    }
                }
                else if (shot.def.kind == ProjectileKind.Flame)
                {
                    // Струя огня видна частицами (сам «снаряд» не рисуется)
                    effects.FlameTrail(shot.position, shot.direction, shot.traveled / shot.maxDistance);
                }

                if (shot.traveled >= shot.maxDistance)
                {
                    // Снаряд на излёте рвётся в воздухе, огонь гаснет, пуля просто теряется
                    if (shot.def.kind == ProjectileKind.Shell)
                        Explode(shot, shot.position, Vector3.up, null, false);
                    RemoveAt(i);
                    continue;
                }
                shots[i] = shot;
            }
        }

        void RemoveAt(int i)
        {
            int last = shots.Count - 1;
            shots[i] = shots[last];
            shots.RemoveAt(last);
        }

        bool TryHit(in Shot shot, float step)
        {
            int count = Combat.SortedRaycast(shot.position, shot.direction, step);
            var hits = Combat.Hits;
            for (int k = 0; k < count; k++)
            {
                var hit = hits[k];
                var target = Combat.DamageableAt(hit);
                if (target != null)
                {
                    // Через уже убитых и через своих бойцов и здания — насквозь;
                    // своя стена снаряд останавливает, но не получает урона и воронки
                    if (!target.IsAlive)
                        continue;
                    if (target.Faction == shot.owner)
                    {
                        if (target is not WallSegment)
                            continue;
                        target = null;
                    }
                    Impact(shot, hit.point, hit.normal, target, false);
                    return true;
                }

                if (hit.collider.isTrigger)
                    continue;

                Impact(shot, hit.point, hit.normal, null, hit.collider.transform.IsChildOf(arena.transform));
                return true;
            }
            return false;
        }

        void Impact(in Shot shot, Vector3 point, Vector3 normal, IDamageable target, bool terrain)
        {
            if (shot.def.kind == ProjectileKind.Shell)
            {
                Explode(shot, point, normal, target, terrain);
                return;
            }

            if (shot.def.kind == ProjectileKind.Flame)
            {
                // Огонь: вспышка пламени, урон всем рядом, без воронки
                effects.FlameBurst(point);
                if (target != null)
                    target.TakeDamage(Combat.DamageTo(shot.def, target, shot.def.damage), point, shot.direction);
                Combat.Splash(point, shot.def, shot.owner, target);
                return;
            }

            if (target != null)
            {
                // Здание и стена крошатся сами — только искры; машина — искры от брони; боец — искры
                // и крошки его цвета
                if (target is IAreaTarget)
                    effects.HitSparks(point, normal, VoxelBlocks.SlotWallSide, false);
                else if (target is VehicleUnit)
                    effects.HitSparks(point, normal, VoxelBlocks.SlotMetal, false);
                else
                    effects.HitSparks(point, normal, Effects.TeamSlot(target.Faction.team));
                target.TakeDamage(Combat.DamageTo(shot.def, target, shot.def.damage), point, shot.direction);
            }
            else if (terrain)
            {
                // Сначала цвет выбитого вокселя, потом сама воронка
                byte slot = Effects.GroundSlot(arena, point, normal);
                arena.Explode(point - normal * (shot.def.craterRadius * 0.5f), shot.def.craterRadius, CraterDepth);
                effects.GroundImpact(point, normal, slot, 0.5f);
            }
            else
            {
                // Своя стена или прочее препятствие
                effects.HitSparks(point, normal, VoxelBlocks.SlotWallSide);
            }
        }

        void Explode(in Shot shot, Vector3 point, Vector3 normal, IDamageable target, bool terrain)
        {
            byte slot = terrain ? Effects.GroundSlot(arena, point, normal) : VoxelBlocks.SlotStoneSide;
            effects.Explosion(point, shot.def.splashRadius);

            if (target != null)
                target.TakeDamage(Combat.DamageTo(shot.def, target, shot.def.damage), point, shot.direction);
            if (shot.def.splashRadius > 0f)
                Combat.Splash(point, shot.def, shot.owner, target);

            // Воронка — от попадания в землю; здание и участок стены крошатся сами (урон выше),
            // своя стена не страдает
            if (terrain)
            {
                arena.Explode(point - normal * (shot.def.craterRadius * 0.4f), shot.def.craterRadius, CraterDepth);
                effects.GroundImpact(point, normal, slot, 1.6f);
            }
        }

        void LateUpdate()
        {
            if (shots.Count == 0)
                return;

            int bullets = 0, shells = 0;
            var bounds = arena.WorldBounds;
            var bulletParams = new RenderParams(bulletMaterial) { worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off };
            var shellParams = new RenderParams(shellMaterial) { worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off };
            var mesh = Effects.CubeMesh;

            foreach (var shot in shots)
            {
                var rotation = Quaternion.LookRotation(shot.direction);
                if (shot.def.kind == ProjectileKind.Bullet)
                {
                    // Трассер не торчит назад из ствола в первые кадры
                    float length = Mathf.Min(BulletLength, shot.traveled + 0.1f);
                    bulletMatrices[bullets++] = Matrix4x4.TRS(shot.position - shot.direction * (length * 0.5f), rotation,
                                                              new Vector3(BulletWidth, BulletWidth, length));
                    if (bullets == BatchSize)
                    {
                        Graphics.RenderMeshInstanced(bulletParams, mesh, 0, bulletMatrices, bullets);
                        bullets = 0;
                    }
                }
                else if (shot.def.kind == ProjectileKind.Shell)
                {
                    shellMatrices[shells++] = Matrix4x4.TRS(shot.position, rotation, new Vector3(ShellSize, ShellSize, ShellLength));
                    if (shells == BatchSize)
                    {
                        Graphics.RenderMeshInstanced(shellParams, mesh, 0, shellMatrices, shells);
                        shells = 0;
                    }
                }
            }

            if (bullets > 0)
                Graphics.RenderMeshInstanced(bulletParams, mesh, 0, bulletMatrices, bullets);
            if (shells > 0)
                Graphics.RenderMeshInstanced(shellParams, mesh, 0, shellMatrices, shells);
        }
    }
}
