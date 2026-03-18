# AIG

`AIG` — это воксельная игра на `C# + raylib`, написанная `GPT Codex` и вдохновлённая Minecraft.

Проект развивается в трёх основных направлениях:
- блочный мир с генерацией, добычей и строительством;
- бот-спутник, который умеет собирать ресурсы и строить вместо игрока;
- собственный рендер, который уже перешёл от простого рисования кубов к текстурному `chunk mesh` и `shader-driven` пайплайну.

## Что уже есть в игре

- процедурно генерируемый воксельный мир;
- система чанков и стриминга мира;
- ломание и установка блоков;
- хотбар и выбор блоков;
- вид от первого и третьего лица;
- главное меню и меню паузы;
- полноэкранный режим;
- лесной мир с деревьями и листвой;
- бот-спутник с командами:
  - сбор ресурсов;
  - строительство дома по шаблону;
- браслет-интерфейс для управления ботом;
- внутриигровые скриншоты и запись видео;
- текстурный atlas для блоков;
- `chunk mesh`-рендер поверхности мира;
- shader-driven world pass с лёгкими солнечными тенями;
- усиленная material system для разных типов блоков;
- постобработка, небо, облачные полосы и дальний фон, согласованные с новым рендером;
- усиленный shader stack мира с `sun scatter`, `ambient lift`, `haze` и material-shadow response;
- дополнительная глубина рендера через `horizon depth`, `foliage translucency` и последующие shader-driven слои глубины;
- автопроверки производительности и стабильности;
- полное автоматическое тестовое покрытие.

## Текущее состояние

Последняя завершённая версия: `0.022 (29)`.

На этом этапе в проекте уже реализованы:
- текстурный atlas блоков;
- `chunk meshing` для поверхности мира;
- настоящий `frustum culling` для чанков и поверхностей;
- явный visibility split `near / mid / far / atmospheric`;
- первый настоящий `voxel daylight propagation` для surface-кэша;
- отдельный `local light propagation` и явные локальные источники света для будущих факелов и ламп;
- первый настоящий `shadow pass` от солнца с `near/far` shadow maps;
- явные pass-контракты для `sky`, `screen-space`, `selection` и `held-block`;
- отдельные `object pass` и `final composite pass`;
- мягкие overlap-зоны между `near / mid / far / atmospheric` и более цельный дальний terrain handoff;
- расширенный `mid/far world` с более честным дальним terrain-mesh;
- отдельный `distant terrain mesh` для ultra-far terrain-чанков;
- отдельный `far-world streaming + cache residency` для дальнего мира;
- отдельный `cheap far-lighting contract` для distant terrain mesh: `far` и `ultra-far` больше не тянут одинаково дорогой local-light payload;
- двухконтурная `shadow policy`: near-world сохраняет полноценные тени, а дальний мир уходит в более дешёвый shadow-proxy response без полной стоимости far shadow resolve;
- high-профиль дальнего мира поднят до текущего предела архитектуры: `190` real-world distance для distant terrain mesh и расширенный far-world streaming под этот режим;
- более честная source-driven lighting model: закрытые шахты и комнаты без источников света больше не держат прежний магический ambient, а локальный свет и daylight заметно сильнее управляют итоговой освещённостью;
- очищенный visual stack без новой архитектуры: уменьшены `haze`, screen-space glow и навязчивость финального композита, усилена читаемость материалов и foliage в среднем плане;
- отдельная правка дальнего слоя: уменьшено выбеливание `far/ultra-far`, горизонт лучше отделён от неба, а distant terrain mesh держит форму заметно увереннее;
- более зрелый shader-driven light stack: `sun scatter`, `ambient lift`, `shadow depth`, `haze`, `material shadow`, `horizon depth`, `foliage translucency`, `secondary bounce`, `sky response`, `far gradient`, `shadow contour`, `atmospheric contour`, `relief bridge`, `shadow haze fusion`, `light plasticity`, `far readability`, `final cohesion`, `view material`;
- обновлённая подсветка блока;
- доработанный held-block и спецслои рендера;
- стабильный `autocheck` / `autoperf`;
- `100%` покрытие строк;
- `100%` покрытие ветвлений.

Подробная история версий и roadmap лежат в файле [versions](/home/rutasan/AIG/versions).

## Технологии

- `C#`
- `.NET 8`
- `Raylib-cs 7.0.2`

Вся игровая логика, генерация мира, бот, UI и текущий рендер написаны специально под этот проект.

## Запуск

Требование:
- установленный `.NET 8 SDK`

Запуск из корня проекта:

```bash
dotnet run --project src/AIG.Game/AIG.Game.csproj
```

Запуск тестов:

```bash
dotnet test AIG.sln -v minimal
```

Проверка покрытия:

```bash
dotnet test AIG.sln --collect:"XPlat Code Coverage" -v minimal
```

## Управление

### Перемещение

- `W A S D` или стрелки: движение
- `Space`: прыжок
- мышь: обзор

### Взаимодействие с миром

- `ЛКМ`: сломать блок
- `ПКМ`: поставить блок
- `1-9`: выбрать слот хотбара

### Камера

- `V` или `F5`: переключение между видом от первого и третьего лица

### Меню и интерфейс бота

- `ESC`: меню паузы
- `B`: открыть или закрыть браслет управления ботом
- `Enter`: подтвердить действие в интерфейсе браслета
- `0-9`: ввод количества ресурсов
- `Backspace`: удалить цифру

### Служебные функции

- `F10`: начать/остановить запись видео
- `F12`: сделать скриншот
- `F3`: расширенный debug HUD

## Возможности бота

Бот-спутник умеет:
- следовать за игроком;
- собирать дерево, камень, землю и листву;
- хранить запас ресурсов;
- строить дом по blueprint;
- работать с очередью команд;
- показывать статус через интерфейс браслета.

Также у бота уже есть:
- навигация по миру;
- восстановление после проблемного маршрута;
- `no-path` cooldown;
- fallback-логика для стройки;
- ограниченный `scaffold-recovery`, если до шага стройки нельзя дотянуться обычным путём;
- диагностика в папке `botlogs/`.

## Направление рендера

Игра начиналась с простого покубового рендера.

Сейчас рендер уже включает:
- texture atlas блоков;
- textured mesh path;
- `chunk meshing`;
- настоящий `frustum culling`;
- явный split мира на `near / mid / far / atmospheric`;
- локальное skylight field и daylight payload поверхности;
- отдельный local-light payload и явные локальные источники света;
- первый CPU shadow pass от солнца с near/far shadow maps;
- отдельные pass-настройки для `sky`, `screen-space`, `selection` и `held-block`;
- отдельные `object pass` и `final composite pass`;
- расширенный дальний mesh-пояс для terrain-dominant чанков;
- отдельный `distant terrain mesh` и отдельный `far-world streaming` для большого мира;
- CPU/GPU-кэш мешей;
- shader-driven world pass;
- material-channel для разных типов блоков;
- material-separation для лучшего различия блоков под светом и в тени;
- material-shadow response для более выразительного различия материалов именно в тенях;
- `horizon depth`, `foliage translucency`, `secondary bounce`, `sky response`, `far gradient`, `shadow contour`, `atmosphere gradient`, `distance shadow lift`, `sky contour`, `distant silhouette`, `atmospheric contour`;
- финальные shader-driven слои `relief bridge`, `shadow haze fusion`, `light plasticity`, `far readability`, `final cohesion`, `view material`;
- атмосферный туман;
- стилизованный свет, distance haze и первые настоящие солнечные тени;
- улучшенные небо, облачные полосы и дальний фон;
- лёгкий post-process без тяжёлого offscreen-пайплайна;
- дополнительные secondary effects, связывающие солнце, haze, небо и дальний фон в более цельный кадр;
- доработанные highlight- и held-block-слои;
- более зрелое смешивание `daylight + local light` и более цельный light payload на границах чанков.

Этапы `0.022 (10) - 0.022 (23)` перевели проект на новый архитектурный курс: сначала появилась более честная система видимости мира, затем первый настоящий daylight payload в surface-кэше чанков, после этого мир получил отдельный канал локального света, затем поверх этого ввели первый реальный shadow pass от солнца, потом render stack разнесён на более явные pass-контракты, дальше `mid/far world` подтянут под тот же фундамент, в `0.022 (16)` добавлена финальная склейка shadow cascades, дальнего мира и атмосферы, в `0.022 (17)` дожат сам shadow resolve через PCF-подобную фильтрацию, slope bias и более зрелый cascade handoff, в `0.022 (18)` сам световой payload стал цельнее на chunk boundaries и в mesh lighting, в `0.022 (19)` добавлены явные `object pass` и `final composite pass`, в `0.022 (20)` переходы между `near/mid/far/atmospheric` стали мягче через overlap-band visibility blending и более цельный far-terrain handoff, в `0.022 (21)` все финальные screen-space, shadow и far-world параметры сведены через единый стабильный `render polish profile`, в `0.022 (22)` появился отдельный `distant terrain mesh` слой для ultra-far terrain-чанков, а в `0.022 (23)` над ним введён отдельный `far-world streaming + cache residency` слой. Текущий рендер теперь строится как цепочка `visibility -> light -> shadows -> passes -> far world -> final cohesion`, и дальний мир уже не только рисуется отдельно, но и удерживается отдельным streaming-path, а не простым backdrop или повторением ближних правил.

## Структура проекта

```text
src/AIG.Game/
  Bot/        - логика бота, команды, blueprint, навигация
  Config/     - конфигурация игры
  Core/       - главный игровой цикл, рендер, UI, ввод, захват видео/скриншотов
  Gameplay/   - игровая вспомогательная логика
  Player/     - контроллер игрока и камера
  World/      - блоки, чанки, генерация, стриминг мира

tests/AIG.Game.Tests/
  автотесты логики игры, бота, рендера, интерфейсов и захвата

assets/
  шрифты, текстуры, шейдеры

captures/
  скриншоты и видеозаписи

botlogs/
  диагностические логи бота
```

## Идея проекта

Этот проект нужен как практический эксперимент: сделать воксельную игру, где весь код пишет `GPT Codex`, а человек не вмешивается в программирование.

Роли в проекте разделены так:
- `GPT Codex` пишет весь игровой код, тесты, рендер, генерацию мира, логику бота и инфраструктуру проекта;
- человек участвует только в геймдизайне, постановке задач, выборе направлений развития и визуальных/игровых решений.

То есть цель проекта — не просто сделать ещё одну Minecraft-подобную игру, а проверить, насколько далеко можно довести полноценную игру, если разработка кода полностью выполняется ИИ, а человек остаётся на стороне дизайна и управления направлением проекта.
