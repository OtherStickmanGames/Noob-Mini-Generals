using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Generals.EditorTools.HudPrefabBuilder;

namespace Generals.EditorTools
{
    /// <summary>
    /// Собирает мобильный интерфейс боя Assets/Gameplay/Prefabs/HUD_Mobile.prefab (та же логика GameHud,
    /// своя вёрстка под телефон) и привязывает его к ПК-интерфейсу (поле mobileVariant): на телефоне
    /// ПК-интерфейс из сцены сам заменяется мобильным.
    ///
    /// Вёрстка под палец, альбомная ориентация: кнопки не меньше ~130 единиц (около 9 мм), шрифт от 36,
    /// всё — внутри безопасной области экрана (SafeArea: вырез камеры, скругления углов); меню построек —
    /// лента карточек снизу с прокруткой пальцем, как в Clash of Clans; панель здания — снизу слева
    /// под большой палец; «Строить» — снизу справа.
    /// После сборки префаб правится руками; пересобрать — меню Tools/Voxel Arena/Rebuild Mobile HUD Prefab.
    /// </summary>
    public static class HudMobilePrefabBuilder
    {
        public const string PrefabPath = "Assets/Gameplay/Prefabs/HUD_Mobile.prefab";

        [MenuItem("Tools/Voxel Arena/Rebuild Mobile HUD Prefab")]
        static void RebuildMenu()
        {
            if (File.Exists(PrefabPath) &&
                !EditorUtility.DisplayDialog("HUD", "Мобильный префаб HUD уже есть. Пересобрать его заново? Ручные правки в нём пропадут.", "Пересобрать", "Отмена"))
                return;

            Build();
            EnsureAndLink();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        /// <summary>Мобильного префаба нет — собрать; у ПК-префаба нет ссылки на него — проставить</summary>
        public static void EnsureAndLink()
        {
            var mobile = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (mobile == null)
            {
                mobile = Build();
                Debug.Log("[HUD] Собран мобильный интерфейс " + PrefabPath);
            }
            else if (NeedsReinforceControls(mobile.GetComponent<GameHud>()))
            {
                // Уже собранный мобильный интерфейс дополняется новыми элементами, ручные правки сохраняются
                var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    AddReinforceControls(contents, true);
                    PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                    Debug.Log("[HUD] В мобильный интерфейс добавлен список отрядов пункта подкрепления");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                mobile = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }
            if (NeedsRepeatIcon(mobile.GetComponent<GameHud>()))
            {
                AddRepeatIconTo(PrefabPath);
                mobile = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }

            var pc = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabBuilder.PrefabPath);
            if (pc == null || pc.GetComponent<GameHud>() is not { } pcHud)
                return;
            if (new SerializedObject(pcHud).FindProperty("mobileVariant").objectReferenceValue != null)
                return;

            var root = PrefabUtility.LoadPrefabContents(HudPrefabBuilder.PrefabPath);
            try
            {
                var so = new SerializedObject(root.GetComponent<GameHud>());
                so.FindProperty("mobileVariant").objectReferenceValue = mobile.GetComponent<GameHud>();
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, HudPrefabBuilder.PrefabPath);
                Debug.Log("[HUD] ПК-интерфейс привязан к мобильному (на телефоне — мобильный)");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static GameObject Build()
        {
            var root = new GameObject("HUD Mobile", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Телефоны вытянуты по ширине (19.5:9) — масштаб по высоте, чтобы кнопки были одного размера
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            var hud = root.AddComponent<GameHud>();
            var t = root.transform;

            // Значки и полоски над объектами — под всем интерфейсом, позиция — экранная
            var badge = MakePanel(t, "Barracks Badge Template", Vector2.zero, Vector2.zero, new Vector2(0.5f, 0f),
                                  Vector2.zero, new Vector2(230f, 62f), DefendColor);
            badge.GetComponent<Image>().raycastTarget = false;
            MakeLabel(badge, "Оборона", 34, TextAlignmentOptions.Center);

            var bar = MakePanel(t, "Health Bar Template", Vector2.zero, Vector2.zero, new Vector2(0.5f, 0f),
                                Vector2.zero, new Vector2(110f, 20f), HealthBackColor);
            bar.GetComponent<Image>().raycastTarget = false;
            foreach (var (name, color) in new[] { ("Lag", HealthLagColor), ("Fill", AccentColor) })
            {
                var part = MakePanel(bar, name, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero, color);
                part.offsetMin = new Vector2(3f, 3f);
                part.offsetMax = new Vector2(-3f, -3f);
                part.GetComponent<Image>().raycastTarget = false;
            }

            // Всё остальное — в безопасной области экрана
            var safe = MakePanel(t, "Safe Area", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.clear);
            safe.gameObject.AddComponent<SafeArea>();

            // Ресурсы сверху
            var top = MakePanel(safe, "Resources", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                                Vector2.zero, new Vector2(0f, 84f), PanelColor);
            var resourcesText = MakeLabel(top, "Базовый ресурс: 0     Ценный: 0", 38, TextAlignmentOptions.Center);

            // Подсказка при установке — под ресурсами, сообщения — ниже неё
            var hintPanel = MakePanel(safe, "Placement Hint", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                      new Vector2(0f, -96f), new Vector2(1100f, 80f), PanelColor);
            var placementHint = MakeLabel(hintPanel, "Казармы · 150", 38, TextAlignmentOptions.Center);

            var toast = MakePanel(safe, "Toast", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                  new Vector2(0f, -190f), new Vector2(1500f, 80f), Color.clear);
            var toastText = MakeLabel(toast, "Сообщение", 44, TextAlignmentOptions.Center);

            // «Строить» — снизу справа, под большой палец
            var buildToggle = BigButton(safe, "Build Button", "Строить", new Vector2(1f, 0f), new Vector2(-24f, 24f),
                                     new Vector2(270f, 170f), AccentColor, 52);

            // Меню построек — лента карточек снизу (справа место под «Строить», чтобы им же закрыть)
            var menu = MakePanel(safe, "Build Menu", Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                                 Vector2.zero, Vector2.zero, PanelColor);
            menu.offsetMin = Vector2.zero;
            menu.offsetMax = new Vector2(-318f, 250f);
            var scroll = menu.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;

            var viewport = MakePanel(menu, "Viewport", Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero, Color.clear);
            viewport.offsetMin = new Vector2(16f, 16f);
            viewport.offsetMax = new Vector2(-16f, -16f);
            // Прозрачная, но ловит касания — чтобы ленту можно было листать за пустое место
            viewport.GetComponent<Image>().raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = Vector2.zero;
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 0.5f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var row = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 16f;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;

            var template = BigButton(content, "Build Button Template", "Здание · 100", new Vector2(0f, 0.5f), Vector2.zero,
                                  new Vector2(330f, 0f), ButtonColor, 42);
            template.gameObject.AddComponent<LayoutElement>().preferredWidth = 330f;

            // Кнопки «Строить / Отмена» над устанавливаемым зданием — крупнее, чем на ПК
            var buttons = new GameObject("Placement Buttons", typeof(RectTransform)).GetComponent<RectTransform>();
            buttons.SetParent(safe, false);
            buttons.anchorMin = buttons.anchorMax = Vector2.zero;
            buttons.pivot = new Vector2(0.5f, 0f);
            buttons.sizeDelta = new Vector2(560f, 150f);
            var confirm = BigButton(buttons, "Confirm", "Строить", new Vector2(0f, 0f), Vector2.zero, new Vector2(270f, 150f), AccentColor, 46);
            var cancel = BigButton(buttons, "Cancel", "Отмена", new Vector2(1f, 0f), Vector2.zero, new Vector2(270f, 150f), CancelColor, 46);

            // Панель выбранного здания — снизу слева. Сверху вниз: название, описание, полоса найма;
            // снизу вверх: кнопка найма, строка «Оборона / Атака» (у казарм; без неё панель ниже)
            var selection = MakePanel(safe, "Selection", Vector2.zero, Vector2.zero, Vector2.zero,
                                      new Vector2(24f, 24f), new Vector2(820f, 560f), PanelColor);
            var titleArea = MakePanel(selection, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                                      new Vector2(0f, -12f), new Vector2(-28f, 72f), Color.clear);
            var selectionTitle = MakeLabel(titleArea, "Здание", 46, TextAlignmentOptions.MidlineLeft);
            var infoArea = MakePanel(selection, "Info", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                                     new Vector2(0f, -92f), new Vector2(-28f, 110f), Color.clear);
            var selectionInfo = MakeLabel(infoArea, "Описание", 36, TextAlignmentOptions.TopLeft);

            var progress = MakePanel(selection, "Hire Progress", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                                     new Vector2(0f, -212f), new Vector2(-48f, 26f), ProgressBackColor);
            progress.GetComponent<Image>().raycastTarget = false;
            var fill = MakePanel(progress, "Fill", Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero, AccentColor);
            fill.GetComponent<Image>().raycastTarget = false;

            var behavior = MakePanel(selection, "Behavior", Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                                     new Vector2(0f, 170f), new Vector2(-48f, 130f), Color.clear);
            var defend = MakeStretchButton(behavior, "Defend", "Оборона", Vector2.zero, new Vector2(0.5f, 1f),
                                           Vector2.zero, new Vector2(-8f, 0f), DefendColor);
            var attack = MakeStretchButton(behavior, "Attack", "Атака", new Vector2(0.5f, 0f), Vector2.one,
                                           new Vector2(8f, 0f), Vector2.zero, ButtonColor);
            SetFont(defend, 44);
            SetFont(attack, 44);

            var hire = BigButton(selection, "Hire Builder", "Нанять строителя", new Vector2(0.5f, 0f), new Vector2(0f, 20f),
                              new Vector2(772f, 140f), AccentColor, 44);

            // Итоги боя — поверх всего, затемнение на весь экран (и под вырезом)
            var results = BigButton(safe, "Results Button", "Итоги боя", new Vector2(1f, 0f), new Vector2(-24f, 24f),
                                 new Vector2(270f, 170f), AccentColor, 46);

            var dim = MakePanel(t, "Match End", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, DimColor);
            var card = MakePanel(dim, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                 Vector2.zero, new Vector2(1150f, 800f), PanelColor);
            var endTitleArea = MakePanel(card, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                                         new Vector2(0f, -24f), new Vector2(-40f, 160f), Color.clear);
            var endTitle = MakeLabel(endTitleArea, "Победа!", 110, TextAlignmentOptions.Center);
            endTitle.fontStyle = FontStyles.Bold;
            var statsArea = MakePanel(card, "Stats", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                                      new Vector2(0f, -200f), new Vector2(-80f, 360f), Color.clear);
            var stats = MakeLabel(statsArea, "Время боя: 0:00", 42, TextAlignmentOptions.Top);
            var newMatch = BigButton(card, "New Match", "Новый бой", new Vector2(0.5f, 0f), new Vector2(-250f, 44f),
                                  new Vector2(460f, 150f), AccentColor, 50);
            var lookAround = BigButton(card, "Look Around", "Осмотреться", new Vector2(0.5f, 0f), new Vector2(250f, 44f),
                                    new Vector2(460f, 150f), ButtonColor, 50);

            var so = new SerializedObject(hud);
            so.FindProperty("layout").enumValueIndex = (int)GameHud.HudLayout.Mobile;
            so.FindProperty("resourcesText").objectReferenceValue = resourcesText;
            so.FindProperty("toastText").objectReferenceValue = toastText;
            so.FindProperty("buildToggle").objectReferenceValue = buildToggle;
            so.FindProperty("buildMenu").objectReferenceValue = menu.gameObject;
            so.FindProperty("buildMenuContent").objectReferenceValue = content;
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
            so.FindProperty("hireProgressFill").objectReferenceValue = fill;
            so.FindProperty("behaviorRow").objectReferenceValue = behavior.gameObject;
            so.FindProperty("defendButton").objectReferenceValue = defend;
            so.FindProperty("attackButton").objectReferenceValue = attack;
            so.FindProperty("barracksBadgeTemplate").objectReferenceValue = badge;
            so.FindProperty("healthBarTemplate").objectReferenceValue = bar;
            so.FindProperty("matchEndPanel").objectReferenceValue = dim.gameObject;
            so.FindProperty("matchEndTitle").objectReferenceValue = endTitle;
            so.FindProperty("matchEndStats").objectReferenceValue = stats;
            so.FindProperty("newMatchButton").objectReferenceValue = newMatch;
            so.FindProperty("lookAroundButton").objectReferenceValue = lookAround;
            so.FindProperty("resultsButton").objectReferenceValue = results;
            so.ApplyModifiedPropertiesWithoutUndo();

            AddReinforceControls(root, true);
            AddRepeatIcon(root);

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Button BigButton(Transform parent, string name, string text, Vector2 anchor, Vector2 position, Vector2 size,
                             Color color, int fontSize)
        {
            var button = MakeButton(parent, name, text, anchor, position, size, color);
            SetFont(button, fontSize);
            return button;
        }

        static void SetFont(Button button, int size)
        {
            button.GetComponentInChildren<TMP_Text>().fontSize = size;
        }
    }
}
