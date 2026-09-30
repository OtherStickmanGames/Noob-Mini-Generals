using UnityEngine;
using UnityEngine.Rendering;

namespace Generals
{
    /// <summary>
    /// Вид арены при установке здания: стены и готовые здания полупрозрачные.
    /// Непрозрачная земля с глобальным ключом _VOXEL_BUILD_FADE не рисует стены, их рисует
    /// BuildGridOverlay вторым проходом прозрачным материалом с ключом _WALLS_ONLY.
    /// </summary>
    public static class BuildModeVisuals
    {
        /// <summary>Непрозрачность стен при создании их материала; дальше её задаёт PlacementSceneStyle</summary>
        public const float DefaultOpacity = 0.9f;

        // Порядок в прозрачной очереди: сначала стены и здания, потом сетка поверх
        public const int FadedQueue = 3001;
        public const int GridQueue = 3002;

        static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        static readonly int HideWallsId = Shader.PropertyToID("_HideWalls");
        static readonly int DesaturateId = Shader.PropertyToID("_Desaturate");
        static readonly int DarkenId = Shader.PropertyToID("_Darken");

        /// <summary>Копия материала арены для отдельных объектов (зданий, призрака, стен): свои стены не прячет</summary>
        public static Material CreateVariant(Material arenaMaterial, string name)
        {
            var material = new Material(arenaMaterial) { name = name };
            material.SetFloat(HideWallsId, 0f);
            return material;
        }

        /// <summary>Материал стен для второго прохода: рисует только стены, полупрозрачно</summary>
        public static Material CreateWallsMaterial(Material arenaMaterial)
        {
            var material = CreateVariant(arenaMaterial, "Walls (build mode)");
            material.EnableKeyword("_WALLS_ONLY");
            SetOpacity(material, DefaultOpacity);
            return material;
        }

        /// <summary>1 — обычный непрозрачный материал, меньше — прозрачный с записью глубины</summary>
        public static void SetOpacity(Material material, float alpha)
        {
            bool transparent = alpha < 1f;
            material.SetFloat(SrcBlendId, (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
            material.SetFloat(DstBlendId, (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat(ZWriteId, 1f);
            material.SetFloat(AlphaId, alpha);
            material.renderQueue = transparent ? FadedQueue : -1;
        }

        /// <summary>Приглушить цвет: обесцветить и затемнить (0 — как есть)</summary>
        public static void SetMuted(Material material, float desaturate, float darken)
        {
            material.SetFloat(DesaturateId, desaturate);
            material.SetFloat(DarkenId, darken);
        }

        /// <summary>Вид стен и готовых зданий: при установке — по стилю, иначе обычный</summary>
        public static void Apply(Material material, PlacementSceneStyle style, bool placing)
        {
            SetOpacity(material, placing ? style.opacity : 1f);
            SetMuted(material, placing ? style.desaturate : 0f, placing ? style.darken : 0f);
        }

        /// <summary>Включить или выключить режим установки: земля перестаёт рисовать стены</summary>
        public static void SetActive(bool on)
        {
            if (on)
                Shader.EnableKeyword("_VOXEL_BUILD_FADE");
            else
                Shader.DisableKeyword("_VOXEL_BUILD_FADE");
        }
    }
}
