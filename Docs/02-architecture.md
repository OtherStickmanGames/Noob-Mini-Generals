# Архитектура

## Технологии

- Unity **2022.3.62f3**, URP **14.0.12**, Burst 1.8.27, Collections 2.6.4, Mathematics 1.2.6,
  AI Navigation 1.1.6, TextMeshPro 3.0.7, старый Input Manager.
- Ассембли-дефиниций нет: весь код в Assembly-CSharp (видит URP, Burst и т. д.).
- Ассеты рендерера URP: `Assets/Settings/URP-{Performant,Balanced,HighFidelity}(-Renderer).asset`.
  HighFidelity — MSAA 4x. Во всех рендерерах есть фича `OutlineFeature` (добавляется сама,
  см. «Рендеринг»), промежуточная текстура камеры — Always.

## Папки

| Папка | Что там |
|---|---|
| `Assets/VoxelArena` | Воксельная арена: хранение, генерация, меш, NavMesh, шейдер, тестовая сцена. Глобальное пространство имён. README внутри. |
| `Assets/Gameplay` | Бой вертикального среза, пространство имён `Generals`. README внутри. |
| `Assets/Gameplay/Resources` | Шейдеры, которые ищутся через `Shader.Find` (сетка застройки, контур) — в Resources, чтобы попасть в сборку. |
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

### Хранение и меш
- `VoxelArena.cs`: один `NativeArray<byte>` на всю арену (id блока), чанки 16³.
- Меш чанка — Burst-джоб `Jobs/GreedyMeshJob.cs`: склеивает грани с одинаковым цветом палитры и AO.
  Режимы: суша и вода (вода — отдельный дочерний меш чанка).
- **Меш собирается обычными `SetVertices / SetNormals / SetColors / SetIndices`**, и один и тот же
  меш идёт и в `MeshRenderer`, и в `MeshCollider`. (Ручной `SetVertexBufferParams` ломал
  коллайдер — см. `04-problems-and-lessons.md`.)
- Цвет вершины: `r` — индекс в палитре (0..255), `g` — AO.
- `Explode(worldCenter, radius)` — разрушение; перестраиваются только затронутые чанки
  (с запасом на воксель для AO).
- События: `Generated`, `ChunkMeshChanged(index, land, water, matrix)`.

### Блоки и палитра
- `VoxelBlocks.cs`: id блоков 0..19 (Air, Bedrock, Stone, Dirt, Grass, Sand, Water, Wood, Leaves,
  LeavesAlt, Snow, GoldOre, IronOre, Wall, Marker, TeamOne, TeamTwo, TeamOneDark, TeamTwoDark, Metal);
  у каждого блока цвет граней — слоты палитры (верх/бок). Слоты команд — 32+. Слоты стен:
  `SlotWallTop = 15`, `SlotWallSide = 16` (на них завязан шейдер в режиме установки).
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

| Файл | Роль |
|---|---|
| `MatchManager.cs` | Синглтон боя. По `arena.Generated`: стороны, `BuildGrid`, точки захвата, главное здание (повернуто к воротам) и стартовый добытчик; строители — когда готов NavMesh. Заказ построек, найм строителей, общий материал зданий. |
| `Faction.cs` | Сторона: ресурсы, уровень стен, здания, строители, очередь найма. |
| `StructureCatalog.cs` | Типы зданий (Headquarters, Extractor, Mine, Barracks, Turret), размеры, цены, время, HP, правило места (`InsideWalls`, `BaseArea`, `Deposit`), экономические константы. |
| `Structure.cs` | Здание: модель поднимается из земли по мере стройки, доход, `BoxCollider` для тапа, `NavMeshObstacle` с вырезанием. |
| `BuilderUnit.cs` | Строитель на NavMeshAgent: сам берёт ближайшую незанятую стройку (`MatchManager.ClaimSite`). |
| `CapturePoint.cs` | Точка захвата: радиус 4 м, захват 10 с, владелец, флаг, доход. Захватчики — через интерфейс `ICapturer` и статический список. |
| `BuildGrid.cs` | Занятость клеток, проверка места (`CanPlace`, по клеткам — `IsCellBuildable`), поиск места (`FindNearest`, `FindNearestFree`, `HasClearance`). |
| `VoxelModels.cs` | Воксельные модели-заглушки зданий, строителя, флага; меш через `GreedyMeshJob`, кэш. |
| `RtsCamera.cs` | Камера: своя база внизу экрана, сдвиг/зум пальцами, мышью, WASD; `Tapped`, перетаскивание объекта (`TryBeginObjectDrag`, `ObjectDragged`), `GlideTo` (плавный перевод). |
| `GameHud.cs` | Логика интерфейса: ресурсы, меню построек, установка здания, панель выбранного здания. Настройки вида установки (`BuildGridStyle`, `PlacementOutlineStyle`, `PlacementSceneStyle`) — в инспекторе. |
| `BuildGridOverlay.cs` + `Resources/BuildGrid.shader` | Сетка застройки вокруг центра экрана (апофема 38), клетки вплотную, линии и заливку рисует шейдер по UV. Также рисует второй прозрачный проход стен. |
| `BuildModeVisuals.cs` | Вид арены при установке: ключ шейдера, прозрачность и приглушение материалов. |
| `Rendering/OutlineFeature.cs`, `Rendering/SelectionOutline.cs`, `Resources/Outline.shader` | Экранный контур (URP Renderer Feature): маска силуэта с буфером глубины камеры → линия постоянной толщины в пикселях, тусклее за препятствиями. |
| `Editor/HudPrefabBuilder.cs` | Собирает `HUD.prefab` (Canvas Scale With Screen Size 1920×1080, TMP). Меню Tools/Voxel Arena/Rebuild HUD Prefab (ручные правки префаба пропадут). |
| `Editor/OutlineFeatureInstaller.cs` | Сам добавляет `OutlineFeature` во все ассеты рендерера URP. |

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
