using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Generals
{
    /// <summary>
    /// Второе действие кнопки: правая кнопка мыши (ПК) или долгое нажатие (телефон) — как в
    /// Dawn of War правый клик по иконке отряда ставит постоянный найм. Обычный клик остаётся у
    /// Button.onClick; после долгого нажатия обычный клик надо пропустить (ConsumeLongPress).
    /// </summary>
    public class ButtonPressExtras : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        const float LongPressSeconds = 0.5f;

        public event Action Secondary;

        bool pressing;
        bool longPressFired;
        float pressTime;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                Secondary?.Invoke();
                return;
            }
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            pressing = true;
            longPressFired = false;
            pressTime = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData) => pressing = false;

        public void OnPointerExit(PointerEventData eventData) => pressing = false;

        void Update()
        {
            if (!pressing || longPressFired || Time.unscaledTime - pressTime < LongPressSeconds)
                return;
            longPressFired = true;
            Secondary?.Invoke();
        }

        /// <summary>Было долгое нажатие — следующий обычный клик не считать (true — пропустить)</summary>
        public bool ConsumeLongPress()
        {
            if (!longPressFired)
                return false;
            longPressFired = false;
            return true;
        }
    }
}
