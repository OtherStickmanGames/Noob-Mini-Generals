using System;
using System.Collections;
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

        [Header("Вид установки (меняется на лету в Play mode)")]
        [SerializeField] BuildGridStyle gridStyle = new();
        [SerializeField] PlacementOutlineStyle outlineStyle = new();
        [SerializeField] PlacementSceneStyle sceneStyle = new();

        [Header("Выбранное здание")]
        [SerializeField] GameObject selectionPanel;
        [SerializeField] TMP_Text selectionTitle;
        [SerializeField] TMP_Text selectionInfo;
        [SerializeField] Button hireButton;
        [Tooltip("Заполнение полосы прогресса найма: ширина задаётся правым якорем")]
        [SerializeField] RectTransform hireProgressFill;
        [Tooltip("Строка «Оборона / Атака» — только у казарм")]
        [SerializeField] GameObject behaviorRow;
        [SerializeField] Button defendButton;
        [SerializeField] Button attackButton;

        [Header("Значки над казармами")]
        [Tooltip("Образец значка поведения: копируется по одному на каждые свои казармы")]
        [SerializeField] RectTransform barracksBadgeTemplate;
        [SerializeField] Color defendColor = new(0.22f, 0.42f, 0.72f, 1f);
        [SerializeField] Color attackColor = new(0.72f, 0.26f, 0.2f, 1f);
        [SerializeField] Color inactiveColor = new(0.22f, 0.25f, 0.30f, 1f);

        [Header("Полоски прочности")]
        [Tooltip("Образец полоски над бойцом или зданием: фон, дочерние Lag (след урона) и Fill (прочность)")]
        [SerializeField] RectTransform healthBarTemplate;
        [SerializeField] Color ownHealthColor = new(0.36f, 0.86f, 0.38f, 1f);
        [SerializeField] Color enemyHealthColor = new(0.93f, 0.3f, 0.25f, 1f);
        [Tooltip("Сколько секунд после урона полоска видна (у выбранного здания — всегда, пока оно повреждено)")]
        [SerializeField] float healthBarShowSeconds = 5f;
        [Tooltip("С какой скоростью догоняет прочность светлый след урона, доля в секунду")]
        [SerializeField] float healthLagSpeed = 0.6f;

        [Header("Конец боя")]
        [SerializeField] GameObject matchEndPanel;
        [SerializeField] TMP_Text matchEndTitle;
        [SerializeField] TMP_Text matchEndStats;
        [SerializeField] Button newMatchButton;
        [Tooltip("Спрятать итоги и посмотреть на поле")]
        [SerializeField] Button lookAroundButton;
        [Tooltip("Кнопка «Итоги боя» вместо «Строить», пока итоги спрятаны")]
        [SerializeField] Button resultsButton;
        [SerializeField] Color victoryColor = new(1f, 0.84f, 0.35f, 1f);
        [SerializeField] Color defeatColor = new(0.93f, 0.36f, 0.3f, 1f);
        [Tooltip("Через сколько секунд после разрушения главного здания показать итоги")]
        [SerializeField] float resultsDelay = 2.2f;

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

        // Значки поведения над своими казармами
        readonly Dictionary<Barracks, RectTransform> badges = new();
        readonly List<Barracks> badgeCleanup = new();

        // Полоски прочности: активные по цели и запас свободных
        class HealthBar
        {
            public RectTransform rect;
            public RectTransform fill;
            public RectTransform lag;
            public Image fillImage;
            public float lagValue;
            public bool used;
        }
        readonly Dictionary<IDamageable, HealthBar> healthBars = new();
        readonly Stack<HealthBar> freeHealthBars = new();
        readonly List<IDamageable> healthBarCleanup = new();

        // Конец боя: камера едет к разрушенному главному зданию, потом итоги
        bool matchEndHandled;
        float resultsTimer;

        // Высота панели выбранного здания со строкой поведения и без неё
        float selectionHeightWithBehavior;
        float BehaviorRowHeight => ((RectTransform)behaviorRow.transform).rect.height + 20f;

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
            hireButton.onClick.AddListener(HireForSelected);
            defendButton.onClick.AddListener(() => SetSelectedBehavior(BarracksBehavior.Defend));
            attackButton.onClick.AddListener(() => SetSelectedBehavior(BarracksBehavior.Attack));
            selectionHeightWithBehavior = ((RectTransform)selectionPanel.transform).sizeDelta.y;
            barracksBadgeTemplate.gameObject.SetActive(false);
            healthBarTemplate.gameObject.SetActive(false);

            newMatchButton.onClick.AddListener(() => StartCoroutine(NewMatchRoutine()));
            lookAroundButton.onClick.AddListener(() =>
            {
                matchEndPanel.SetActive(false);
                resultsButton.gameObject.SetActive(true);
            });
            resultsButton.onClick.AddListener(ShowResults);
            matchEndPanel.SetActive(false);
            resultsButton.gameObject.SetActive(false);

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
            BuildModeVisuals.SetActive(false);
            SelectionOutline.Clear();
            if (ghostMaterial != null)
                Destroy(ghostMaterial);
        }

        void Update()
        {
            if (Player == null)
                return;

            resourcesText.text = $"Базовый ресурс: {Player.BaseResource}     Ценный: {Player.Valuable}     " +
                                 $"Строители: {Player.builders.Count}     Бойцы: {Player.units.Count}     Точки: {OwnedPoints()}";

            UpdateMatchEnd();

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

            // Отладка: отряды противника у его ворот (пока нет ИИ противника)
            if (Input.GetKeyDown(KeyCode.V))
            {
                Match.DebugSpawnEnemySquad(3, BarracksBehavior.Attack);
                Toast("Отряд противника идёт в атаку (отладка)");
            }
            if (Input.GetKeyDown(KeyCode.B))
            {
                Match.DebugSpawnEnemySquad(3, BarracksBehavior.Defend);
                Toast("Отряд противника встал в оборону (отладка)");
            }
        }

        // ---------- Конец боя ----------

        void UpdateMatchEnd()
        {
            // Новая карта сгенерирована — интерфейс снова в режиме боя
            if (matchEndHandled && !Match.IsOver)
            {
                matchEndHandled = false;
                matchEndPanel.SetActive(false);
                resultsButton.gameObject.SetActive(false);
                buildToggle.gameObject.SetActive(true);
                return;
            }

            if (Match.IsOver && !matchEndHandled)
            {
                matchEndHandled = true;
                CancelPlacing();
                Select(null);
                buildMenu.SetActive(false);
                buildToggle.gameObject.SetActive(false);
                rtsCamera.GlideTo(Match.EndPoint);
                resultsTimer = resultsDelay;
            }

            if (matchEndHandled && resultsTimer > 0f)
            {
                resultsTimer -= Time.deltaTime;
                if (resultsTimer <= 0f)
                    ShowResults();
            }
        }

        void ShowResults()
        {
            var own = Player;
            var enemy = Match.Enemy;
            bool victory = Match.Winner == own;

            matchEndTitle.text = victory ? "Победа!" : "Поражение";
            matchEndTitle.color = victory ? victoryColor : defeatColor;

            int time = Mathf.FloorToInt(Match.MatchTime);
            matchEndStats.text =
                (victory ? "Главное здание противника разрушено" : "Ваше главное здание разрушено") + "\n\n" +
                $"Время боя: {time / 60}:{time % 60:00}\n" +
                $"Бойцов нанято: {own.unitsHired}, потеряно: {own.unitsLost}\n" +
                $"Уничтожено бойцов противника: {enemy.unitsLost}\n" +
                $"Зданий построено: {own.structuresBuilt}, потеряно: {own.structuresLost}\n" +
                $"Уничтожено зданий противника: {enemy.structuresLost}";

            newMatchButton.interactable = true;
            lookAroundButton.interactable = true;
            matchEndPanel.SetActive(true);
            resultsButton.gameObject.SetActive(false);
        }

        // Генерация карты на главном потоке замораживает кадр: сначала показываем, что идёт новая карта
        IEnumerator NewMatchRoutine()
        {
            newMatchButton.interactable = false;
            lookAroundButton.interactable = false;
            matchEndTitle.text = "Новая карта…";
            matchEndTitle.color = Color.white;
            matchEndStats.text = "Генерация займёт несколько секунд";
            yield return null;
            yield return null;
            Match.NewMatch();
        }

        // ---------- Полоски прочности ----------

        // Над повреждёнными бойцами и зданиями — полоска прочности (зелёная своя, красная чужая)
        // со светлым следом только что снятого урона; видна несколько секунд после урона
        void UpdateHealthBars()
        {
            foreach (var bar in healthBars.Values)
                bar.used = false;

            if (placingDef == null)
            {
                foreach (var faction in new[] { Match.Player, Match.Enemy })
                {
                    foreach (var s in faction.structures)
                        UpdateHealthBar(s);
                    foreach (var w in faction.walls)
                        UpdateHealthBar(w);
                    foreach (var u in faction.units)
                        UpdateHealthBar(u);
                }
            }

            healthBarCleanup.Clear();
            foreach (var (target, bar) in healthBars)
                if (!bar.used)
                    healthBarCleanup.Add(target);
            foreach (var target in healthBarCleanup)
            {
                var bar = healthBars[target];
                bar.rect.gameObject.SetActive(false);
                freeHealthBars.Push(bar);
                healthBars.Remove(target);
            }
        }

        void UpdateHealthBar(IDamageable target)
        {
            if (!Combat.IsAlive(target) || target.Health >= target.MaxHealth)
                return;
            bool recent = Time.time - target.LastDamageTime < healthBarShowSeconds;
            if (!recent && !ReferenceEquals(target, selected))
                return;

            // Здание и участок стены — полоска шире и над их верхом; боец — короткая над головой
            var area = target as IAreaTarget;
            var top = area != null
                ? area.transform.position + Vector3.up * (area.Height + 0.5f)
                : target.transform.position + Vector3.up * 2.4f;
            var screen = rtsCamera.Camera.WorldToScreenPoint(top);
            if (screen.z <= 0f)
                return;

            bool isNew = !healthBars.TryGetValue(target, out var bar);
            if (isNew)
            {
                bar = freeHealthBars.Count > 0 ? freeHealthBars.Pop() : CreateHealthBar();
                healthBars.Add(target, bar);
                bar.rect.gameObject.SetActive(true);
                bar.rect.sizeDelta = area != null
                    ? new Vector2(Mathf.Clamp(Mathf.Max(area.HalfExtents.x, area.HalfExtents.y) * 2f * 26f, 70f, 180f), 14f)
                    : new Vector2(50f, 9f);
                bar.fillImage.color = target.Faction == Player ? ownHealthColor : enemyHealthColor;
            }

            bar.used = true;
            bar.rect.position = screen;

            float value = Mathf.Clamp01(target.Health / target.MaxHealth);
            // Новая полоска — след стартует с полной, чтобы было видно первый урон
            if (isNew)
                bar.lagValue = 1f;
            bar.lagValue = value > bar.lagValue ? value : Mathf.Max(value, bar.lagValue - healthLagSpeed * Time.deltaTime);
            bar.fill.anchorMax = new Vector2(value, 1f);
            bar.lag.anchorMax = new Vector2(bar.lagValue, 1f);
        }

        HealthBar CreateHealthBar()
        {
            var rect = Instantiate(healthBarTemplate, healthBarTemplate.parent);
            rect.name = "Health Bar";
            // Сразу за образцом: под панелями и кнопками интерфейса
            rect.SetSiblingIndex(healthBarTemplate.GetSiblingIndex() + 1);
            var fill = (RectTransform)rect.Find("Fill");
            return new HealthBar
            {
                rect = rect,
                fill = fill,
                lag = (RectTransform)rect.Find("Lag"),
                fillImage = fill.GetComponent<Image>(),
            };
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
            // Бойцы — триггеры и тапом не выбираются (непрямое управление), их пропускаем
            if (Physics.Raycast(ray, out var anyHit, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                anyHit.collider.TryGetComponent<Structure>(out var structure))
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
            gridOverlay.ApplyStyle(gridStyle);
            gridOverlay.Show(Match.Grid, Player, def.rule, center);
            SetBuildFade(true);

            SetGhostMin(FindInitialSpot(def, center));

            // Здание появилось за краем кадра (шахта на дальней точке) — камера плавно едет к нему
            var view = rtsCamera.Camera.WorldToViewportPoint(ghost.transform.position);
            if (view.z <= 0f || view.x < 0.1f || view.x > 0.9f || view.y < 0.1f || view.y > 0.8f)
                rtsCamera.GlideTo(ghost.transform.position);
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
            // Остальное — ближайшее к центру экрана место, где здание хорошо видно
            else if (FindVisibleSpot(def, center, out var spot))
            {
                return spot;
            }

            // Подходящего места нет — хотя бы не внутри другого здания
            Match.Grid.FindNearestFree(def, center, gridOverlay.Apothem * 2, out var free);
            return free;
        }

        /// <summary>
        /// Место для нового здания, как в Clash of Clans: ближайшее к центру экрана, где его можно поставить,
        /// оно целиком в кадре (с местом под кнопки сверху), ничем не закрыто от камеры и не стоит
        /// вплотную к другим зданиям. Нет такого — сначала отказываемся от зазора, потом от видимости.
        /// </summary>
        bool FindVisibleSpot(StructureDef def, int2 center, out int2 result)
        {
            var start = BuildGrid.MinFromCenter(def, center);
            int2 visibleSpot = default, anySpot = default;
            bool hasVisible = false, hasAny = false;

            for (int r = 0; r <= gridOverlay.Apothem; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (math.max(math.abs(dx), math.abs(dz)) != r)
                            continue;

                        var min = start + new int2(dx, dz);
                        if (!Match.Grid.CanPlace(Player, def, min, out _, out _))
                            continue;
                        if (!hasAny)
                        {
                            anySpot = min;
                            hasAny = true;
                        }

                        bool clearance = Match.Grid.HasClearance(min, def.footprint, PlacementClearance);
                        if (!clearance && hasVisible)
                            continue;
                        if (!IsSpotVisible(def, min))
                            continue;

                        if (clearance)
                        {
                            result = min;
                            return true;
                        }
                        visibleSpot = min;
                        hasVisible = true;
                    }
                }
            }

            result = hasVisible ? visibleSpot : anySpot;
            return hasVisible || hasAny;
        }

        // Сколько клеток зазора оставлять до других зданий при выборе места
        const int PlacementClearance = 1;

        /// <summary>
        /// Здание на этом месте целиком в кадре (сверху остаётся место под кнопки «Строить / Отмена»)
        /// и его верх не закрыт от камеры ни стеной, ни рельефом, ни другим зданием
        /// </summary>
        bool IsSpotVisible(StructureDef def, int2 min)
        {
            var cam = rtsCamera.Camera;
            var origin = cam.transform.position;
            var ground = Match.CellCenter(min, def.footprint);
            float height = VoxelModels.Size(VoxelModels.Structure(def.type, Player.team)).y * VoxelModels.VoxelSize;
            float cell = Match.Arena.VoxelSize;
            float hx = def.footprint.x * cell * 0.5f - 0.1f;
            float hz = def.footprint.y * cell * 0.5f - 0.1f;
            var top = ground + Vector3.up * height;

            // Место под кнопки над зданием
            var buttons = cam.WorldToViewportPoint(top + Vector3.up * 0.6f);
            if (buttons.z <= 0f || buttons.y > 0.85f)
                return false;

            Span<Vector3> points = stackalloc Vector3[]
            {
                top,
                top + new Vector3(-hx, 0f, -hz),
                top + new Vector3(hx, 0f, -hz),
                top + new Vector3(-hx, 0f, hz),
                top + new Vector3(hx, 0f, hz),
                ground + Vector3.up * (height * 0.5f),
            };

            foreach (var p in points)
            {
                var vp = cam.WorldToViewportPoint(p);
                if (vp.z <= 0f || vp.x < 0.06f || vp.x > 0.94f || vp.y < 0.08f || vp.y > 0.85f)
                    return false;

                var toPoint = p - origin;
                float distance = toPoint.magnitude;
                if (Physics.Raycast(origin, toPoint / distance, distance - 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return false;
            }
            return true;
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

            bool affordable = Player.CanAfford(placingDef.costBase, placingDef.costValuable);
            confirmButton.interactable = placingValid && affordable;
            placementHint.text = !placingValid ? reason
                : affordable ? $"{placingDef.name} · {Cost(placingDef)}"
                : "Не хватает ресурсов";
        }

        // Кнопки едут над зданием
        void LateUpdate()
        {
            if (Player != null)
            {
                UpdateBadges();
                UpdateHealthBars();
            }

            if (placingDef == null || ghost == null || !ghost.activeSelf)
                return;

            // Сетка застройки идёт за центром экрана; вид берётся из инспектора каждый кадр,
            // чтобы настройки было видно сразу
            gridOverlay.ApplyStyle(gridStyle);
            if (ScreenCenterCell(out var centerCell))
                gridOverlay.SetCenter(centerCell);

            BuildModeVisuals.Apply(Match.StructureMaterial, sceneStyle, true);
            BuildModeVisuals.Apply(gridOverlay.WallsMaterial, sceneStyle, true);

            SelectionOutline.Color = placingValid ? outlineStyle.validColor : outlineStyle.invalidColor;
            SelectionOutline.WidthAt1080 = outlineStyle.widthAt1080;
            SelectionOutline.HiddenLineAlpha = outlineStyle.hiddenLineAlpha;
            SelectionOutline.HiddenFillAlpha = outlineStyle.hiddenFillAlpha;

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
            SelectionOutline.Clear();
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

            // Свой материал: при установке не прозрачный, как стены и здания; модель в своих цветах,
            // можно ли ставить — показывает контур (SelectionOutline)
            if (ghostMaterial == null)
            {
                ghostMaterial = BuildModeVisuals.CreateVariant(Match.Arena.Material, "Ghost");
            }
            var ghostRenderer = model.AddComponent<MeshRenderer>();
            ghostRenderer.sharedMaterial = ghostMaterial;
            SelectionOutline.Set(ghostRenderer, outlineStyle.invalidColor);
        }

        // ---------- Выбор здания ----------


        // Режим установки: стены и готовые здания полупрозрачные
        void SetBuildFade(bool on)
        {
            BuildModeVisuals.SetActive(on);
            BuildModeVisuals.Apply(Match.StructureMaterial, sceneStyle, on);
            if (on)
                BuildModeVisuals.Apply(gridOverlay.WallsMaterial, sceneStyle, true);
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
            // Прочность — справа от названия, цветом стороны
            var hpColor = ColorUtility.ToHtmlStringRGB(own ? ownHealthColor : enemyHealthColor);
            selectionTitle.text = (own ? selected.Def.name : $"{selected.Def.name} (противник)") +
                                  $"   <size=75%><color=#{hpColor}>{Mathf.CeilToInt(selected.Health)} / {selected.MaxHealth:0}</color></size>";

            bool isHq = own && selected.Def.type == StructureType.Headquarters;
            var barracks = own ? selected.Barracks : null;

            // Найм: у главного здания — строители, у казарм — пехота
            bool canHire = isHq || barracks != null;
            hireButton.gameObject.SetActive(canHire);
            hireProgressFill.transform.parent.gameObject.SetActive(canHire);
            SetBehaviorRowVisible(barracks != null);

            if (!selected.IsBuilt)
            {
                string who = selected.AssignedBuilder != null ? "строитель работает" : "ждёт строителя";
                selectionInfo.text = $"Строится: {selected.Progress:P0}, {who}";
                hireButton.interactable = false;
                SetHireProgress(0f);
                if (barracks != null)
                    SetButtonText(hireButton, $"Нанять: {UnitCatalog.InfantryName.ToLower()} · {UnitCatalog.InfantryCost}");
            }
            else if (isHq)
            {
                SetButtonText(hireButton, $"Нанять строителя · {StructureCatalog.BuilderCost}");
                hireButton.interactable = Player.CanAfford(StructureCatalog.BuilderCost, 0);
                SetHireProgress(Player.buildersQueued > 0 ? Player.hireProgress : 0f);
                selectionInfo.text = Player.buildersQueued > 0
                    ? $"Найм строителя, в очереди {Player.buildersQueued}"
                    : "Производит базовый ресурс";
            }
            else if (barracks != null)
            {
                SetButtonText(hireButton, $"Нанять: {UnitCatalog.InfantryName.ToLower()} · {UnitCatalog.InfantryCost}");
                hireButton.interactable = barracks.Queued < UnitCatalog.BarracksQueueLimit &&
                                          Player.CanAfford(UnitCatalog.InfantryCost, 0);
                SetHireProgress(barracks.Queued > 0 ? barracks.Progress : 0f);

                string behavior = barracks.Behavior == BarracksBehavior.Defend
                    ? "Оборона: держат пост у ворот"
                    : "Атака: идут на врага";
                selectionInfo.text = $"Бойцов: {barracks.Units.Count}   В очереди: {barracks.Queued}/{UnitCatalog.BarracksQueueLimit}\n{behavior}";
            }
            else if (selected.Def.type == StructureType.Extractor)
            {
                selectionInfo.text = "Добывает базовый ресурс";
            }
            else if (selected.Def.type == StructureType.Mine)
            {
                selectionInfo.text = selected.CapturePoint.Owner == selected.Faction.team
                    ? "Добывает ценный ресурс"
                    : "Точка потеряна — добыча стоит";
            }
            else if (selected.Turret != null)
            {
                selectionInfo.text = selected.Turret.Target != null
                    ? "Ведёт огонь"
                    : $"Стреляет по врагам в радиусе {WeaponCatalog.Cannon.range:0} м";
            }
            else if (selected.Def.type == StructureType.Headquarters)
            {
                selectionInfo.text = "Разрушить — значит победить";
            }
            else
            {
                selectionInfo.text = "Постройка противника";
            }

            if (barracks != null)
            {
                bool defend = barracks.Behavior == BarracksBehavior.Defend;
                defendButton.image.color = defend ? defendColor : inactiveColor;
                attackButton.image.color = defend ? inactiveColor : attackColor;
            }
        }

        void SetHireProgress(float progress)
        {
            hireProgressFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        }

        // Строка поведения есть только у казарм; без неё панель ниже, верхние строки опускаются
        void SetBehaviorRowVisible(bool visible)
        {
            if (behaviorRow.activeSelf == visible)
                return;
            behaviorRow.SetActive(visible);
            var rect = (RectTransform)selectionPanel.transform;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, visible ? selectionHeightWithBehavior : selectionHeightWithBehavior - BehaviorRowHeight);
        }

        void HireForSelected()
        {
            if (selected == null)
                return;

            string reason;
            bool ok = selected.Barracks != null
                ? selected.Barracks.TryHire(out reason)
                : Match.TryHireBuilder(Player, out reason);
            if (!ok)
                Toast(reason);
        }

        void SetSelectedBehavior(BarracksBehavior behavior)
        {
            if (selected != null && selected.Barracks != null && selected.Faction == Player)
                selected.Barracks.SetBehavior(behavior);
        }

        // ---------- Значки над казармами ----------

        // Над каждыми своими казармами — плашка с текущим поведением; при установке здания скрыты
        void UpdateBadges()
        {
            foreach (var s in Player.structures)
            {
                if (s == null || s.Barracks == null || badges.ContainsKey(s.Barracks))
                    continue;
                var badge = Instantiate(barracksBadgeTemplate, barracksBadgeTemplate.parent);
                badge.name = "Badge " + s.name;
                // Сразу за образцом: под панелями и кнопками интерфейса
                badge.SetSiblingIndex(barracksBadgeTemplate.GetSiblingIndex() + 1);
                badges.Add(s.Barracks, badge);
            }

            var cam = rtsCamera.Camera;
            badgeCleanup.Clear();
            foreach (var (barracks, badge) in badges)
            {
                if (barracks == null)
                {
                    badgeCleanup.Add(barracks);
                    if (badge != null)
                        Destroy(badge.gameObject);
                    continue;
                }

                var top = barracks.transform.position + Vector3.up * (barracks.Structure.Height + 0.8f);
                var screen = cam.WorldToScreenPoint(top);
                bool visible = placingDef == null && screen.z > 0f;
                if (badge.gameObject.activeSelf != visible)
                    badge.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                badge.position = screen;
                bool defend = barracks.Behavior == BarracksBehavior.Defend;
                badge.GetComponent<Image>().color = defend ? defendColor : attackColor;
                badge.GetComponentInChildren<TMP_Text>().text = defend ? "Оборона" : "Атака";
            }
            foreach (var b in badgeCleanup)
                badges.Remove(b);
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
