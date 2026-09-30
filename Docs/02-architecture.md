# Архитектура

## Технологии

- Unity **2022.3.62f3**, URP **14.0.12**, Burst 1.8.27, Collections 2.6.4, Mathematics 1.2.6,
  AI Navigation 1.1.6, TextMeshPro 3.0.7, старый Input Manager.
- Ассембли-дефиниций нет: весь код в Assembly-CSharp (видит URP, Burst и т. д.).
- Ассеты рендерера URP: `Assets/Settings/URP-{Performant,Balanced,HighFidelity}(-Renderer).asset`.
  HighFidelity — MSAA 4x. Во всех рендерерах есть фича `OutlineFeature` (добавляется сама,
  см. `Rendering/OutlineFeature.cs` в разделе «Бой»), промежуточная текстура камеры — Always.

## Папки

| Папка | Что там |
|---|---|
| `Assets/VoxelArena` | Воксельная арена: хранение, генерация, меш, NavMesh, шейдер, тестовая сцена. Глобальное пространство имён. README внутри. |
| `Assets/VoxelArena/Scripts` | Код арены (`Scripts/Jobs` — Burst-джобы). `Editor/` — меню создания сцен, `Shaders/` — шейдер арены. |
| `Assets/Gameplay` | Бой вертикального среза, пространство имён `Generals`. README внутри. |
| `Assets/Gameplay/Scripts` | Код боя (`Scripts/Rendering` — экранный контур). `Editor/` — сборка HUD-префаба и установка фичи рендерера. |
| `Assets/Gameplay/Resources` | Шейдеры, которые ищутся через `Shader.Find` (сетка застройки, контур, эффекты боя) — в Resources, чтобы попасть в сборку. |
| `Tools/compile-check.sh` | Проверка компиляции скриптов без открытия Unity (см. `05-workflow.md`). |
| `Assets/Gameplay/Prefabs/HUD.prefab` | Интерфейс боя (собран editor-скриптом, дальше может правиться руками). |
| `Assets/Scenes/Match.unity` | Сцена боя (основная для среза). `Voxel Arena.unity` — тестовая сцена арены. `Game.unity` — старая игра. |
| `Assets/Scripts`, `Assets/GameManager.cs`, `Assets/*.json`, `Assets/WeightSets` | Старая игра и её нейросеть (не используются новым кодом). |
| `Docs/` | Эта документация. |

## Масштабы и координаты

- Клетка земли = воксель арены = **0.5 м**. Арена 400 × 48 × 400 вокселей, чанки 16³.
- Модели зданий и юнитов — воксели **0.25 м** (`VoxelModels.VoxelSize`). Здание 8×8 клеток
  земли = 16×16 вокселей модели.
- Индекс столбца раскладки: `z * sizeX + x`. Индекс вокселя: `(y * dims.z + z) * dims.x + x`.
- `VoxelArena.WorldToVoxel` / `VoxelToWorld` — перевод координат; `SurfaceY(x, z)` — y первого
  пустого вокселя над сплошным (вода не сплошная); `ColumnTop` — мировая точка верха столбца.
- Клетки зданий: `int2` (x, z). `BuildGrid.MinFromCenter` — угол прямоугольника по центру.

## Воксельная арена (`Assets/VoxelArena`)

Файлы `.cs` ниже — в `Assets/VoxelArena/Scripts/` (`Jobs/…` — в `Scripts/Jobs/`); шейдер — в
`Assets/VoxelArena/Shaders/`, меню создания сцен — `Assets/VoxelArena/Editor/VoxelArenaSetup.cs`.

### Хранение и меш
- `VoxelArena.cs`: один `NativeArray<byte>` на всю арену (id блока), чанки 16³.
- Меш чанка — Burst-джоб `Jobs/GreedyMeshJob.cs`: склеивает грани с одинаковым цветом палитры и AO.
  Режимы: суша и вода (вода — отдельный дочерний меш чанка).
- **Меш собирается обычными `SetVertices / SetNormals / SetColors / SetIndices`**, и один и тот же
  меш идёт и в `MeshRenderer`, и в `MeshCollider`. (Ручной `SetVertexBufferParams` ломал
  коллайдер — см. `04-problems-and-lessons.md`.)
- Цвет вершины: `r` — индекс в палитре (0..255), `g` — AO.
- `GetBlock(voxel)`, `ClearVoxels(список)` — прочитать блок, выбить набор вокселей (участки стен).
- `Explode(worldCenter, radius)` — разрушение; перестраиваются только затронутые чанки
  (с запасом на воксель для AO).
- События: `Generated`, `ChunkMeshChanged(index, land, water, matrix)`.

### Блоки и палитра
- `VoxelBlocks.cs`: id блоков 0..19 (Air, Bedrock, Stone, Dirt, Grass, Sand, Water, Wood, Leaves,
  LeavesAlt, Snow, GoldOre, IronOre, Wall, Marker, TeamOne, TeamTwo, TeamOneDark, TeamTwoDark, Metal);
  у каждого блока цвет граней — слоты палитры (верх/бок). Слоты команд — 32+. Слоты стен:
  `SlotWallTop = 15`, `SlotWallSide = 16` (на них завязан шейдер в режиме установки).
  `FaceSlot(block, face)` — слот палитры грани (по нему обломки попадания берут цвет выбитого вокселя).
- `ArenaBiome.cs`: палитры биомов (лето, осень, зима, пустыня), снеговая линия, цвет тумана,
  плотность деталей. Биом — по сиду или вручную.

### Генерация (`ArenaLayout.cs` → `Jobs/ArenaGenerateJob.cs` → `ArenaDecorator.cs`)
Раскладка (по столбцам) строится на главном потоке (~5–6 с на ноутбуке автора), дальше Burst-джоб
заполняет воксели, декоратор ставит деревья, камни, стены, точки захвата.

Конвейер `ArenaLayoutGenerator` (логика сначала отлаживалась на Python-прототипе с картинками):
1. Горы по краю: ширина по шуму 8..44 клетки, гребни, снег, лес на склонах.
2. Базы на случайной оси через центр, 50 клеток поля за базой.
3. 3 уровня высоты по квантилям шума, сглаживание мод-фильтром, слияние мелких участков.
4. Содержимое баз: ровная площадка под стены 3-го уровня, стены 1-го уровня (воксельные,
   с зубцами) и ворота к противнику.
5. Вода (озёра), затем рампы по графу связности участков (union-find; если не выходит — выравнивание участка).
6. Точки захвата — после рамп, только там, куда доходит проход шириной 3 клетки.
7. Коридоры без деревьев (ворота ↔ ворота, ворота ↔ точки), деревья, камни.
8. `baseZone` (внутри стен какого уровня клетка: 1..3 — база 1, 5..7 — база 2),
   `baseArea` (площадка базы: 1 / 2).
9. Проверка проходимости по сетке с учётом стен, деталей и ширины агента.

Настройки — `ArenaGenSettings.cs` (в инспекторе арены). Поле `version` + `CurrentVersion`
(сейчас 8): сцена со старой версией настроек получает значения по умолчанию (в логе:
«Настройки генерации из старой версии…»). При изменении значений по умолчанию — поднимать версию.

### NavMesh (`ArenaNavMesh.cs`)
- Один `NavMeshData` на всю арену, **готовый список источников** (`NavMeshBuildSource`) по
  мешам чанков; обновление `NavMeshBuilder.UpdateNavMeshDataAsync` только в границах изменений.
- Размер плитки = чанк, поэтому пересобираются только изменённые плитки. NavMeshLinks не нужны.
- Вода — источник с областью Not Walkable. Здания — `NavMeshObstacle` с вырезанием.
- Замер: взрыв ≈ 1.8 мс меш, 30–100 мс NavMesh за 2 кадра (асинхронно).

### Шейдер арены (`Shaders/VoxelArena.shader`)
- Палитра — глобальная текстура `_VoxelPaletteTex` (256×1), `_VoxelSize` — глобально.
- Проходы: ForwardLit, ShadowCaster (с `CommonMaterial.hlsl` — иначе ошибка `LerpWhiteTo`), DepthOnly.
- Режим установки здания: глобальный ключ `_VOXEL_BUILD_FADE` — непрозрачная земля не рисует
  пиксели стен (`_HideWalls`), их рисует второй прозрачный проход (материал с ключом `_WALLS_ONLY`).
  Свойства для копий материала: `_Alpha`, `_SrcBlend/_DstBlend/_ZWrite`, `_Desaturate`, `_Darken`.

## Бой (`Assets/Gameplay`, пространство имён `Generals`)

Пути в таблице — от `Assets/Gameplay/Scripts/`; `Resources/…` и `Editor/…` — от `Assets/Gameplay/`.

| Файл | Роль |
|---|---|
| `MatchManager.cs` | Синглтон боя. По `arena.Generated`: стороны, `BuildGrid`, точки захвата, главное здание (повернуто к воротам) и стартовый добытчик; строители — когда готов NavMesh. Заказ построек, найм строителей, общий материал зданий. Пехота: `SpawnSquad` (отряд выходит из казарм к воротам), `UpdateSquads`, `GateOf`, `SquadPost` (посты отрядов в обороне за воротами, по три в ряд). Отладка: C — захват точки, V / B — отряд противника (5 бойцов) в атаке / обороне. Держит `Projectiles` и `Effects` (добавляет их себе в `Awake`). Бой: `StructureDestroyed` (освобождает клетки, казармы возвращают очередь, главное здание → `Winner`, `IsOver`, `EndPoint`), `MatchTime`, `NewMatch` (новый сид, `arena.Generate()`). Стены: участки (`CreateWallSegments`, `WallSegmentAt`, `WallSegmentDestroyed` — с турелью на нём). Атака построек: `ChooseAttackTarget` (случайная из ближайших, веса: близость, здание ×2, меньше уже атакующих; внутри вражеских стен — только здания, `IsInsideWalls` по `layout.baseZone`), `ClaimAttackSlot` / `ReleaseAttackSlot` (позиции кольцом вокруг цели: NavMesh, линия огня, путь есть, не в воротах, не рядом с чужой позицией). |
| `Faction.cs` | Сторона: ресурсы, уровень стен, здания, строители, пехота (`units`), участки стены (`walls`), очередь найма строителей, итоги боя (нанято/потеряно бойцов, построено/потеряно зданий). |
| `StructureCatalog.cs` | Типы зданий (Headquarters, Extractor, Mine, Barracks, Turret, ReinforcementPoint), размеры, цены, время, HP, правило места (`InsideWalls`, `BaseArea`, `Deposit`), экономические константы. |
| `Structure.cs` | Здание (`IAreaTarget`, крошится через `DestructibleModel`): стройка по вокселям (`DestructibleModel.SetBuildProgress`, прочность растёт с 10% вместе со стройкой), доход, `BoxCollider` для тапа и попаданий (по высоте уже уложенного), выбивание вокселей — по накопленному урону (`damageTaken`), `NavMeshObstacle` с вырезанием. Урон → при нуле `MatchManager.StructureDestroyed`, эффект обрушения, `Destroy`. |
| `WeaponCatalog.cs` | Оружие (`WeaponDef`: урон, темп, дальность, скорость снаряда, точность, разброс, взрыв, воронка): `Rifle`, `Cannon`; скорость поворота турели. |
| `Combat.cs` | `IDamageable` (бойцы, здания, участки стены: прочность, `AimPoint`, `MissPoint`, `TakeDamage(урон, точка, направление)`), `IAreaTarget` (цель-прямоугольник: здание, участок стены) и геометрия прямоугольника; `DamageableAt(hit)` — попадание в воксель стены даёт её участок; `Combat` — `IsAlive` (с проверкой уничтоженного объекта), луч с попаданиями по порядку, `HasLineOfFire` (свои не мешают), разброс в конусе, урон по площади. |
| `Weapon.cs` | Перезарядка и выстрел: бросок точности → в цель с упреждением или в `MissPoint`, разброс, снаряд в `Projectiles`, вспышка. |
| `Projectiles.cs` | Все летящие снаряды — структуры в списке (без GameObject), луч на длину шага; свои насквозь. Попадание: урон, в землю/стену — `arena.Explode` (воронка) и обломки её цвета; снаряд турели — взрыв и урон по площади. Рисуются `Graphics.RenderMeshInstanced` одним вызовом на вид. |
| `Effects.cs` | Эффекты кубиками: 3 `ParticleSystem` на весь бой — обломки (цвет из палитры арены, гравитация, отскок от земли), дым (прозрачный), вспышки (светятся). Вспышка ствола, искры, фонтан земли, взрыв, след снаряда, гибель бойца, обрушение здания. Материалы — шейдер `Resources/Effect.shader`. |
| `DestructibleModel.cs` | Стройка: заложенное здание — пустая модель + чертёж (меш неуложенных вокселей, материал `Effects.BlueprintMaterial`); `SetBuildProgress` укладывает воксели по порядку (слои снизу вверх, внутри слоя вразнобой) с эффектом `Effects.VoxelPlaced`, пересборка меша во время стройки не чаще раза в 0.06 с. Своя копия вокселей здания (`VoxelModels.StructureBody`) и свой меш: `Damage` выбивает воксели у точки попадания по снятой доле прочности (к нулю — `MaxChippedShare` = 35%), куски без связи с фундаментом (обход по граням от нижнего слоя) падают обломками; `Shatter` — разлёт оставшихся при разрушении; меш пересобирается раз в кадр (`RebuildIfDirty`, `Model.WriteMesh`). |
| `WallSegment.cs` | Участок стены базы (квадрат `WallSegmentCells` клеток) как цель (`IAreaTarget`): помнит свои воксели стены в арене; урон выбивает ближайшие к попаданию (`VoxelArena.ClearVoxels`), при нуле — обрушение целиком (пролом, NavMesh пересобирается сам). Создаются `MatchManager.CreateWallSegments`; попадание в воксель стены → участок через `MatchManager.WallSegmentAt`. |
| `EnemyAI.cs` | Скриптовый ИИ противника (`EnemyAiSettings` — сериализуемое поле `MatchManager`): список стройки `BuildOrder` (пункт выполнен, если таких построек/строителей не меньше, чем раз он встретился до него), выбор места (`FindSpot`: вокруг главного здания со случайным сдвигом или по бокам ворот для турелей; `CanPlace` + зазор 2 клетки + не в коридоре ворота↔главное здание), найм пехоты с запасом денег, волны (`WaveNumber`, `Attacking`). `ResetForMatch` — по `Generated`, `Begin` — когда появились строители. |
| `Turret.cs` | Турель рядом со `Structure`: поворотная башня (`VoxelModels.TurretHead` на `TurretBase`), выбор цели (бойцы, потом здания) с линией огня, стрельба при довороте ствола. |
| `UnitCatalog.cs` | Поведение казарм (`BarracksBehavior`: Defend, Attack), цифры пехоты (HP, скорость, дальность, обзор, радиус обороны), отряд (`SquadSize` 5, `SquadCost`, `SquadHireTime`), лимит очереди отрядов. |
| `Barracks.cs` | Компонент казарм рядом со `Structure`: очередь найма отрядов, постоянный найм (`Repeat`), прогресс, поведение (на все отряды этих казарм), список отрядов (`Squads`). При разрушении (`OnDestroyed`) возвращает деньги за очередь, бойцы остаются с последним режимом. |
| `InfantryUnit.cs` | Пехотинец (`IDamageable`) на NavMeshAgent — исполнитель приказов своего отряда (`Squad`): свободная стрельба по ближайшему врагу в дальности с линией огня (важнее приказа), иначе строй (`Squad.FormationPoint`) или своя позиция вокруг постройки отряда (`ClaimAttackSlot`); застрял 1.5 с — другая позиция; упал с NavMesh — `Warp`. Коллайдер-триггер: в него попадают снаряды, тап его не выбирает. |
| `ReinforcementPoint.cs` | Пункт подкрепления рядом со `Structure`: `TryReinforce(squad)` — оплата за недостающих (`Missing` учитывает `Squad.PendingReinforcements`), очередь по бойцу, выход — `MatchManager.SpawnReinforcement` (боец сразу в отряде); возврат денег за погибшие отряды и при разрушении (`OnDestroyed`). Список отрядов в HUD — `GameHud.UpdateReinforcePanel`, элементы префаба — `HudPrefabBuilder.AddReinforceControls` (ПК и мобильный). |
| `Squad.cs` | Отряд (как в Dawn of War): бойцы, поведение от казарм, решение раз в 0.25 с — `SquadOrder` Hold (строй на посту `MatchManager.SquadPost`), EngageUnits (строй на подходе к врагу), AttackArea (цель — `ChooseAttackTarget`, внутри вражеских стен — только здания). Тикается из `MatchManager.UpdateSquads`. |
| `BuilderUnit.cs` | Строитель на NavMeshAgent: сам берёт ближайшую незанятую стройку (`MatchManager.ClaimSite`). |
| `CapturePoint.cs` | Точка захвата: радиус 4 м, захват 10 с, владелец, флаг, доход. Захватчики — через интерфейс `ICapturer` и статический список (пехота его пока не реализует: точки заморожены, захват только отладочной клавишей C). |
| `BuildGrid.cs` | Занятость клеток, проверка места (`CanPlace`, по клеткам — `IsCellBuildable`), поиск места (`FindNearest`, `FindNearestFree`, `HasClearance`). |
| `VoxelModels.cs` | Воксельные модели-заглушки зданий, строителя, пехотинца, флага; турель — целиком (призрак при установке) и по частям (`TurretBase` + поворотная `TurretHead`); меш через `GreedyMeshJob`, кэш. |
| `RtsCamera.cs` | Камера: своя база внизу экрана, сдвиг/зум пальцами, мышью, WASD; `Tapped`, перетаскивание объекта (`TryBeginObjectDrag`, `ObjectDragged`), `GlideTo` (плавный перевод). |
| `GameHud.cs` | Логика интерфейса: ресурсы, меню построек, установка здания, панель выбранного здания (прочность рядом с названием), значки казарм, полоски прочности (пул копий образца, след урона), конец боя (камера к главному зданию, итоги, «Новый бой», «Осмотреться»), предупреждение о волне. Платформа: `layout` (PC / Mobile), у ПК-интерфейса `mobileVariant` и `choice` (Auto / PC / Mobile) — в `Awake` на телефоне заменяет себя мобильным. Настройки вида установки (`BuildGridStyle`, `PlacementOutlineStyle`, `PlacementSceneStyle`), цвета полосок и итогов — в инспекторе. |
| `BuildGridStyle.cs` | Сериализуемые стили вида установки для инспектора HUD: `BuildGridStyle` (цвета, линии, заливка, апофема, затухание края сетки), `PlacementSceneStyle` (прозрачность и приглушение стен и зданий), `PlacementOutlineStyle` (цвета и толщина контура, яркость за препятствием). Значения по умолчанию = настроенные автором в префабе. |
| `BuildGridOverlay.cs` + `Resources/BuildGrid.shader` | Сетка застройки вокруг центра экрана (апофема 38), клетки вплотную, линии и заливку рисует шейдер по UV. Также рисует второй прозрачный проход стен. |
| `BuildModeVisuals.cs` | Вид арены при установке: ключ шейдера, прозрачность и приглушение материалов. |
| `Rendering/OutlineFeature.cs`, `Rendering/SelectionOutline.cs`, `Resources/Outline.shader` | Экранный контур (URP Renderer Feature): маска силуэта с буфером глубины камеры → линия постоянной толщины в пикселях, тусклее за препятствиями. |
| `Editor/HudPrefabBuilder.cs` | Собирает `HUD.prefab` (Canvas Scale With Screen Size 1920×1080, TMP). Меню Tools/Voxel Arena/Rebuild HUD Prefab (ручные правки префаба пропадут). |
| `Editor/HudMobilePrefabBuilder.cs` | Собирает мобильный `HUD_Mobile.prefab` (Canvas масштаб по высоте, `SafeArea`, крупные элементы, меню построек — `ScrollRect` с горизонтальной лентой) и проставляет ПК-префабу `mobileVariant` (`EnsureAndLink`, вызывается из `UpgradeIfNeeded`). Меню Tools/Voxel Arena/Rebuild Mobile HUD Prefab. |
| `ButtonPressExtras.cs` | Второе действие кнопки: ПКМ или долгое нажатие (0.5 с) — `Secondary`; после долгого нажатия обычный клик пропускается (`ConsumeLongPress`). На кнопке найма — постоянный найм. |
| `SafeArea.cs` | Растягивает RectTransform на `Screen.safeArea` (вырез, скругления), следит за поворотом и разрешением. |
| `Editor/HudPrefabUpgrader.cs` | После компиляции дополняет существующий `HUD.prefab` недостающими элементами (`HudPrefabBuilder.UpgradeIfNeeded`), не трогая ручные правки. Новые элементы интерфейса добавлять так же, а не пересборкой префаба. |
| `Editor/OutlineFeatureInstaller.cs` | Сам добавляет `OutlineFeature` во все ассеты рендерера URP. |

Шейдер эффектов `Resources/Effect.shader` («NoobGenerals/Effect»): кубики трёх видов по локальным
ключам — `_PALETTE` (альфа цвета вершины = индекс палитры арены — не r: RGB частиц в линейном пространстве Unity переводит из гаммы; освещённый; обломки в цветах биома и
команд), `_EMISSIVE` (светится сам: снаряды, вспышки), `_BLUEPRINT` (чертёж стройки: `_Color` с яркими
линиями по граням вокселей, координаты объекта — в вокселях модели), без ключа — освещённый `_Color` × цвет
вершины (дым, прозрачный через `_SrcBlend/_DstBlend/_ZWrite`). Поддерживает инстансинг. Материалы
создаются в коде (`Effects.CreateMaterial`), поэтому ключи — `multi_compile_local`, не `shader_feature`.

Порядок отрисовки в режиме установки: земля (непрозрачная, без стен) → прозрачные стены и
здания (очередь 3001, пишут глубину) → сетка (3002) → контур (после прозрачных).

## Сцены и editor-меню

- **Tools → Voxel Arena → Create Match Setup** — сцена боя: арена, `ArenaNavMesh`, камера с `RtsCamera`,
  `MatchManager`, экземпляр `HUD.prefab`, EventSystem.
- **Tools → Voxel Arena → Create Test Setup** — тестовая сцена арены (взрывы, агенты, замеры).
- **Tools → Voxel Arena → Rebuild HUD Prefab**, **Install Outline Feature**.

## Соглашения по коду

- Комментарии и строки интерфейса — по-русски.
- Новый код боя — только в `namespace Generals` (в старом коде есть `Building`, `Unit`, `Player`, `UI`, `Team`).
- В `VoxelArena.cs` `using Debug = UnityEngine.Debug;` (конфликт с `System.Diagnostics`).
- Настраиваемый вид — сериализуемые классы стилей в инспекторе, значения по умолчанию в коде
  совпадают с тем, что автор настроил в префабе.
