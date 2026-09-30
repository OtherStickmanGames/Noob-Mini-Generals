using System.Collections.Generic;
using Unity.Mathematics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Generals
{
    /// <summary>
    /// Интерфейс боя. Вёрстка — в префабе Assets/Gameplay/Prefabs/HUD.prefab
    /// (собирается меню Tools/Voxel Arena/Rebuild HUD Prefab, дальше правится руками).
    /// Здесь только логика: ресурсы, меню построек, установка здания, панель выбранного здания.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        [SerializeField] RtsCamera rtsCamera;

        [Header("Верх")]
        [SerializeField] TMP_Text resourcesText;
        [SerializeField] TMP_Text toastText;

        [Header("Строительство")]
        [SerializeField] Button buildToggle;
        [SerializeField] GameObject buildMenu;
        [SerializeField] Transform buildMenuContent;
        [Tooltip("Образец кнопки здания: копируется по одной на каждое здание из каталога")]
        [SerializeField] Button buildButtonTemplate;

        [Header("Установка")]
        [Tooltip("Кнопки «Строить / Отмена», которые висят над зданием при установке")]
        [SerializeField] RectTransform placementButtons;
        [SerializeField] Button confirmButton;
        [SerializeField] Button cancelButton;
        [Tooltip("Строка под ресурсами: название и цена или почему здесь нельзя")]
        [SerializeField] GameObject placementHintPanel;
        [SerializeField] TMP_Text placementHint;

        [Header("Выбранное здание")]
        [SerializeField] GameObject selectionPanel;
        [SerializeField] TMP_Text selectionTitle;
        [SerializeField] TMP_Text selectionInfo;
        [SerializeField] Button hireButton;

        public RtsCamera RtsCamera
        {
            get => rtsCamera;
            set => rtsCamera = value;
        }

        readonly List<(StructureDef def, Button button)> buildButtons = new();
        float toastTimer;
        Structure selected;

        // Установка здания
        StructureDef placingDef;
        int2 placingMin;
        bool placingValid;
        bool placingHasCell;
        int2 grabOffset;
        GameObject ghost;
        Material ghostMaterial;
        float ghostHeight;
        BuildGridOverlay gridOverlay;

        MatchManager Match => MatchManager.Instance;
        Faction Player => Match.Player;

        void Awake()
        {
            rtsCamera.Tapped += Camera_Tapped;
            rtsCamera.TryBeginObjectDrag = TryGrabGhost;
            rtsCamera.ObjectDragged += Ghost_Dragged;
            rtsCamera.ObjectDragEnded += Ghost_Dragged;

            buildToggle.onClick.AddListener(() =>
            {
                CancelPlacing();
                buildMenu.SetActive(!buildMenu.activeSelf);
            });
            confirmButton.onClick.AddListener(ConfirmPlacing);
            cancelButton.onClick.AddListener(CancelPlacing);
            hireButton.onClick.AddListener(HireBuilder);
            SetButtonText(hireButton, $"Нанять строителя · {StructureCatalog.BuilderCost}");

            buildButtonTemplate.gameObject.SetActive(false);
            foreach (var def in StructureCatalog.All)
            {
                if (!def.buildable)
                    continue;
                var captured = def;
                var button = Instantiate(buildButtonTemplate, buildMenuContent);
                button.gameObject.SetActive(true);
                button.name = def.name;
                SetButtonText(button, $"{def.name} · {Cost(def)}");
                button.onClick.AddListener(() => StartPlacing(captured));
                buildButtons.Add((def, button));
            }

            buildMenu.SetActive(false);
            placementButtons.gameObject.SetActive(false);
            placementHintPanel.SetActive(false);
            selectionPanel.SetActive(false);
            toastText.enabled = false;
        }

        void OnDestroy()
        {
            rtsCamera.Tapped -= Camera_Tapped;
            rtsCamera.ObjectDragged -= Ghost_Dragged;
            rtsCamera.ObjectDragEnded -= Ghost_Dragged;
            rtsCamera.TryBeginObjectDrag = null;
            SetBuildFade(false);
            if (ghostMaterial != null)
                Destroy(ghostMaterial);
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

            if (placingDef != null && (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)))
                CancelPlacing();

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
                if (placingDef != null)
                    gridOverlay.MarkDirty();
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

        static void SetButtonText(Button button, string text)
        {
            button.GetComponentInChildren<TMP_Text>().text = text;
        }

        // ---------- Касания ----------

        void Camera_Tapped(Vector2 screen)
        {
            if (Player == null)
                return;

            if (placingDef != null)
            {
                // Тап по земле переносит здание сюда
                if (RaycastTerrain(screen, out var hit))
                    SetGhostMin(BuildGrid.MinFromCenter(placingDef, CellAt(hit)));
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

        int2 CellAt(RaycastHit hit)
        {
            float3 voxel = Match.Arena.WorldToVoxel(hit.point - hit.normal * 0.05f);
            return new int2((int)math.floor(voxel.x), (int)math.floor(voxel.z));
        }

        // Выбрал здание — оно сразу стоит на ближайшем подходящем месте, дальше его можно тащить
        void StartPlacing(StructureDef def)
        {
            CancelPlacing();
            Select(null);
            buildMenu.SetActive(false);

            placingDef = def;
            placingHasCell = false;
            placingValid = false;
            CreateGhost(def);

            placementButtons.gameObject.SetActive(true);
            placementHintPanel.SetActive(true);

            if (!ScreenCenterCell(out var center))
                center = Player.headquarters != null
                    ? Player.headquarters.MinCell + Player.headquarters.Def.footprint / 2
                    : int2.zero;

            if (gridOverlay == null)
                gridOverlay = BuildGridOverlay.Create(Match.Arena);
            gridOverlay.Show(Match.Grid, Player, def.rule, center);
            SetBuildFade(true);

            SetGhostMin(FindInitialSpot(def, center));
        }

        /// <summary>Клетка под центром экрана</summary>
        bool ScreenCenterCell(out int2 cell)
        {
            cell = default;
            if (!RaycastTerrain(new Vector2(Screen.width, Screen.height) * 0.5f, out var hit))
                return false;
            cell = CellAt(hit);
            return true;
        }

        int2 FindInitialSpot(StructureDef def, int2 center)
        {
            // Шахта — на ближайшую свою точку захвата
            if (def.rule == PlacementRule.Deposit)
            {
                int2 best = default;
                int bestDistance = int.MaxValue;
                foreach (var r in Match.Arena.Layout.resources)
                {
                    var min = BuildGrid.MinFromCenter(def, r.cell);
                    if (!Match.Grid.CanPlace(Player, def, min, out _, out _))
                        continue;
                    var delta = r.cell - center;
                    int d = delta.x * delta.x + delta.y * delta.y;
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = min;
                    }
                }
                if (bestDistance != int.MaxValue)
                    return best;
            }
            // Остальное — ближайшее подходящее место от центра экрана
            else if (Match.Grid.FindNearest(Player, def, center, gridOverlay.apothem, out var spot))
            {
                return spot;
            }

            // Подходящего места нет — хотя бы не внутри другого здания
            Match.Grid.FindNearestFree(def, center, gridOverlay.apothem * 2, out var free);
            return free;
        }

        // Нажатие на здание (или рядом, на клетку) при установке — начинаем его тащить
        bool TryGrabGhost(Vector2 screen)
        {
            if (placingDef == null || !placingHasCell || !RaycastTerrain(screen, out var hit))
                return false;

            var cell = CellAt(hit);
            var local = cell - placingMin;
            bool inside = local.x >= -1 && local.y >= -1 && local.x <= placingDef.footprint.x && local.y <= placingDef.footprint.y;
            if (inside)
                grabOffset = local;
            return inside;
        }

        void Ghost_Dragged(Vector2 screen)
        {
            if (placingDef != null && RaycastTerrain(screen, out var hit))
                SetGhostMin(CellAt(hit) - grabOffset);
        }

        void SetGhostMin(int2 min)
        {
            // Шахта прилипает к ближайшей точке захвата
            if (placingDef.rule == PlacementRule.Deposit &&
                Match.Grid.SnapToResource(min + placingDef.footprint / 2, 4, out var resource))
                min = BuildGrid.MinFromCenter(placingDef, resource);

            placingMin = min;
            placingHasCell = true;
            placingValid = Match.Grid.CanPlace(Player, placingDef, placingMin, out var reason, out _);

            ghost.SetActive(true);
            ghost.transform.position = Match.CellCenter(placingMin, placingDef.footprint);
            ghostMaterial.SetColor(TintId, placingValid ? new Color(0.25f, 0.95f, 0.3f, 0.45f) : new Color(1f, 0.2f, 0.15f, 0.55f));

            bool affordable = Player.CanAfford(placingDef.costBase, placingDef.costValuable);
            confirmButton.interactable = placingValid && affordable;
            placementHint.text = !placingValid ? reason
                : affordable ? $"{placingDef.name} · {Cost(placingDef)}"
                : "Не хватает ресурсов";
        }

        // Кнопки едут над зданием
        void LateUpdate()
        {
            if (placingDef == null || ghost == null || !ghost.activeSelf)
                return;

            // Сетка застройки идёт за центром экрана
            if (ScreenCenterCell(out var centerCell))
                gridOverlay.SetCenter(centerCell);

            var top = ghost.transform.position + Vector3.up * (ghostHeight + 0.6f);
            var screen = rtsCamera.Camera.WorldToScreenPoint(top);
            bool visible = screen.z > 0f;
            if (placementButtons.gameObject.activeSelf != visible)
                placementButtons.gameObject.SetActive(visible);
            if (visible)
                placementButtons.position = screen;
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
            placementButtons.gameObject.SetActive(false);
            placementHintPanel.SetActive(false);
            if (gridOverlay != null)
                gridOverlay.Hide();
            SetBuildFade(false);
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
            ghostHeight = size.y * VoxelModels.VoxelSize;

            // Свой материал: не прозрачный и подкрашивается зелёным или красным
            if (ghostMaterial == null)
            {
                ghostMaterial = new Material(Match.Arena.Material) { name = "Ghost" };
                ghostMaterial.SetFloat("_FadeWalls", 0f);
                ghostMaterial.SetFloat("_FadeWhole", 0f);
            }
            model.AddComponent<MeshRenderer>().sharedMaterial = ghostMaterial;
        }

        // ---------- Выбор здания ----------

        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int BuildFadeId = Shader.PropertyToID("_VoxelBuildFade");

        // Режим установки: стены и готовые здания полупрозрачные, чтобы было видно сетку
        static void SetBuildFade(bool on)
        {
            Shader.SetGlobalFloat(BuildFadeId, 0.1f);
            if (on)
                Shader.EnableKeyword("_VOXEL_BUILD_FADE");
            else
                Shader.DisableKeyword("_VOXEL_BUILD_FADE");
        }

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
                selectionInfo.text = "Добывает базовый ресурс";
            }
            else if (selected.Def.type == StructureType.Mine)
            {
                selectionInfo.text = selected.CapturePoint.Owner == Player.team
                    ? "Добывает ценный ресурс"
                    : "Точка потеряна — добыча стоит";
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
    }
}
