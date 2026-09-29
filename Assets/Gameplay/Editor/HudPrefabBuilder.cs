using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Generals.EditorTools
{
    /// <summary>
    /// Собирает Canvas интерфейса боя и сохраняет его префабом Assets/Gameplay/Prefabs/HUD.prefab.
    /// После сборки префаб правится руками в редакторе; пересобирать — только чтобы начать заново.
    /// </summary>
    public static class HudPrefabBuilder
    {
        public const string PrefabPath = "Assets/Gameplay/Prefabs/HUD.prefab";

        static readonly Color PanelColor = new(0.08f, 0.09f, 0.11f, 0.82f);
        static readonly Color ButtonColor = new(0.22f, 0.25f, 0.30f, 1f);
        static readonly Color AccentColor = new(0.24f, 0.52f, 0.28f, 1f);


        [MenuItem("Tools/Voxel Arena/Rebuild HUD Prefab")]
        static void RebuildMenu()
        {
            if (File.Exists(PrefabPath) &&
                !EditorUtility.DisplayDialog("HUD", "Префаб HUD уже есть. Пересобрать его заново? Ручные правки в нём пропадут.", "Пересобрать", "Отмена"))
                return;

            Build();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        /// <summary>Префаб HUD; если его ещё нет — собирается</summary>
        public static GameObject GetOrBuild()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            return prefab != null ? prefab : Build();
        }

        static GameObject Build()
        {
            var root = new GameObject("HUD", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            var hud = root.AddComponent<GameHud>();
            var t = root.transform;

            // Ресурсы сверху
            var top = MakePanel(t, "Resources", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 70f), PanelColor);
            var resourcesText = MakeLabel(top, "Базовый ресурс: 0     Ценный: 0", 34, TextAlignmentOptions.Center);

            // Сообщения
            var toast = MakePanel(t, "Toast", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1100f, 70f), Color.clear);
            var toastText = MakeLabel(toast, "Сообщение", 34, TextAlignmentOptions.Center);

            // Кнопка «Строить» и меню зданий над ней
            var buildToggle = MakeButton(t, "Build Button", "Строить", new Vector2(1f, 0f), new Vector2(-30f, 30f), new Vector2(300f, 120f), AccentColor);

            var menu = MakePanel(t, "Build Menu", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 170f), new Vector2(460f, 10f), PanelColor);
            var layout = menu.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            menu.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var template = MakeButton(menu, "Build Button Template", "Здание · 100", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 100f), ButtonColor);
            template.gameObject.AddComponent<LayoutElement>().preferredHeight = 100f;

            // Установка здания
            var bar = MakePanel(t, "Placement Bar", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(1000f, 210f), PanelColor);
            var hint = MakePanel(bar, "Hint", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 80f), Color.clear);
            var placementHint = MakeLabel(hint, "Коснитесь земли, чтобы выбрать место", 32, TextAlignmentOptions.Center);
            var confirm = MakeButton(bar, "Confirm", "Поставить", new Vector2(0.5f, 0f), new Vector2(-235f, 20f), new Vector2(430f, 100f), AccentColor);
            var cancel = MakeButton(bar, "Cancel", "Отмена", new Vector2(0.5f, 0f), new Vector2(235f, 20f), new Vector2(430f, 100f), ButtonColor);

            // Панель выбранного здания
            var selection = MakePanel(t, "Selection", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(30f, 30f), new Vector2(640f, 300f), PanelColor);
            var title = MakePanel(selection, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(-20f, 60f), Color.clear);
            var selectionTitle = MakeLabel(title, "Здание", 38, TextAlignmentOptions.MidlineLeft);
            var info = MakePanel(selection, "Info", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(-20f, 80f), Color.clear);
            var selectionInfo = MakeLabel(info, "Описание", 30, TextAlignmentOptions.TopLeft);
            var hire = MakeButton(selection, "Hire Builder", "Нанять строителя", new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(600f, 100f), AccentColor);

            // Ссылки для GameHud
            var so = new SerializedObject(hud);
            so.FindProperty("resourcesText").objectReferenceValue = resourcesText;
            so.FindProperty("toastText").objectReferenceValue = toastText;
            so.FindProperty("buildToggle").objectReferenceValue = buildToggle;
            so.FindProperty("buildMenu").objectReferenceValue = menu.gameObject;
            so.FindProperty("buildMenuContent").objectReferenceValue = menu;
            so.FindProperty("buildButtonTemplate").objectReferenceValue = template;
            so.FindProperty("placementBar").objectReferenceValue = bar.gameObject;
            so.FindProperty("placementHint").objectReferenceValue = placementHint;
            so.FindProperty("confirmButton").objectReferenceValue = confirm;
            so.FindProperty("cancelButton").objectReferenceValue = cancel;
            so.FindProperty("selectionPanel").objectReferenceValue = selection.gameObject;
            so.FindProperty("selectionTitle").objectReferenceValue = selectionTitle;
            so.FindProperty("selectionInfo").objectReferenceValue = selectionInfo;
            so.FindProperty("hireButton").objectReferenceValue = hire;
            so.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static RectTransform MakePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                   Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = color.a > 0f;
            return rect;
        }

        // Шрифт — TMP по умолчанию (LiberationSans SDF), кириллицу дорисовывает его динамический запасной шрифт
        static TextMeshProUGUI MakeLabel(Transform parent, string text, int size, TextAlignmentOptions alignment)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(16f, 4f);
            rect.offsetMax = new Vector2(-16f, -4f);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = Color.white;
            label.enableWordWrapping = true;
            label.raycastTarget = false;
            return label;
        }

        // Кнопка с якорем и опорной точкой в одном месте
        static Button MakeButton(Transform parent, string name, string text, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var rect = MakePanel(parent, name, anchor, anchor, anchor, position, size, color);
            var button = rect.gameObject.AddComponent<Button>();
            MakeLabel(rect, text, 34, TextAlignmentOptions.Center);
            return button;
        }
    }
}
