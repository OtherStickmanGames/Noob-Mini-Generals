using UnityEngine;

namespace Generals
{
    /// <summary>
    /// Значки интерфейса, нарисованные кодом (готовых иконок в проекте нет, а символы шрифта
    /// ненадёжны): белые, со сглаженными краями, красятся цветом Image.
    /// </summary>
    public static class UiIcons
    {
        static Sprite repeat;

        /// <summary>Круговая стрелка «повторять» — постоянный найм</summary>
        public static Sprite Repeat
        {
            get
            {
                if (repeat == null)
                    repeat = DrawRepeat(128);
                return repeat;
            }
        }

        // Кольцо почти по кругу (разрыв справа) и наконечник стрелки на его конце, направленный
        // против часовой — как значок «повтор»
        static Sprite DrawRepeat(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Repeat Icon",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float s = size;
            var center = new Vector2(s, s) * 0.5f;
            float outer = s * 0.40f, inner = s * 0.27f, mid = (outer + inner) * 0.5f;
            const float gapFrom = 0f, gapTo = 55f;     // градусы: разрыв кольца справа-сверху

            // Наконечник на конце кольца (угол 0°), остриём вверх — по ходу против часовой
            var baseCenter = center + new Vector2(mid, 0f);
            var tip = baseCenter + new Vector2(0f, s * 0.24f);
            var left = baseCenter + new Vector2(-s * 0.20f, -s * 0.02f);
            var right = baseCenter + new Vector2(s * 0.20f, -s * 0.02f);

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var d = p - center;
                    float r = d.magnitude;
                    float angle = Mathf.Repeat(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, 360f);

                    // Кольцо: расстояние до его полосы, сглаживание в пиксель
                    float ring = Mathf.Max(inner - r, r - outer);
                    float ringAlpha = Mathf.Clamp01(0.5f - ring);
                    if (angle > gapFrom && angle < gapTo)
                        ringAlpha = 0f;

                    float arrowAlpha = Mathf.Clamp01(0.5f + TriangleInside(p, tip, left, right));
                    float a = Mathf.Max(ringAlpha, arrowAlpha);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // Насколько точка внутри треугольника, в пикселях (>0 — внутри, <0 — снаружи)
        static float TriangleInside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            return Mathf.Min(EdgeDistance(p, a, b), Mathf.Min(EdgeDistance(p, b, c), EdgeDistance(p, c, a)));
        }

        static float EdgeDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var edge = b - a;
            var normal = new Vector2(-edge.y, edge.x).normalized;
            // Обход a→b→c против часовой — внутренняя нормаль слева от ребра
            return Vector2.Dot(p - a, normal);
        }
    }
}
