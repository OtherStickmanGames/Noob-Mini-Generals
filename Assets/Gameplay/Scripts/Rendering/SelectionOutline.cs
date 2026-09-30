using System.Collections.Generic;
using UnityEngine;

namespace Generals
{
    /// <summary>
    /// Кого обводит экранный контур (OutlineFeature) и каким цветом.
    /// Сейчас — здание при установке: зелёный — можно ставить, красный — нельзя.
    /// </summary>
    public static class SelectionOutline
    {
        public static readonly List<Renderer> Renderers = new();
        public static Color Color = Color.white;

        /// <summary>Толщина линии в пикселях при высоте экрана 1080; на других экранах — пропорционально</summary>
        public static float WidthAt1080 = 4f;
        /// <summary>Сила линии там, где объект закрыт стеной или другим зданием</summary>
        public static float HiddenLineAlpha = 0.45f;
        /// <summary>Заливка закрытой части объекта</summary>
        public static float HiddenFillAlpha = 0.25f;

        public static bool HasTargets
        {
            get
            {
                foreach (var r in Renderers)
                    if (r != null && r.enabled && r.gameObject.activeInHierarchy)
                        return true;
                return false;
            }
        }

        public static void Set(Renderer renderer, Color color)
        {
            Renderers.Clear();
            Renderers.Add(renderer);
            Color = color;
        }

        public static void Clear() => Renderers.Clear();
    }
}
