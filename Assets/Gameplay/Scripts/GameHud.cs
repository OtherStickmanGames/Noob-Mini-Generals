using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Generals
{
    /// <summary>
    /// Интерфейс боя для телефона, собирается кодом (uGUI):
    /// ресурсы сверху, кнопка «Строить» и меню зданий, установка «тап по земле → Поставить / Отмена»,
    /// панель выбранного здания (в главном здании — найм строителя).
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        [SerializeField] RtsCamera rtsCamera;

        static readonly Color PanelColor = new(0.08f, 0.09f, 0.11f, 0.82f);
        static readonly Color ButtonColor = new(0.22f, 0.25f, 0.30f, 1f);
        static readonly Color AccentColor = new(0.24f, 0.52f, 0.28f, 1f);

        Font font;
        Text resourcesText;
        Text toastText;
        float toastTimer;

        GameObject buildMenu;
        readonly List<(StructureDef def, Button button)> buildButtons = new();

        GameObject placementBar;
        Text placementHint;
        Button confirmButton;

        GameObject selectionPanel;
        Text selectionTitle;
        Text selectionInfo;
        Button hireButton;
        Structure selected;

        // Установка здания
        StructureDef placingDef;
        int2 placingMin;
        bool placingValid;
        bool placingHasCell;
        GameObject ghost;
        Renderer ghostSlab;

        MatchManager Match => MatchManager.Instance;
        Faction Player => Match.Player;

        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildCanvas();
            rtsCamera.Tapped += Camera_Tapped;
        }

        void OnDestroy()
        {
            rtsCamera.Tapped -= Camera_Tapped;
        }

        void Update()
        {
            if (Player == null)
                return;

            resourcesText.text = $"Базовый ресурс: {Player.BaseResource}     Ценный: {Player.Valuable}     " +
                                 $"Строители: {Player.builders.Count}     Точки: {OwnedPoints()}";

            foreach (var (def, button) in buildButtons)
                button.interactable = Player.CanAfford(def.costBase, def.costValuable);

            UpdateSelection();

            if (toastTimer > 0f)
            {
                toastTimer -= Time.deltaTime;
                toastText.enabled = toastTimer > 0f;
            }

            // Отладка: захватить ближайшую к центру экрана точку (пока нет боевых юнитов)
            if (Input.GetKeyDown(KeyCode.C) && RaycastTerrain(new Vector2(Screen.width, Screen.height) * 0.5f, out var hit))
            {
                Match.DebugCaptureNearest(hit.point, 0);
                Toast("Ближайшая точка захвачена (отладка)");
            }
        }

        int OwnedPoints()
        {
            int count = 0;
            foreach (var p in Match.CapturePoints)
                if (p.Owner == Player.team)
                    count++;
            return count;
        }

        // ---------- Касания ----------

        void Camera_Tapped(Vector2 screen)
        {
            if (Player == null)
                return;

            if (placingDef != null)
            {
                if (RaycastTerrain(screen, out var hit))
                    MoveGhost(hit);
                return;
            }

            var ray = rtsCamera.Camera.ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out var anyHit, 1000f) && anyHit.collider.TryGetComponent<Structure>(out var structure))
                Select(structure);
            else
                Select(null);
        }

        bool RaycastTerrain(Vector2 screen, out RaycastHit terrainHit)
        {
            var ray = rtsCamera.Camera.ScreenPointToRay(screen);
            float best = float.MaxValue;
            terrainHit = default;
            foreach (var hit in Physics.RaycastAll(ray, 1000f))
            {
                if (!hit.collider.transform.IsChildOf(Match.Arena.transform) || hit.distance >= best)
                    continue;
                best = hit.distance;
                terrainHit = hit;
            }
            return best < float.MaxValue;
        }

        // ---------- Установка здания ----------

        void StartPlacing(StructureDef def)
        {
            CancelPlacing();
            Select(null);
            buildMenu.SetActive(false);

            placingDef = def;
            placingHasCell = false;
            placingValid = false;
            CreateGhost(def);

            placementBar.SetActive(true);
            placementHint.text = def.rule == PlacementRule.Deposit
                ? $"{def.name}: коснитесь месторождения или своей точки захвата"
                : $"{def.name}: коснитесь земли, чтобы выбрать место";
            confirmButton.interactable = false;

            // Сразу пробуем поставить в центр экрана
            if (RaycastTerrain(new Vector2(Screen.width, Screen.height) * 0.5f, out var hit))
                MoveGhost(hit);
        }

        void MoveGhost(RaycastHit hit)
        {
            var arena = Match.Arena;
            float3 voxel = arena.WorldToVoxel(hit.point - hit.normal * 0.05f);
            var cell = new int2((int)math.floor(voxel.x), (int)math.floor(voxel.z));

            // Добытчик прилипает к ближайшему месторождению или точке
            if (placingDef.rule == PlacementRule.Deposit && Match.Grid.SnapToResource(cell, 4, out var resource))
                cell = resource;

            placingMin = BuildGrid.MinFromCenter(placingDef, cell);
            placingHasCell = true;
            placingValid = Match.Grid.CanPlace(Player, placingDef, placingMin, out var reason, out _);

            ghost.SetActive(true);
            ghost.transform.position = Match.CellCenter(placingMin, placingDef.footprint);
            ghostSlab.material.color = placingValid ? new Color(0.3f, 0.9f, 0.3f) : new Color(0.95f, 0.25f, 0.2f);

            bool affordable = Player.CanAfford(placingDef.costBase, placingDef.costValuable);
            confirmButton.interactable = placingValid && affordable;
            placementHint.text = placingValid
                ? affordable ? $"{placingDef.name}: {Cost(placingDef)}" : "Не хватает ресурсов"
                : reason;
        }

        void ConfirmPlacing()
        {
            if (placingDef == null || !placingHasCell)
                return;

            if (Match.TryOrderConstruction(Player, placingDef, placingMin, out var reason))
            {
                Toast($"{placingDef.name}: заложено, строитель уже идёт");
                CancelPlacing();
            }
            else
            {
                Toast(reason);
            }
        }

        void CancelPlacing()
        {
            placingDef = null;
            placementBar.SetActive(false);
            if (ghost != null)
                Destroy(ghost);
            ghost = null;
        }

        void CreateGhost(StructureDef def)
        {
            ghost = new GameObject("Призрак здания");
            ghost.SetActive(false);

            var mesh = VoxelModels.Structure(def.type, Player.team);
            var size = VoxelModels.Size(mesh);
            var model = new GameObject("Model");
            model.transform.SetParent(ghost.transform, false);
            model.transform.localScale = Vector3.one * VoxelModels.VoxelSize;
            model.transform.localPosition = new Vector3(-size.x, 0f, -size.z) * (VoxelModels.VoxelSize * 0.5f);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterial = Match.Arena.Material;

            // Цветная плита под зданием: зелёная — можно, красная — нельзя
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(slab.GetComponent<Collider>());
            slab.transform.SetParent(ghost.transform, false);
            float cell = Match.Arena.VoxelSize;
            slab.transform.localScale = new Vector3(def.footprint.x * cell, 0.1f, def.footprint.y * cell);
            slab.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            ghostSlab = slab.GetComponent<Renderer>();
        }

        // ---------- Выбор здания ----------

        void Select(Structure structure)
        {
            selected = structure;
            selectionPanel.SetActive(structure != null);
        }

        void UpdateSelection()
        {
            if (selected == null)
            {
                if (selectionPanel.activeSelf)
                    selectionPanel.SetActive(false);
                return;
            }

            bool own = selected.Faction == Player;
            selectionTitle.text = own ? selected.Def.name : $"{selected.Def.name} (противник)";

            bool isHq = own && selected.Def.type == StructureType.Headquarters;
            hireButton.gameObject.SetActive(isHq);

            if (!selected.IsBuilt)
            {
                string who = selected.AssignedBuilder != null ? "строитель работает" : "ждёт строителя";
                selectionInfo.text = $"Строится: {selected.Progress:P0}, {who}";
            }
            else if (isHq)
            {
                hireButton.interactable = Player.CanAfford(StructureCatalog.BuilderCost, 0);
                selectionInfo.text = Player.buildersQueued > 0
                    ? $"Найм строителя: {Player.hireProgress:P0}, в очереди {Player.buildersQueued}"
                    : "Производит базовый ресурс";
            }
            else if (selected.Def.type == StructureType.Extractor)
            {
                selectionInfo.text = selected.CapturePoint == null ? "Добывает базовый ресурс" : "Добывает ценный ресурс";
            }
            else
            {
                selectionInfo.text = $"Прочность: {selected.Health:0}";
            }
        }

        void HireBuilder()
        {
            if (!Match.TryHireBuilder(Player, out var reason))
                Toast(reason);
        }

        void Toast(string text)
        {
            toastText.text = text;
            toastText.enabled = true;
            toastTimer = 2.5f;
        }

        static string Cost(StructureDef def) =>
            def.costValuable > 0 ? $"{def.costBase} + {def.costValuable} ценного" : $"{def.costBase}";

        // ---------- Сборка интерфейса ----------

        static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
                return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        void BuildCanvas()
        {
            var canvasObject = new GameObject("HUD Canvas");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            var root = canvasObject.transform;

            // Ресурсы сверху
            var top = MakePanel(root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 70f));
            resourcesText = MakeLabel(top, "", 34, TextAnchor.MiddleCenter);

            // Сообщения
            var toast = MakePanel(root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1100f, 70f));
            toast.GetComponent<Image>().color = Color.clear;
            toastText = MakeLabel(toast, "", 34, TextAnchor.MiddleCenter);
            toastText.enabled = false;

            // Кнопка «Строить»
            var buildButton = MakeButton(root, "Строить", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 30f), new Vector2(300f, 120f), AccentColor);
            buildButton.onClick.AddListener(() =>
            {
                CancelPlacing();
                buildMenu.SetActive(!buildMenu.activeSelf);
            });

            // Меню зданий над кнопкой
            var menu = MakePanel(root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 170f), new Vector2(460f, 10f));
            buildMenu = menu.gameObject;
            var layout = menu.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            menu.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var def in StructureCatalog.All)
            {
                if (!def.buildable)
                    continue;
                var captured = def;
                var button = MakeButton(menu, $"{def.name} · {Cost(def)}", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0f, 100f), ButtonColor);
                button.gameObject.AddComponent<LayoutElement>().preferredHeight = 100f;
                button.onClick.AddListener(() => StartPlacing(captured));
                buildButtons.Add((def, button));
            }
            buildMenu.SetActive(false);

            // Установка: подсказка и кнопки
            var bar = MakePanel(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(1000f, 210f));
            placementBar = bar.gameObject;
            var hint = MakePanel(bar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 80f));
            hint.GetComponent<Image>().color = Color.clear;
            placementHint = MakeLabel(hint, "", 32, TextAnchor.MiddleCenter);
            confirmButton = MakeButton(bar, "Поставить", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-235f, 20f), new Vector2(430f, 100f), AccentColor);
            confirmButton.onClick.AddListener(ConfirmPlacing);
            var cancelButton = MakeButton(bar, "Отмена", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(235f, 20f), new Vector2(430f, 100f), ButtonColor);
            cancelButton.onClick.AddListener(CancelPlacing);
            placementBar.SetActive(false);

            // Панель выбранного здания
            var panel = MakePanel(root, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(30f, 30f), new Vector2(640f, 300f));
            selectionPanel = panel.gameObject;
            var title = MakePanel(panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(-20f, 60f));
            title.GetComponent<Image>().color = Color.clear;
            selectionTitle = MakeLabel(title, "", 38, TextAnchor.MiddleLeft);
            var info = MakePanel(panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(-20f, 80f));
            info.GetComponent<Image>().color = Color.clear;
            selectionInfo = MakeLabel(info, "", 30, TextAnchor.UpperLeft);
            hireButton = MakeButton(panel, $"Нанять строителя · {StructureCatalog.BuilderCost}", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(600f, 100f), AccentColor);
            hireButton.onClick.AddListener(HireBuilder);
            selectionPanel.SetActive(false);
        }

        RectTransform MakePanel(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            go.GetComponent<Image>().color = PanelColor;
            return rect;
        }

        Text MakeLabel(Transform parent, string text, int size, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(16f, 4f);
            rect.offsetMax = new Vector2(-16f, -4f);
            var label = go.GetComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.alignment = anchor;
            label.color = Color.white;
            label.text = text;
            label.raycastTarget = false;
            return label;
        }

        Button MakeButton(Transform parent, string text, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Color color)
        {
            var pivot = new Vector2(anchorMin.x, anchorMin.y);
            var rect = MakePanel(parent, anchorMin, anchorMax, pivot, position, size);
            rect.name = text;
            rect.GetComponent<Image>().color = color;
            var button = rect.gameObject.AddComponent<Button>();
            MakeLabel(rect, text, 34, TextAnchor.MiddleCenter);
            return button;
        }
    }
}
