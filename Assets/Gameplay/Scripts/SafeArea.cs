using UnityEngine;

namespace Generals
{
    /// <summary>
    /// Растягивает RectTransform на безопасную область экрана (Screen.safeArea): интерфейс не
    /// залезает под вырез камеры, скругления углов и системные полосы телефона. Следит за
    /// поворотом экрана и сменой разрешения (и в Device Simulator).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SafeArea : MonoBehaviour
    {
        RectTransform rect;
        Rect applied;
        Vector2Int screen;

        void Awake()
        {
            rect = (RectTransform)transform;
            Apply();
        }

        void Update()
        {
            if (Screen.safeArea != applied || screen.x != Screen.width || screen.y != Screen.height)
                Apply();
        }

        void Apply()
        {
            applied = Screen.safeArea;
            screen = new Vector2Int(Screen.width, Screen.height);
            if (screen.x <= 0 || screen.y <= 0)
                return;

            rect.anchorMin = new Vector2(applied.xMin / screen.x, applied.yMin / screen.y);
            rect.anchorMax = new Vector2(applied.xMax / screen.x, applied.yMax / screen.y);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
