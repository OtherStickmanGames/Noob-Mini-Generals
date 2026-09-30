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
        }

        // Вид трассера пули и снаряда турели
        const float BulletWidth = 0.07f;
        const float BulletLength = 0.9f;
        const float ShellSize = 0.2f;
        const float ShellLength = 0.36f;
        const float ShellTrailInterval = 0.03f;
        const int BatchSize = 1023;

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

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var shot = shots[i];
                float step = shot.def.projectileSpeed * dt;
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

                if (shot.traveled >= shot.maxDistance)
                {
                    // Снаряд турели на излёте рвётся в воздухе, пуля просто теряется
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
                var target = Combat.DamageableOf(hit.collider);
                if (target != null)
                {
                    // Через своих и через уже убитых — насквозь
                    if (target.Faction == shot.owner || !target.IsAlive)
                        continue;
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

            if (target != null)
            {
                effects.HitSparks(point, normal, target is Structure ? VoxelBlocks.SlotWallSide : Effects.TeamSlot(target.Faction.team));
                target.TakeDamage(shot.def.damage);
            }
            else if (terrain)
            {
                // Сначала цвет выбитого вокселя, потом сама воронка
                byte slot = Effects.GroundSlot(arena, point, normal);
                arena.Explode(point - normal * (shot.def.craterRadius * 0.5f), shot.def.craterRadius);
                effects.GroundImpact(point, normal, slot, 0.5f);
            }
            else
            {
                effects.HitSparks(point, normal, VoxelBlocks.SlotStoneSide);
            }
        }

        void Explode(in Shot shot, Vector3 point, Vector3 normal, IDamageable target, bool terrain)
        {
            byte slot = terrain ? Effects.GroundSlot(arena, point, normal) : VoxelBlocks.SlotStoneSide;
            effects.Explosion(point, shot.def.splashRadius);

            if (target != null)
                target.TakeDamage(shot.def.damage);
            if (shot.def.splashRadius > 0f)
                Combat.Splash(point, shot.def.splashRadius, shot.def.damage, shot.owner, target);

            // Воронка — от попадания в землю и стены; в здание — нет, иначе оно повиснет над ямой
            if (terrain)
            {
                arena.Explode(point - normal * (shot.def.craterRadius * 0.4f), shot.def.craterRadius);
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
                else
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
