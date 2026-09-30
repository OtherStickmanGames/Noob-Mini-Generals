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

        [Header("Вооружение (казармы)")]
        [Tooltip("Строка выбора вооружения новых отрядов — над «Оборона / Атака»")]
        [SerializeField] GameObject weaponRow;
        [Tooltip("Образец кнопки вооружения: копируется на «Винтовки» и каждое открытое спецоружие")]
        [SerializeField] Button weaponButtonTemplate;
        [SerializeField] Color weaponSelectedColor = new(0.24f, 0.52f, 0.28f, 1f);

        [Header("Оружейная")]
        [Tooltip("Список спецоружия над панелью здания — только у своей оружейной")]
        [SerializeField] GameObject armouryPanel;
        [SerializeField] TMP_Text armouryHeader;
        [Tooltip("Образец строки оружия: дочерние Label (название, роль, цены) и Research (кнопка)")]
        [SerializeField] RectTransform armouryRowTemplate;
        [Tooltip("Строка «Изучить … вместо:» с кнопками Option 0, Option 1 и Cancel — когда открыто уже два")]
        [SerializeField] RectTransform armouryReplaceRow;
        [SerializeField] Color knownWeaponRowColor = new(0.24f, 0.52f, 0.28f, 0.35f);

        [Header("Пункт подкрепления")]
        [Tooltip("Список отрядов над панелью здания — только у своего пункта подкрепления")]
        [SerializeField] GameObject reinforcePanel;
        [Tooltip("Список отрядов с прокруткой; строки — в его Content")]
        [SerializeField] ScrollRect reinforceList;
        [Tooltip("Образец строки отряда: дочерние Label (текст) и Reinforce (кнопка «Пополнить»)")]
        [SerializeField] RectTransform reinforceRowTemplate;
        [Tooltip("Строка вместо списка, когда отрядов нет")]
        [SerializeField] TMP_Text reinforceNote;
        [Tooltip("Сколько строк видно без прокрутки")]
        [SerializeField] int reinforceMaxRows = 6;

        [Header("Значки над казармами")]
        [Tooltip("Образец значка поведения: копируется по одному на каждые свои казармы")]
        [SerializeField] RectTransform barracksBadgeTemplate;
        [SerializeField] Color defendColor = new(0.22f, 0.42f, 0.72f, 1f);
        [SerializeField] Color attackColor = new(0.72f, 0.26f, 0.2f, 1f);
        [SerializeField] Color inactiveColor = new(0.22f, 0.25f, 0.30f, 1f);
        [Tooltip("Кнопка найма, когда в казармах включён постоянный найм")]
        [SerializeField] Color repeatHireColor = new(0.72f, 0.52f, 0.16f, 1f);

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

        [Header("Предупреждения")]
        [Tooltip("Цвет сообщения «Противник идёт в атаку»")]
        [SerializeField] Color warningColor = new(1f, 0.45f, 0.35f, 1f);

        [Header("Платформа")]
        [Tooltip("Для какой платформы сверстан этот интерфейс")]
        [SerializeField] HudLayout layout = HudLayout.PC;
        [Tooltip("Интерфейс для телефона (только у ПК-интерфейса): на телефоне ПК-интерфейс заменяется им")]
        [SerializeField] GameHud mobileVariant;
        [Tooltip("Какой интерфейс показывать (только у ПК-интерфейса). Авто — по платформе: телефон, " +
                 "в том числе Device Simulator, — мобильный. Mobile — проверить мобильный в редакторе")]
        [SerializeField] HudChoice choice = HudChoice.Auto;

        public enum HudLayout { PC, Mobile }
        public enum HudChoice { Auto, PC, Mobile }

        public HudLayout Layout => layout;

        // ПК-интерфейс заменил себя мобильным и ничего не делает до уничтожения
        bool replaced;

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
        int lastWaveWarned;
        float resultsTimer;

        // Высота панели выбранного здания со строкой поведения и без неё
        float selectionHeightWithBehavior;
        float BehaviorRowHeight => ((RectTransform)behaviorRow.transform).rect.height + 20f;

        MatchManager Match => MatchManager.Instance;
        Faction Player => Match.Player;

        void Awake()
        {
            // На телефоне ПК-интерфейс из сцены заменяет себя мобильным (сцену менять не нужно)
            if (layout == HudLayout.PC && mobileVariant != null && WantsMobile())
            {
                replaced = true;
                var mobile = Instantiate(mobileVariant);
                mobile.name = mobileVariant.name;
                Destroy(gameObject);
                return;
            }

            // Интерфейс создан из кода (мобильный вместо ПК) — камера боя берётся из сцены
            if (rtsCamera == null)
                rtsCamera = FindObjectOfType<RtsCamera>();

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
            // Правая кнопка / долгое нажатие по найму в казармах — постоянный найм
            hirePressExtras = hireButton.gameObject.AddComponent<ButtonPressExtras>();
            hirePressExtras.Secondary += ToggleRepeatForSelected;
            hireButtonColor = hireButton.image.color;
            defendButton.onClick.AddListener(() => SetSelectedBehavior(BarracksBehavior.Defend));
            attackButton.onClick.AddListener(() => SetSelectedBehavior(BarracksBehavior.Attack));
            selectionHeightWithBehavior = ((RectTransform)selectionPanel.transform).sizeDelta.y;
            barracksBadgeTemplate.gameObject.SetActive(false);
            healthBarTemplate.gameObject.SetActive(false);
            reinforcePanel.SetActive(false);
            reinforceRowTemplate.gameObject.SetActive(false);
            weaponButtonTemplate.gameObject.SetActive(false);
            armouryPanel.SetActive(false);
            armouryRowTemplate.gameObject.SetActive(false);
            armouryReplaceRow.gameObject.SetActive(false);
            armouryRowColor = armouryRowTemplate.GetComponent<Image>().color;
            for (int k = 0; k < WeaponCatalog.MaxKnownSpecials; k++)
            {
                int option = k;
                armouryReplaceRow.Find("Option " + k).GetComponent<Button>().onClick.AddListener(() => ReplaceClicked(option));
            }
            armouryReplaceRow.Find("Cancel").GetComponent<Button>().onClick.AddListener(() => pendingResearch = null);

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

        bool WantsMobile() => choice switch
        {
            HudChoice.Mobile => true,
            HudChoice.PC => false,
            _ => Application.isMobilePlatform,
        };

        void OnDestroy()
        {
            if (replaced)
                return;
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
            if (replaced || Player == null)
                return;

            resourcesText.text = $"Базовый ресурс: {Player.BaseResource}     Ценный: {Player.Valuable}     " +
                                 $"Строители: {Player.builders.Count}     Бойцы: {Player.units.Count}     Точки: {OwnedPoints()}";

            UpdateMatchEnd();

            // Противник пошёл волной — предупредить заметно (красным и дольше обычного сообщения)
            var ai = Match.EnemyAI;
            if (ai.WaveNumber != lastWaveWarned)
            {
                if (ai.WaveNumber > lastWaveWarned && !Match.IsOver)
                    Toast($"Противник идёт в атаку! Волна {ai.WaveNumber}", warningColor, 4f);
                lastWaveWarned = ai.WaveNumber;
            }

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
                Match.DebugSpawnEnemySquad(BarracksBehavior.Attack);
                Toast("Отряд противника идёт в атаку (отладка)");
            }
            if (Input.GetKeyDown(KeyCode.B))
            {
                Match.DebugSpawnEnemySquad(BarracksBehavior.Defend);
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
                // Масштаб — по высоте образца: у мобильного интерфейса полоски крупнее
                float k = healthBarTemplate.sizeDelta.y / 14f;
                bar.rect.sizeDelta = k * (area != null
                    ? new Vector2(Mathf.Clamp(Mathf.Max(area.HalfExtents.x, area.HalfExtents.y) * 2f * 26f, 70f, 180f), 14f)
                    : new Vector2(50f, 9f));
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
            if (replaced)
                return;
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
            var reinforcement = own ? selected.ReinforcementPoint : null;
            UpdateReinforcePanel(reinforcement);
            var armoury = own ? selected.Armoury : null;
            UpdateArmouryPanel(armoury);
            // Прочность — справа от названия, цветом стороны
            var hpColor = ColorUtility.ToHtmlStringRGB(own ? ownHealthColor : enemyHealthColor);
            selectionTitle.text = (own ? selected.Def.name : $"{selected.Def.name} (противник)") +
                                  $"   <size=75%><color=#{hpColor}>{Mathf.CeilToInt(selected.Health)} / {selected.MaxHealth:0}</color></size>";

            bool isHq = own && selected.Def.type == StructureType.Headquarters;
            var barracks = own ? selected.Barracks : null;

            // Найм: у главного здания — строители, у казарм — пехота
            bool canHire = isHq || barracks != null;
            hireButton.gameObject.SetActive(canHire);
            // Полоса — прогресс найма; у пункта подкрепления — выход очередного бойца, у оружейной — исследование
            hireProgressFill.transform.parent.gameObject.SetActive(canHire || reinforcement != null || armoury != null);
            SetBarracksRowsVisible(barracks != null);
            if (barracks != null)
                UpdateWeaponRow(barracks);
            // Постоянный найм — кнопка другого цвета (переключается даже без денег: закажет, когда появятся)
            bool repeat = barracks != null && barracks.Repeat;
            hireButton.image.color = repeat ? repeatHireColor : hireButtonColor;

            if (!selected.IsBuilt)
            {
                string who = selected.AssignedBuilder != null ? "строитель работает" : "ждёт строителя";
                selectionInfo.text = $"Строится: {selected.Progress:P0}, {who}";
                hireButton.interactable = false;
                SetHireProgress(0f);
                if (barracks != null)
                    SetButtonText(hireButton, HireSquadText(repeat, barracks.Weapon));
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
                SetButtonText(hireButton, HireSquadText(repeat, barracks.Weapon));
                hireButton.interactable = barracks.Queued < UnitCatalog.BarracksQueueLimit &&
                                          Player.CanAfford(WeaponCatalog.SquadCost(barracks.Weapon), 0);
                SetHireProgress(barracks.Queued > 0 ? barracks.Progress : 0f);

                string behavior = barracks.Behavior == BarracksBehavior.Defend
                    ? "Оборона: держат пост у ворот"
                    : "Атака: идут на врага";
                selectionInfo.text = $"Отрядов: {barracks.Squads.Count} (бойцов {barracks.UnitCount})   " +
                                     $"В очереди: {barracks.Queued}/{UnitCatalog.BarracksQueueLimit}\n{behavior}";
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
            else if (reinforcement != null)
            {
                SetHireProgress(reinforcement.Queued > 0 ? reinforcement.Progress : 0f);
                selectionInfo.text = reinforcement.Queued > 0
                    ? $"Пополнение: в очереди бойцов {reinforcement.Queued}"
                    : $"Пополняет отряды: {UnitCatalog.ReinforceCost} за бойца, спецбоец дороже";
            }
            else if (armoury != null)
            {
                SetHireProgress(armoury.Researching.HasValue ? armoury.Progress : 0f);
                selectionInfo.text = armoury.Researching.HasValue
                    ? $"Изучается: {WeaponCatalog.Name(armoury.Researching.Value)}" +
                      (armoury.Replacing.HasValue ? $"\nЗатем забудется: {WeaponCatalog.Name(armoury.Replacing.Value)}" : "")
                    : $"Спецоружие для отрядов: открыто {Player.knownWeapons.Count} из {WeaponCatalog.MaxKnownSpecials}";
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

        // Кнопка найма отряда: вторая строка мелко — как включить или выключить постоянный найм
        string HireSquadText(bool repeat, SquadWeapon weapon)
        {
            string how = layout == HudLayout.Mobile ? "Долгое нажатие" : "ПКМ";
            int cost = WeaponCatalog.SquadCost(weapon);
            return repeat
                ? $"Нанимать постоянно · {cost}\n<size=58%>{how} — выключить</size>"
                : $"Нанять отряд ({UnitCatalog.SquadSize}) · {cost}\n<size=58%>{how} — нанимать постоянно</size>";
        }

        ButtonPressExtras hirePressExtras;
        Color hireButtonColor;

        // ---------- Пункт подкрепления ----------

        class ReinforceRow
        {
            public RectTransform rect;
            public TMP_Text label;
            public Button button;
            public TMP_Text buttonText;
            public Squad squad;
        }
        readonly List<ReinforceRow> reinforceRows = new();
        readonly List<Squad> reinforceSquads = new();
        ReinforcementPoint shownReinforcement;

        // Над панелью пункта подкрепления — отряды игрока: номер, режим, бойцы и «Пополнить +N · цена»
        void UpdateReinforcePanel(ReinforcementPoint point)
        {
            shownReinforcement = point;
            if (reinforcePanel.activeSelf != (point != null))
                reinforcePanel.SetActive(point != null);
            if (point == null)
                return;

            // Все отряды, самые потрёпанные — сверху (по фактическим бойцам, а не с учётом заказанных:
            // после нажатия «Пополнить» строка не прыгает из-под пальца), дальше по номеру
            reinforceSquads.Clear();
            foreach (var s in Player.squads)
                if (s.IsAlive)
                    reinforceSquads.Add(s);
            reinforceSquads.Sort((a, b) =>
            {
                int byLosses = a.Members.Count.CompareTo(b.Members.Count);
                return byLosses != 0 ? byLosses : a.Number.CompareTo(b.Number);
            });

            // Видно не больше reinforceMaxRows строк, остальное — прокруткой
            int count = reinforceSquads.Count;
            var listLayout = reinforceList.GetComponent<LayoutElement>();
            float rowHeight = reinforceRowTemplate.GetComponent<LayoutElement>().preferredHeight;
            float spacing = reinforceList.content.GetComponent<VerticalLayoutGroup>().spacing;
            int visibleRows = Mathf.Clamp(count, 1, reinforceMaxRows);
            listLayout.preferredHeight = visibleRows * rowHeight + (visibleRows - 1) * spacing;
            if (reinforceList.gameObject.activeSelf != (count > 0))
                reinforceList.gameObject.SetActive(count > 0);

            while (reinforceRows.Count < count)
                reinforceRows.Add(CreateReinforceRow(reinforceRows.Count));

            for (int i = 0; i < reinforceRows.Count; i++)
            {
                var row = reinforceRows[i];
                bool visible = i < count;
                if (row.rect.gameObject.activeSelf != visible)
                    row.rect.gameObject.SetActive(visible);
                if (!visible)
                {
                    row.squad = null;
                    continue;
                }

                var squad = reinforceSquads[i];
                row.squad = squad;
                string mode = squad.Behavior == BarracksBehavior.Defend ? "оборона" : "атака";
                string coming = squad.PendingReinforcements > 0 ? $"  <color=#9fd49f>+{squad.PendingReinforcements} в пути</color>" : "";
                // Спецоружие отряда — сколько его носителей живо (и забыто ли оно — тогда их не восстановить)
                string special = squad.Weapon == SquadWeapon.Rifle ? ""
                    : $" · {WeaponCatalog.Name(squad.Weapon).ToLower()} {squad.SpecialCount}/{WeaponCatalog.SpecialsPerSquad}" +
                      (Player.Knows(squad.Weapon) ? "" : " <color=#d9a38a>(забыто)</color>");
                row.label.text = $"Отряд {squad.Number} · {mode}\n" +
                                 $"<size=78%>бойцов {squad.Members.Count}/{UnitCatalog.SquadSize}{special}{coming}</size>";

                int missing = ReinforcementPoint.Missing(squad);
                int cost = missing > 0 ? ReinforcementPoint.Cost(squad) : 0;
                row.buttonText.text = missing > 0 ? $"Пополнить +{missing} · {cost}" : "Полный";
                row.button.interactable = missing > 0 && point.Structure.IsBuilt && Player.CanAfford(cost, 0);
            }

            if (reinforceNote.gameObject.activeSelf != (count == 0))
                reinforceNote.gameObject.SetActive(count == 0);
            if (count == 0)
                reinforceNote.text = "Отрядов нет — наймите в казармах";
        }

        ReinforceRow CreateReinforceRow(int index)
        {
            var rect = Instantiate(reinforceRowTemplate, reinforceRowTemplate.parent);
            rect.name = "Reinforce Row " + index;
            rect.SetAsLastSibling();
            var button = rect.Find("Reinforce").GetComponent<Button>();
            var row = new ReinforceRow
            {
                rect = rect,
                label = rect.Find("Label").GetComponentInChildren<TMP_Text>(),
                button = button,
                buttonText = button.GetComponentInChildren<TMP_Text>(),
            };
            button.onClick.AddListener(() =>
            {
                if (shownReinforcement != null && row.squad != null &&
                    !shownReinforcement.TryReinforce(row.squad, out var reason))
                    Toast(reason);
            });
            return row;
        }

        void SetHireProgress(float progress)
        {
            hireProgressFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        }

        // Строки «Вооружение» и «Оборона / Атака» есть только у казарм; без них панель ниже,
        // верхние строки опускаются
        void SetBarracksRowsVisible(bool visible)
        {
            if (behaviorRow.activeSelf == visible)
                return;
            behaviorRow.SetActive(visible);
            weaponRow.SetActive(visible);
            var rect = (RectTransform)selectionPanel.transform;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x,
                visible ? selectionHeightWithBehavior : selectionHeightWithBehavior - BehaviorRowHeight - WeaponRowHeight);
        }

        // Высота строки вооружения с зазором до строки поведения под ней
        float WeaponRowHeight
        {
            get
            {
                var weapon = (RectTransform)weaponRow.transform;
                var behavior = (RectTransform)behaviorRow.transform;
                float gap = weapon.anchoredPosition.y - (behavior.anchoredPosition.y + behavior.rect.height);
                return weapon.rect.height + gap;
            }
        }

        // ---------- Вооружение (казармы) ----------

        readonly List<(Button button, SquadWeapon weapon)> weaponButtons = new();
        readonly List<SquadWeapon> weaponChoices = new();

        // Кнопки «Винтовки» и открытого спецоружия: выбранное подсвечено, на каждой — надбавка и
        // роль мелко. Ничего не открыто — вторая кнопка-подсказка «изучите в оружейной»
        void UpdateWeaponRow(Barracks barracks)
        {
            weaponChoices.Clear();
            weaponChoices.Add(SquadWeapon.Rifle);
            weaponChoices.AddRange(Player.knownWeapons);
            bool hint = Player.knownWeapons.Count == 0;
            int count = weaponChoices.Count + (hint ? 1 : 0);

            while (weaponButtons.Count < count)
            {
                var button = Instantiate(weaponButtonTemplate, weaponButtonTemplate.transform.parent);
                button.gameObject.SetActive(true);
                int index = weaponButtons.Count;
                button.onClick.AddListener(() => SelectWeapon(index));
                weaponButtons.Add((button, SquadWeapon.Rifle));
            }

            for (int i = 0; i < weaponButtons.Count; i++)
            {
                var (button, _) = weaponButtons[i];
                bool visible = i < count;
                if (button.gameObject.activeSelf != visible)
                    button.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                if (i >= weaponChoices.Count)
                {
                    // Подсказка: где взять спецоружие
                    weaponButtons[i] = (button, SquadWeapon.Rifle);
                    button.interactable = false;
                    button.image.color = inactiveColor;
                    SetButtonText(button, "Спецоружие\n<size=62%>изучите в оружейной</size>");
                    continue;
                }

                var weapon = weaponChoices[i];
                weaponButtons[i] = (button, weapon);
                button.interactable = true;
                button.image.color = barracks.Weapon == weapon ? weaponSelectedColor : inactiveColor;
                if (weapon == SquadWeapon.Rifle)
                {
                    SetButtonText(button, "Винтовки\n<size=62%>у всех</size>");
                }
                else
                {
                    var special = WeaponCatalog.Special(weapon);
                    SetButtonText(button, $"{special.name} +{special.squadSurcharge}\n<size=62%>{special.role}</size>");
                }
            }
        }

        // ---------- Оружейная ----------

        class ArmouryRow
        {
            public RectTransform rect;
            public Image background;
            public TMP_Text label;
            public Button button;
            public TMP_Text buttonText;
            public SquadWeapon weapon;
        }
        readonly List<ArmouryRow> armouryRows = new();
        Armoury shownArmoury;
        // Выбрано оружие для изучения, но открыто уже два — ждём ответа «вместо чего»
        SquadWeapon? pendingResearch;
        Color armouryRowColor;

        // Над панелью оружейной — все 4 спецоружия: название, роль, надбавка к отряду; кнопка по
        // состоянию: «Открыто», «Будет забыто», «Изучается 40%», «Изучить · 200», «Изучить вместо… · 200»
        void UpdateArmouryPanel(Armoury armoury)
        {
            shownArmoury = armoury;
            if (armouryPanel.activeSelf != (armoury != null))
                armouryPanel.SetActive(armoury != null);
            if (armoury == null)
            {
                pendingResearch = null;
                return;
            }

            armouryHeader.text = $"Оружейная · открыто {Player.knownWeapons.Count} из {WeaponCatalog.MaxKnownSpecials}";

            var specials = WeaponCatalog.Specials;
            while (armouryRows.Count < specials.Count)
                armouryRows.Add(CreateArmouryRow());

            bool busy = Player.researching.HasValue;
            for (int i = 0; i < armouryRows.Count; i++)
            {
                var row = armouryRows[i];
                var special = specials[i];
                row.weapon = special.id;
                bool known = Player.Knows(special.id);
                bool researching = armoury.Researching == special.id;
                bool forgetting = armoury.Replacing == special.id;

                row.label.text = $"{special.name}\n<size=70%>{special.role} · отряд +{special.squadSurcharge}</size>";
                row.background.color = known ? knownWeaponRowColor : armouryRowColor;

                if (known)
                {
                    row.buttonText.text = forgetting ? "Будет забыто" : "Открыто";
                    row.button.interactable = false;
                }
                else if (researching)
                {
                    row.buttonText.text = $"Изучается {armoury.Progress:P0}";
                    row.button.interactable = false;
                }
                else
                {
                    row.buttonText.text = armoury.NeedsReplacement
                        ? $"Изучить вместо… · {special.researchCost}"
                        : $"Изучить · {special.researchCost}";
                    row.button.interactable = !busy && armoury.Structure.IsBuilt && Player.CanAfford(special.researchCost, 0);
                }
            }

            // «Изучить … вместо:» — пока выбор актуален
            if (pendingResearch.HasValue && (busy || Player.Knows(pendingResearch.Value) || !armoury.NeedsReplacement))
                pendingResearch = null;
            bool choosing = pendingResearch.HasValue;
            if (armouryReplaceRow.gameObject.activeSelf != choosing)
                armouryReplaceRow.gameObject.SetActive(choosing);
            if (choosing)
            {
                armouryReplaceRow.Find("Label").GetComponentInChildren<TMP_Text>().text =
                    $"Изучить «{WeaponCatalog.Name(pendingResearch.Value)}» вместо:";
                for (int k = 0; k < WeaponCatalog.MaxKnownSpecials; k++)
                {
                    var option = armouryReplaceRow.Find("Option " + k).GetComponent<Button>();
                    bool has = k < Player.knownWeapons.Count;
                    option.gameObject.SetActive(has);
                    if (has)
                        SetButtonText(option, WeaponCatalog.Name(Player.knownWeapons[k]));
                }
            }
        }

        ArmouryRow CreateArmouryRow()
        {
            var rect = Instantiate(armouryRowTemplate, armouryRowTemplate.parent);
            rect.gameObject.SetActive(true);
            rect.name = "Armoury Row " + armouryRows.Count;
            // Строки — сразу за образцом, перед строкой «вместо»
            rect.SetSiblingIndex(armouryReplaceRow.GetSiblingIndex());
            var button = rect.Find("Research").GetComponent<Button>();
            var row = new ArmouryRow
            {
                rect = rect,
                background = rect.GetComponent<Image>(),
                label = rect.Find("Label").GetComponentInChildren<TMP_Text>(),
                button = button,
                buttonText = button.GetComponentInChildren<TMP_Text>(),
            };
            button.onClick.AddListener(() => ResearchClicked(row.weapon));
            return row;
        }

        void ResearchClicked(SquadWeapon weapon)
        {
            if (shownArmoury == null)
                return;
            // Открыто уже два — сначала спросить, какое забыть
            if (shownArmoury.NeedsReplacement)
            {
                pendingResearch = weapon;
                return;
            }
            if (!shownArmoury.TryResearch(weapon, null, out var reason))
                Toast(reason);
        }

        void ReplaceClicked(int option)
        {
            if (shownArmoury == null || !pendingResearch.HasValue || option >= Player.knownWeapons.Count)
                return;
            var forget = Player.knownWeapons[option];
            if (shownArmoury.TryResearch(pendingResearch.Value, forget, out var reason))
                Toast($"Изучается «{WeaponCatalog.Name(pendingResearch.Value)}», «{WeaponCatalog.Name(forget)}» будет забыто");
            else
                Toast(reason);
            pendingResearch = null;
        }

        void SelectWeapon(int index)
        {
            var barracks = selected != null && selected.Faction == Player ? selected.Barracks : null;
            if (barracks == null || index >= weaponChoices.Count)
                return;
            barracks.SetWeapon(weaponButtons[index].weapon);
        }

        void ToggleRepeatForSelected()
        {
            var barracks = selected != null && selected.Faction == Player ? selected.Barracks : null;
            if (barracks == null)
                return;
            barracks.SetRepeat(!barracks.Repeat);
            Toast(barracks.Repeat ? "Постоянный найм включён" : "Постоянный найм выключен");
        }

        void HireForSelected()
        {
            // Это был конец долгого нажатия (постоянный найм) — не нанимать ещё и обычным кликом
            if (selected == null || hirePressExtras.ConsumeLongPress())
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
                var badgeColor = defend ? defendColor : attackColor;
                badge.GetComponent<Image>().color = badgeColor;
                badge.GetComponentInChildren<TMP_Text>().text = defend ? "Оборона" : "Атака";

                // Постоянный найм — справа от плашки круговая стрелка в её цвете
                var repeatIcon = badge.Find("Repeat Icon");
                if (repeatIcon != null)
                {
                    if (repeatIcon.gameObject.activeSelf != barracks.Repeat)
                        repeatIcon.gameObject.SetActive(barracks.Repeat);
                    if (barracks.Repeat)
                    {
                        repeatIcon.GetComponent<Image>().color = badgeColor;
                        var glyph = repeatIcon.Find("Glyph").GetComponent<Image>();
                        if (glyph.sprite == null)
                            glyph.sprite = UiIcons.Repeat;
                    }
                }
            }
            foreach (var b in badgeCleanup)
                badges.Remove(b);
        }

        void Toast(string text) => Toast(text, Color.white, 2.5f);

        void Toast(string text, Color color, float seconds)
        {
            toastText.text = text;
            toastText.color = color;
            toastText.enabled = true;
            toastTimer = seconds;
        }

        static string Cost(StructureDef def) =>
            def.costValuable > 0 ? $"{def.costBase} + {def.costValuable} ценного" : $"{def.costBase}";
    }
}
