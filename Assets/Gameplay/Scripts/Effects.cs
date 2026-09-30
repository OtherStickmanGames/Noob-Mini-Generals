using System.Collections;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;
using UnityEngine.Rendering;

namespace Generals
{
    /// <summary>
    /// Эффекты боя кубиками в стиле арены: вспышки выстрелов, искры попаданий, обломки, дым, взрывы.
    /// Три системы частиц на весь бой (без GameObject на эффект):
    /// обломки (цвет из палитры арены, гравитация, отскок от земли), дым (полупрозрачный, всплывает),
    /// вспышки (светятся, быстро гаснут).
    /// </summary>
    public class Effects : MonoBehaviour
    {
        public enum Mode { Lit, Palette, Emissive, Blueprint }

        static readonly Color BlueprintColor = new(0.55f, 0.8f, 1f, 0.22f);

        /// <summary>Полупрозрачный чертёж недостроенной части здания с сеткой вокселей</summary>
        public Material BlueprintMaterial { get; private set; }

        static Mesh cubeMesh;

        /// <summary>Куб 1×1×1 с центром в нуле, белым цветом вершин и нормалями граней</summary>
        public static Mesh CubeMesh
        {
            get
            {
                if (cubeMesh == null)
                    cubeMesh = BuildCube();
                return cubeMesh;
            }
        }

        ParticleSystem debris;
        ParticleSystem smoke;
        ParticleSystem flash;
        Material debrisMaterial;
        Material smokeMaterial;
        Material flashMaterial;

        void Awake()
        {
            debrisMaterial = CreateMaterial("Debris", Mode.Palette, Color.white, false);
            smokeMaterial = CreateMaterial("Smoke", Mode.Lit, Color.white, true);
            flashMaterial = CreateMaterial("Flash", Mode.Emissive, new Color(1f, 0.82f, 0.45f, 1f), false);
            BlueprintMaterial = CreateMaterial("Blueprint", Mode.Blueprint, BlueprintColor, true);

            debris = CreateSystem("Обломки", debrisMaterial, 5000, 1f);
            var collision = debris.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.collidesWith = Physics.DefaultRaycastLayers;
            collision.bounce = 0.3f;
            collision.dampen = 0.45f;
            collision.lifetimeLoss = 0f;
            // Кубик под конец жизни сжимается и пропадает
            SetSizeOverLifetime(debris, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f)));

            smoke = CreateSystem("Дым", smokeMaterial, 2000, -0.06f);
            SetSizeOverLifetime(smoke, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.4f)));
            // Гаснет только альфа: цвет вершины множится на градиент, белый его не меняет
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            var smokeColor = smoke.colorOverLifetime;
            smokeColor.enabled = true;
            smokeColor.color = fade;

            flash = CreateSystem("Вспышки", flashMaterial, 1500, 0f);
            SetSizeOverLifetime(flash, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
        }

        void OnDestroy()
        {
            Destroy(debrisMaterial);
            Destroy(smokeMaterial);
            Destroy(flashMaterial);
            Destroy(BlueprintMaterial);
        }

        public void Clear()
        {
            StopAllCoroutines();
            debris.Clear();
            smoke.Clear();
            flash.Clear();
        }

        // ---------- Эффекты ----------

        /// <summary>Вспышка у ствола и облачко дыма</summary>
        public void MuzzleFlash(Vector3 muzzle, Vector3 direction, float scale)
        {
            for (int i = 0; i < 3; i++)
            {
                var dir = Combat.RandomInCone(direction, 25f);
                Emit(flash, muzzle + dir * (0.08f * i), dir * Random.Range(1f, 3f) * scale,
                     Random.Range(0.08f, 0.14f) * scale, Random.Range(0.05f, 0.09f), Color.white);
            }
            EmitSmoke(muzzle + direction * 0.15f, direction * 0.6f + Vector3.up * 0.3f, 0.14f * scale, 0.5f, 0.75f);
        }

        /// <summary>Попадание пули в бойца или здание: искры и крошки его цвета</summary>
        public void HitSparks(Vector3 point, Vector3 normal, byte paletteSlot, bool chips = true)
        {
            for (int i = 0; i < 2; i++)
                Emit(flash, point, Combat.RandomInCone(normal, 60f) * Random.Range(2f, 4f), 0.06f, 0.08f, Color.white);
            // Здания и стены крошатся сами (выбитые воксели) — крошки не нужны
            if (!chips)
                return;
            for (int i = 0; i < 3; i++)
                EmitDebris(point + normal * 0.05f, Combat.RandomInCone(normal, 60f) * Random.Range(1.5f, 3.5f) + Vector3.up,
                           Random.Range(0.06f, 0.1f), paletteSlot, 0.9f);
        }

        /// <summary>Попадание в землю или стену: выбитые кусочки её цвета и пыль. scale — сила</summary>
        public void GroundImpact(Vector3 point, Vector3 normal, byte paletteSlot, float scale)
        {
            int chips = Mathf.RoundToInt(3 * scale) + 1;
            for (int i = 0; i < chips; i++)
                EmitDebris(point + normal * 0.1f, Combat.RandomInCone(normal, 50f) * Random.Range(2f, 4.5f) * math.sqrt(scale),
                           Random.Range(0.08f, 0.16f) * math.sqrt(scale), paletteSlot, Random.Range(1.5f, 2.5f));

            int puffs = Mathf.RoundToInt(2 * scale);
            for (int i = 0; i < puffs; i++)
                EmitSmoke(point + Random.insideUnitSphere * 0.15f * scale, Combat.RandomInCone(normal, 40f) * 0.8f,
                          Random.Range(0.18f, 0.3f) * scale, Random.Range(0.6f, 1.1f), 0.8f, DustColor);
        }

        /// <summary>Взрыв снаряда: огненная вспышка и клубы дыма</summary>
        public void Explosion(Vector3 point, float radius)
        {
            float r = Mathf.Max(radius, 0.5f);
            for (int i = 0; i < 10; i++)
                Emit(flash, point + Random.insideUnitSphere * r * 0.2f, Random.onUnitSphere * Random.Range(2f, 5f) * r,
                     Random.Range(0.2f, 0.35f) * r, Random.Range(0.12f, 0.22f), Color.white);
            for (int i = 0; i < 8; i++)
                EmitSmoke(point + Random.insideUnitSphere * r * 0.4f, Random.onUnitSphere * r * 0.8f + Vector3.up * 0.8f,
                          Random.Range(0.35f, 0.6f) * r, Random.Range(1.2f, 2.2f), 0.7f);
        }

        static readonly Color FlameInner = new(1f, 0.85f, 0.35f, 1f);
        static readonly Color FlameOuter = new(1f, 0.42f, 0.12f, 1f);

        /// <summary>
        /// Струя огнемёта: светящиеся кубики пламени вдоль полёта (у начала — жёлтые и мелкие, к концу —
        /// оранжевые и крупные), в конце немного дыма. t — пройденная доля пути 0..1
        /// </summary>
        public void FlameTrail(Vector3 position, Vector3 direction, float t)
        {
            for (int i = 0; i < 2; i++)
            {
                var velocity = Combat.RandomInCone(direction, 18f) * Random.Range(1f, 3f) + Vector3.up * Random.Range(0.3f, 1.2f);
                Emit(flash, position + Random.insideUnitSphere * (0.08f + 0.25f * t), velocity,
                     Mathf.Lerp(0.12f, 0.34f, t) * Random.Range(0.8f, 1.2f), Random.Range(0.12f, 0.25f),
                     Color.Lerp(FlameInner, FlameOuter, t + Random.Range(-0.2f, 0.2f)));
            }
            if (t > 0.6f && Random.value < 0.25f)
                EmitSmoke(position, Vector3.up * 0.8f, Random.Range(0.2f, 0.32f), Random.Range(0.6f, 1f), 0.4f);
        }

        /// <summary>Огонь ударил в цель или землю: клуб пламени и дым</summary>
        public void FlameBurst(Vector3 point)
        {
            for (int i = 0; i < 6; i++)
                Emit(flash, point + Random.insideUnitSphere * 0.3f, Random.insideUnitSphere * 1.5f + Vector3.up * 1.5f,
                     Random.Range(0.2f, 0.4f), Random.Range(0.15f, 0.3f), Color.Lerp(FlameInner, FlameOuter, Random.value));
            EmitSmoke(point, Vector3.up * 1f, Random.Range(0.25f, 0.4f), Random.Range(0.8f, 1.3f), 0.5f);
        }

        /// <summary>Дымный след снаряда турели</summary>
        public void Trail(Vector3 point)
        {
            EmitSmoke(point, Random.insideUnitSphere * 0.1f, Random.Range(0.1f, 0.16f), Random.Range(0.35f, 0.55f), 0.6f);
        }

        /// <summary>Гибель бойца: разлетается кубиками своих цветов</summary>
        public void UnitDeath(Vector3 position, int team)
        {
            var center = position + Vector3.up * 1f;
            byte color = TeamSlot(team), dark = TeamDarkSlot(team);
            for (int i = 0; i < 14; i++)
            {
                byte slot = i < 7 ? color : i < 11 ? dark : i < 13 ? VoxelBlocks.SlotSand : VoxelBlocks.SlotMetal;
                EmitDebris(center + Random.insideUnitSphere * 0.35f, Random.insideUnitSphere * 3f + Vector3.up * 3f,
                           VoxelModels.VoxelSize * Random.Range(0.7f, 1f), slot, Random.Range(2f, 3f));
            }
            for (int i = 0; i < 3; i++)
                EmitSmoke(center + Random.insideUnitSphere * 0.3f, Vector3.up * 0.5f, Random.Range(0.3f, 0.45f), Random.Range(0.8f, 1.2f), 0.6f);
        }

        /// <summary>
        /// Стройка: воксель падает на своё место сверху (кубик его цвета, пока меш не показал сам
        /// воксель) и поднимает немного пыли
        /// </summary>
        public void VoxelPlaced(Vector3 position, byte paletteSlot, bool dust)
        {
            const float drop = 0.7f;
            const float time = 0.14f;
            Emit(debris, position + Vector3.up * drop, Vector3.down * (drop / time), VoxelModels.VoxelSize * 0.95f, time,
                 PaletteColor(paletteSlot));
            if (dust)
                EmitSmoke(position + Random.insideUnitSphere * 0.1f, Random.insideUnitSphere * 0.4f + Vector3.up * 0.2f,
                          Random.Range(0.12f, 0.2f), Random.Range(0.5f, 0.8f), 0.5f, DustColor);
        }

        /// <summary>Кусок стены (воксель арены крупнее вокселя модели)</summary>
        public void WallDebris(Vector3 position, Vector3 velocity)
        {
            EmitDebris(position, velocity, Random.Range(0.3f, 0.45f), VoxelBlocks.SlotWallSide, Random.Range(2.5f, 4f));
        }

        /// <summary>Облако пыли над обрушившимся участком стены</summary>
        public void Dust(Vector3 center, Vector2 half, float height)
        {
            for (int i = 0; i < 10; i++)
            {
                var p = center + new Vector3(Random.Range(-half.x, half.x), Random.Range(0f, height), Random.Range(-half.y, half.y));
                EmitSmoke(p, Random.insideUnitSphere * 0.6f + Vector3.up * 0.4f, Random.Range(0.5f, 0.9f),
                          Random.Range(1.5f, 2.8f), 0.7f, DustColor);
            }
        }

        /// <summary>Воксель, выбитый из здания: кубик размером с воксель модели</summary>
        public void VoxelDebris(Vector3 position, Vector3 velocity, byte paletteSlot)
        {
            EmitDebris(position, velocity, VoxelModels.VoxelSize * Random.Range(0.85f, 1f), paletteSlot, Random.Range(2.5f, 4f));
        }

        /// <summary>
        /// Разрушение здания: несколько взрывов по площади за полсекунды и долгий дым (сами
        /// воксели здания разлетаются из DestructibleModel.Shatter). half — половина размера по x и z.
        /// </summary>
        public void StructureDestroyed(Vector3 center, Vector2 halfExtents, float height)
        {
            StartCoroutine(StructureDestroyedRoutine(center, halfExtents, height));
        }

        IEnumerator StructureDestroyedRoutine(Vector3 center, Vector2 half, float height)
        {
            float area = half.x * half.y * 4f;
            int blasts = Mathf.Clamp(Mathf.RoundToInt(area / 3f), 2, 6);

            for (int b = 0; b < blasts; b++)
            {
                var point = center + new Vector3(Random.Range(-half.x, half.x) * 0.7f, Random.Range(0.2f, 0.8f) * height,
                                                 Random.Range(-half.y, half.y) * 0.7f);
                Explosion(point, Mathf.Clamp(height * 0.45f, 0.8f, 1.8f));
                yield return new WaitForSeconds(Random.Range(0.08f, 0.15f));
            }

            // Дым над развалинами
            for (int i = 0; i < blasts * 4; i++)
            {
                var point = center + new Vector3(Random.Range(-half.x, half.x), Random.Range(0f, height * 0.5f), Random.Range(-half.y, half.y));
                EmitSmoke(point, Vector3.up * Random.Range(0.6f, 1.2f), Random.Range(0.5f, 0.9f), Random.Range(2.5f, 4f), 0.45f);
            }
        }

        // ---------- Цвета ----------

        static readonly Color DustColor = new(0.72f, 0.66f, 0.56f, 1f);

        /// <summary>
        /// Цвет частицы-обломка: индекс палитры — в альфе. RGB цвета частиц в линейном цветовом
        /// пространстве Unity переводит из гаммы (индекс 32 становится ~2), альфу оставляет как есть.
        /// </summary>
        static Color32 PaletteColor(byte paletteSlot) => new(255, 255, 255, paletteSlot);

        public static byte TeamSlot(int team) => team == 0 ? VoxelBlocks.SlotTeamOne : VoxelBlocks.SlotTeamTwo;
        public static byte TeamDarkSlot(int team) => team == 0 ? VoxelBlocks.SlotTeamOneDark : VoxelBlocks.SlotTeamTwoDark;

        /// <summary>Слот палитры вокселя арены, в который пришлось попадание (до того, как его выбило)</summary>
        public static byte GroundSlot(VoxelArena arena, Vector3 point, Vector3 normal)
        {
            var voxel = (int3)math.floor(arena.WorldToVoxel(point - normal * 0.1f));
            var dims = arena.Dims;
            if (math.any(voxel < 0) || math.any(voxel >= dims))
                return VoxelBlocks.SlotDirt;
            byte block = arena.Voxels[(voxel.y * dims.z + voxel.z) * dims.x + voxel.x];
            if (block == VoxelBlocks.Air || block == VoxelBlocks.Water)
                return VoxelBlocks.SlotDirt;
            return VoxelBlocks.FaceSlot(block, normal.y > 0.5f ? VoxelBlocks.FaceTop : VoxelBlocks.FaceSide);
        }

        // ---------- Частицы ----------

        void EmitDebris(Vector3 position, Vector3 velocity, float size, byte paletteSlot, float lifetime)
        {
            Emit(debris, position, velocity, size, lifetime, PaletteColor(paletteSlot));
        }

        void EmitSmoke(Vector3 position, Vector3 velocity, float size, float lifetime, float alpha)
        {
            EmitSmoke(position, velocity, size, lifetime, alpha, SmokeColor);
        }

        static readonly Color SmokeColor = new(0.55f, 0.55f, 0.56f, 1f);

        void EmitSmoke(Vector3 position, Vector3 velocity, float size, float lifetime, float alpha, Color color)
        {
            color *= Random.Range(0.85f, 1.1f);
            color.a = alpha;
            Emit(smoke, position, velocity, size, lifetime, color);
        }

        static void Emit(ParticleSystem system, Vector3 position, Vector3 velocity, float size, float lifetime, Color32 color)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = lifetime,
                startColor = color,
                rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
                angularVelocity3D = Random.insideUnitSphere * 360f,
                applyShapeToPosition = false,
            };
            system.Emit(p, 1);
        }

        ParticleSystem CreateSystem(string name, Material material, int maxParticles, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var system = go.AddComponent<ParticleSystem>();
            // Система только что создана и уже играет; настраиваем её остановленной
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.startRotation3D = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = CubeMesh;
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enableGPUInstancing = false;

            system.Play();
            return system;
        }

        static void SetSizeOverLifetime(ParticleSystem system, AnimationCurve curve)
        {
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        /// <summary>Материал эффектов (шейдер Resources/Effect.shader)</summary>
        public static Material CreateMaterial(string name, Mode mode, Color color, bool transparent)
        {
            var material = new Material(Shader.Find("NoobGenerals/Effect")) { name = "Effect " + name };
            material.SetColor("_Color", color);
            if (mode == Mode.Palette)
                material.EnableKeyword("_PALETTE");
            else if (mode == Mode.Emissive)
                material.EnableKeyword("_EMISSIVE");
            else if (mode == Mode.Blueprint)
                material.EnableKeyword("_BLUEPRINT");

            if (transparent)
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetOverrideTag("RenderType", "Transparent");
            }
            material.enableInstancing = true;
            return material;
        }

        static Mesh BuildCube()
        {
            var vertices = new Vector3[24];
            var normals = new Vector3[24];
            var colors = new Color32[24];
            var indices = new int[36];
            Vector3[] faceNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

            for (int f = 0; f < 6; f++)
            {
                var n = faceNormals[f];
                // Две оси в плоскости грани так, чтобы обход был по часовой стрелке снаружи
                var u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                var v = Vector3.Cross(n, u);
                var c = n * 0.5f;
                int b = f * 4;
                vertices[b + 0] = c - u * 0.5f - v * 0.5f;
                vertices[b + 1] = c + u * 0.5f - v * 0.5f;
                vertices[b + 2] = c + u * 0.5f + v * 0.5f;
                vertices[b + 3] = c - u * 0.5f + v * 0.5f;
                for (int k = 0; k < 4; k++)
                {
                    normals[b + k] = n;
                    colors[b + k] = new Color32(255, 255, 255, 255);
                }
                int t = f * 6;
                indices[t + 0] = b;
                indices[t + 1] = b + 1;
                indices[t + 2] = b + 2;
                indices[t + 3] = b;
                indices[t + 4] = b + 2;
                indices[t + 5] = b + 3;
            }

            var mesh = new Mesh { name = "Effect Cube" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors32 = colors;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
