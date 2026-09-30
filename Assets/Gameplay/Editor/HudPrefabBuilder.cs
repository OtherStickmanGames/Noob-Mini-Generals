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

        internal static readonly Color PanelColor = new(0.08f, 0.09f, 0.11f, 0.82f);
        internal static readonly Color ButtonColor = new(0.22f, 0.25f, 0.30f, 1f);
        internal static readonly Color AccentColor = new(0.24f, 0.52f, 0.28f, 1f);
        internal static readonly Color CancelColor = new(0.62f, 0.2f, 0.18f, 1f);
        internal static readonly Color ProgressBackColor = new(0f, 0f, 0f, 0.45f);
        internal static readonly Color DefendColor = new(0.22f, 0.42f, 0.72f, 1f);
        internal static readonly Color HealthBackColor = new(0f, 0f, 0f, 0.65f);
        internal static readonly Color HealthLagColor = new(1f, 0.95f, 0.85f, 0.9f);
        internal static readonly Color DimColor = new(0f, 0f, 0f, 0.55f);


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
            if (prefab != null)
                return prefab;
            Build();
            // Новый ПК-интерфейс сразу со ссылкой на мобильный
            HudMobilePrefabBuilder.EnsureAndLink();
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
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

            // Установка здания: строка подсказки под ресурсами и кнопки, которые висят над зданием
            var hintPanel = MakePanel(t, "Placement Hint", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(900f, 64f), PanelColor);
            var placementHint = MakeLabel(hintPanel, "Казармы · 150", 32, TextAlignmentOptions.Center);

            var buttons = new GameObject("Placement Buttons", typeof(RectTransform)).GetComponent<RectTransform>();
            buttons.SetParent(t, false);
            buttons.anchorMin = buttons.anchorMax = Vector2.zero;
            buttons.pivot = new Vector2(0.5f, 0f);
            buttons.sizeDelta = new Vector2(440f, 100f);
            var confirm = MakeButton(buttons, "Confirm", "Строить", new Vector2(0f, 0f), Vector2.zero, new Vector2(210f, 100f), AccentColor);
            var cancel = MakeButton(buttons, "Cancel", "Отмена", new Vector2(1f, 0f), Vector2.zero, new Vector2(210f, 100f), CancelColor);

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
            so.FindProperty("placementButtons").objectReferenceValue = buttons;
            so.FindProperty("confirmButton").objectReferenceValue = confirm;
            so.FindProperty("cancelButton").objectReferenceValue = cancel;
            so.FindProperty("placementHintPanel").objectReferenceValue = hintPanel.gameObject;
            so.FindProperty("placementHint").objectReferenceValue = placementHint;
            so.FindProperty("selectionPanel").objectReferenceValue = selection.gameObject;
            so.FindProperty("selectionTitle").objectReferenceValue = selectionTitle;
            so.FindProperty("selectionInfo").objectReferenceValue = selectionInfo;
            so.FindProperty("hireButton").objectReferenceValue = hire;
            so.ApplyModifiedPropertiesWithoutUndo();

            AddBarracksControls(root);
            AddCombatControls(root);
            AddReinforceControls(root, false);

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// Дополняет уже существующий префаб HUD элементами, которых в нём ещё нет (панель казарм,
        /// значок поведения, полоски прочности, итоги боя), не трогая остальное — ручные правки сохраняются.
        /// Вызывается сам после компиляции (HudPrefabUpgrader).
        /// </summary>
        public static void UpgradeIfNeeded()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || prefab.GetComponent<GameHud>() is not { } hud)
                return;
            bool barracks = NeedsBarracksControls(hud), combat = NeedsCombatControls(hud);
            bool reinforce = NeedsReinforceControls(hud);
            if (barracks || combat || reinforce)
                UpgradeControls(barracks, combat, reinforce);

            // Мобильный интерфейс: собрать, если его нет, и привязать к ПК-интерфейсу
            HudMobilePrefabBuilder.EnsureAndLink();
        }

        static void UpgradeControls(bool barracks, bool combat, bool reinforce)
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                if (barracks)
                    AddBarracksControls(root);
                if (combat)
                    AddCombatControls(root);
                if (reinforce)
                    AddReinforceControls(root, false);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (barracks)
                    Debug.Log("[HUD] В префаб HUD добавлены панель казарм и значки поведения");
                if (combat)
                    Debug.Log("[HUD] В префаб HUD добавлены полоски прочности и итоги боя");
                if (reinforce)
                    Debug.Log("[HUD] В префаб HUD добавлен список отрядов пункта подкрепления");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        internal static bool NeedsReinforceControls(GameHud hud) =>
            new SerializedObject(hud).FindProperty("reinforcePanel").objectReferenceValue == null;

        /// <summary>
        /// Список отрядов пункта подкрепления — над панелью выбранного здания (её дочерний объект,
        /// растёт вверх по числу строк): заголовок, строки «Отряд N · режим · 3/5 | Пополнить +2 · 120»,
        /// строка-примечание. mobile — всё крупнее, под палец.
        /// </summary>
        internal static void AddReinforceControls(GameObject root, bool mobile)
        {
            var hud = root.GetComponent<GameHud>();
            var so = new SerializedObject(hud);
            var selection = ((GameObject)so.FindProperty("selectionPanel").objectReferenceValue).GetComponent<RectTransform>();
            float k = mobile ? 1.55f : 1f;

            var panel = MakePanel(selection, "Reinforce", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0f),
                                  new Vector2(0f, 12f), new Vector2(0f, 100f), PanelColor);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            int pad = Mathf.RoundToInt(14 * k);
            layout.padding = new RectOffset(pad, pad, pad, pad);
            layout.spacing = 8f * k;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var header = MakeLabel(panel, "Пополнение отрядов", Mathf.RoundToInt(32 * k), TextAlignmentOptions.MidlineLeft);
            header.fontStyle = FontStyles.Bold;
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f * k;

            var row = MakePanel(panel, "Reinforce Row Template", Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f),
                                Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.06f));
            row.GetComponent<Image>().raycastTarget = false;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 66f * k;
            var labelArea = MakePanel(row, "Label", Vector2.zero, new Vector2(0.56f, 1f), new Vector2(0f, 0.5f),
                                      Vector2.zero, Vector2.zero, Color.clear);
            labelArea.offsetMin = Vector2.zero;
            labelArea.offsetMax = Vector2.zero;
            MakeLabel(labelArea, "Отряд 1 · оборона · 5/5", Mathf.RoundToInt(27 * k), TextAlignmentOptions.MidlineLeft);
            var button = MakeStretchButton(row, "Reinforce", "Пополнить +2 · 120", new Vector2(0.57f, 0.08f), new Vector2(1f, 0.92f),
                                           Vector2.zero, Vector2.zero, AccentColor);
            button.GetComponentInChildren<TMP_Text>().fontSize = Mathf.RoundToInt(27 * k);

            var note = MakeLabel(panel, "Отрядов нет — наймите в казармах", Mathf.RoundToInt(27 * k), TextAlignmentOptions.MidlineLeft);
            note.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f * k;

            so.FindProperty("reinforcePanel").objectReferenceValue = panel.gameObject;
            so.FindProperty("reinforceRowTemplate").objectReferenceValue = row;
            so.FindProperty("reinforceNote").objectReferenceValue = note;
            so.FindProperty("reinforceMaxRows").intValue = mobile ? 4 : 6;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static bool NeedsCombatControls(GameHud hud)
        {
            var so = new SerializedObject(hud);
            return so.FindProperty("healthBarTemplate").objectReferenceValue == null ||
                   so.FindProperty("matchEndPanel").objectReferenceValue == null ||
                   so.FindProperty("resultsButton").objectReferenceValue == null;
        }

        // Образец полоски прочности (фон, светлый след урона Lag, заполнение Fill), экран итогов боя
        // и кнопка «Итоги боя» на месте «Строить»
        static void AddCombatControls(GameObject root)
        {
            var hud = root.GetComponent<GameHud>();
            var so = new SerializedObject(hud);
            var t = root.transform;

            if (so.FindProperty("healthBarTemplate").objectReferenceValue == null)
            {
                var bar = MakePanel(t, "Health Bar Template", Vector2.zero, Vector2.zero, new Vector2(0.5f, 0f),
                                    Vector2.zero, new Vector2(80f, 14f), HealthBackColor);
                bar.GetComponent<Image>().raycastTarget = false;
                foreach (var (name, color) in new[] { ("Lag", HealthLagColor), ("Fill", AccentColor) })
                {
                    var part = MakePanel(bar, name, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero, color);
                    part.offsetMin = new Vector2(2f, 2f);
                    part.offsetMax = new Vector2(-2f, -2f);
                    part.GetComponent<Image>().raycastTarget = false;
                }
                // Полоски — под остальным интерфейсом
                bar.SetAsFirstSibling();
                so.FindProperty("healthBarTemplate").objectReferenceValue = bar;
            }

            if (so.FindProperty("matchEndPanel").objectReferenceValue == null)
            {
                // Затемнение на весь экран — заодно не пускает касания в мир под итогами
                var dim = MakePanel(t, "Match End", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, DimColor);
                var card = MakePanel(dim, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                     Vector2.zero, new Vector2(900f, 640f), PanelColor);

                var titleArea = MakePanel(card, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                                          new Vector2(0f, -20f), new Vector2(-40f, 140f), Color.clear);
                var title = MakeLabel(titleArea, "Победа!", 96, TextAlignmentOptions.Center);
                title.fontStyle = FontStyles.Bold;

                var statsArea = MakePanel(card, "Stats", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                                          new Vector2(0f, -170f), new Vector2(-80f, 300f), Color.clear);
                var stats = MakeLabel(statsArea, "Время боя: 0:00", 34, TextAlignmentOptions.Top);

                var newMatch = MakeButton(card, "New Match", "Новый бой", new Vector2(0.5f, 0f), new Vector2(-200f, 40f),
                                          new Vector2(380f, 110f), AccentColor);
                var lookAround = MakeButton(card, "Look Around", "Осмотреться", new Vector2(0.5f, 0f), new Vector2(200f, 40f),
                                            new Vector2(380f, 110f), ButtonColor);

                // Итоги — поверх всего
                dim.SetAsLastSibling();
                so.FindProperty("matchEndPanel").objectReferenceValue = dim.gameObject;
                so.FindProperty("matchEndTitle").objectReferenceValue = title;
                so.FindProperty("matchEndStats").objectReferenceValue = stats;
                so.FindProperty("newMatchButton").objectReferenceValue = newMatch;
                so.FindProperty("lookAroundButton").objectReferenceValue = lookAround;
            }

            if (so.FindProperty("resultsButton").objectReferenceValue == null)
            {
                var results = MakeButton(t, "Results Button", "Итоги боя", new Vector2(1f, 0f), new Vector2(-30f, 30f),
                                         new Vector2(300f, 120f), AccentColor);
                so.FindProperty("resultsButton").objectReferenceValue = results;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static bool NeedsBarracksControls(GameHud hud)
        {
            var so = new SerializedObject(hud);
            return so.FindProperty("hireProgressFill").objectReferenceValue == null ||
                   so.FindProperty("behaviorRow").objectReferenceValue == null ||
                   so.FindProperty("barracksBadgeTemplate").objectReferenceValue == null;
        }

        // Панель выбранного здания становится выше: полоса прогресса найма под описанием и строка
        // «Оборона / Атака» над кнопкой найма. Плюс образец значка поведения над казармами.
        static void AddBarracksControls(GameObject root)
        {
            var hud = root.GetComponent<GameHud>();
            var so = new SerializedObject(hud);
            var selection = ((GameObject)so.FindProperty("selectionPanel").objectReferenceValue).GetComponent<RectTransform>();

            if (so.FindProperty("hireProgressFill").objectReferenceValue == null)
            {
                selection.sizeDelta += new Vector2(0f, 130f);
                var bar = MakePanel(selection, "Hire Progress", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                                    new Vector2(0f, -165f), new Vector2(-40f, 22f), ProgressBackColor);
                bar.GetComponent<Image>().raycastTarget = false;
                var fill = MakePanel(bar, "Fill", Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                                     Vector2.zero, Vector2.zero, AccentColor);
                fill.GetComponent<Image>().raycastTarget = false;
                so.FindProperty("hireProgressFill").objectReferenceValue = fill;
            }

            if (so.FindProperty("behaviorRow").objectReferenceValue == null)
            {
                var row = MakePanel(selection, "Behavior", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                                    new Vector2(0f, 135f), new Vector2(-40f, 90f), Color.clear);
                var defend = MakeStretchButton(row, "Defend", "Оборона", new Vector2(0f, 0f), new Vector2(0.5f, 1f),
                                               Vector2.zero, new Vector2(-6f, 0f), DefendColor);
                var attack = MakeStretchButton(row, "Attack", "Атака", new Vector2(0.5f, 0f), new Vector2(1f, 1f),
                                               new Vector2(6f, 0f), Vector2.zero, ButtonColor);
                so.FindProperty("behaviorRow").objectReferenceValue = row.gameObject;
                so.FindProperty("defendButton").objectReferenceValue = defend;
                so.FindProperty("attackButton").objectReferenceValue = attack;
            }

            if (so.FindProperty("barracksBadgeTemplate").objectReferenceValue == null)
            {
                var badge = MakePanel(root.transform, "Barracks Badge Template", Vector2.zero, Vector2.zero, new Vector2(0.5f, 0f),
                                      Vector2.zero, new Vector2(190f, 50f), DefendColor);
                badge.GetComponent<Image>().raycastTarget = false;
                MakeLabel(badge, "Оборона", 28, TextAlignmentOptions.Center);
                // Значки — под остальным интерфейсом
                badge.SetAsFirstSibling();
                so.FindProperty("barracksBadgeTemplate").objectReferenceValue = badge;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static Button MakeStretchButton(Transform parent, string name, string text, Vector2 anchorMin, Vector2 anchorMax,
                                        Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var rect = MakePanel(parent, name, anchorMin, anchorMax, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, color);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var button = rect.gameObject.AddComponent<Button>();
            MakeLabel(rect, text, 34, TextAlignmentOptions.Center);
            return button;
        }

        internal static RectTransform MakePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
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
        internal static TextMeshProUGUI MakeLabel(Transform parent, string text, int size, TextAlignmentOptions alignment)
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
        internal static Button MakeButton(Transform parent, string name, string text, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var rect = MakePanel(parent, name, anchor, anchor, anchor, position, size, color);
            var button = rect.gameObject.AddComponent<Button>();
            MakeLabel(rect, text, 34, TextAlignmentOptions.Center);
            return button;
        }
    }
}
