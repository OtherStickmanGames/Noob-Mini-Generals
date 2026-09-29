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
        [SerializeField] GameObject placementBar;
        [SerializeField] TMP_Text placementHint;
        [SerializeField] Button confirmButton;
        [SerializeField] Button cancelButton;

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
        GameObject ghost;
        Renderer ghostSlab;

        MatchManager Match => MatchManager.Instance;
        Faction Player => Match.Player;

        void Awake()
        {
            rtsCamera.Tapped += Camera_Tapped;

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
            placementBar.SetActive(false);
            selectionPanel.SetActive(false);
            toastText.enabled = false;
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
    }
}
