using System;
using UnityEngine;

namespace Generals
{
    /// <summary>Вид сетки застройки. Настраивается в инспекторе HUD, меняется на лету в Play mode.</summary>
    [Serializable]
    public class BuildGridStyle
    {
        [Tooltip("Клетка, где выбранное здание ставить можно. Альфа цвета умножается на непрозрачность линий и заливки")]
        public Color freeColor = new Color32(70, 235, 90, 255);
        [Tooltip("Клетка, где выбранное здание ставить нельзя")]
        public Color blockedColor = new Color32(245, 60, 45, 255);
        [Tooltip("Половина толщины линии, доля клетки (у соседних клеток линии складываются)")]
        [Range(0f, 0.2f)] public float lineWidth = 0.045f;
        [Range(0f, 1f)] public float lineAlpha = 0.9f;
        [Range(0f, 1f)] public float fillAlpha = 0.16f;
        [Tooltip("Апофема квадрата сетки вокруг центра экрана, в клетках")]
        [Range(5, 100)] public int apothem = 50;
        [Tooltip("Сколько крайних клеток плавно тают")]
        [Range(0, 30)] public int fadeCells = 8;
    }

    /// <summary>Вид стен и уже поставленных зданий, пока ставится новое</summary>
    [Serializable]
    public class PlacementSceneStyle
    {
        [Tooltip("Непрозрачность стен и других зданий")]
        [Range(0.1f, 1f)] public float opacity = 0.9f;
        [Tooltip("Насколько обесцветить стены и другие здания")]
        [Range(0f, 1f)] public float desaturate = 0.6f;
        [Tooltip("Насколько затемнить стены и другие здания")]
        [Range(0f, 1f)] public float darken = 0.2f;
    }

    /// <summary>Вид контура устанавливаемого здания</summary>
    [Serializable]
    public class PlacementOutlineStyle
    {
        [Tooltip("Здание можно поставить здесь")]
        public Color validColor = new(0.35f, 1f, 0.4f);
        [Tooltip("Здание здесь поставить нельзя")]
        public Color invalidColor = new(1f, 0.22f, 0.18f);
        [Tooltip("Толщина линии в пикселях при высоте экрана 1080, на других экранах — пропорционально")]
        [Range(1f, 12f)] public float widthAt1080 = 4f;
        [Tooltip("Сила линии там, где здание закрыто стеной или другим зданием")]
        [Range(0f, 1f)] public float hiddenLineAlpha = 0.45f;
        [Tooltip("Заливка закрытой части здания")]
        [Range(0f, 1f)] public float hiddenFillAlpha = 0.25f;
    }
}
