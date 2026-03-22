---
name: IProjectArtifactService VM и merge
overview: Прослойка Agent04 — IProjectArtifactService / ProjectArtifactService как источник правды на диске (группировки, восстановление, создание/запись, сохранение контекста и удаление артефактов, в т.ч. субчанков). Rentgen и ChunkVirtualModelMerge сохраняют роли; логика merge переносится в сервис постепенно, финальный остаток merge фиксируется в конце работ.
todos:
  - id: phase-0-contract
    content: "Фаза 0: интерфейс IProjectArtifactService + решение по VM в ответе групп; список методов в proto/README фрагменте"
    status: completed
  - id: phase-1-root-di
    content: "Фаза 1: каркас ProjectArtifactService, ResolveArtifactRoot, интеграция с IJobArtifactRootRegistry, тесты путей"
    status: completed
  - id: phase-2-groups-grpc
    content: "Фаза 2: группировки на C# + gRPC GetChunkArtifactGroups (или аналог) + тесты на sample из agent-browser/runtime"
    status: completed
  - id: phase-3-write-delegate
    content: "Фаза 3: обёртки write — work state, pending_chunks, cancel_signals, кэш; вызовы из существующих классов через сервис"
    status: completed
  - id: phase-4-pipeline-wire
    content: "Фаза 4: пайплайн + TranscriptionGrpcService переводят создание/сплит на точки входа сервиса"
    status: completed
  - id: phase-5-delete-subchunk
    content: "Фаза 5: DeleteSubChunkArtifacts (+ merged/work state/rebuild правила) + тесты"
    status: completed
  - id: phase-6-agent05-proxy
    content: "Фаза 6: Agent05 прокси к новому gRPC, совместимый JSON"
    status: completed
  - id: phase-7-merge-migrate
    content: "Фаза 7: перенос инвариантов ChunkVirtualModelMerge в Agent04 по шагам + финальный аудит остатка в Agent05"
    status: pending
  - id: phase-8-cleanup-agent05
    content: "Фаза 8: удалить дубли Scanner/TS группировки когда контракт стабилен"
    status: pending
isProject: false
---

# IProjectArtifactService: контекст на диске, VM и постепенный перенос Merge

## Сжатый якорь (если контекст ИИ сократился — прочитать этот блок первым)

1. **Имена:** `IProjectArtifactService` / `ProjectArtifactService` в **Agent04**.
2. **Роль:** единственный владелец **персистентного** состояния на диске под job (layout, create/read/update/delete), включая work state, pending_chunks, cancel_signals, кэш, split_chunks, chunks, итоги transcript — см. таблицу «Инвентаризация Agent04» ниже.
3. **Не владелец:** Rentgen узлы (`INodeModel` / `INodeQuery`); оркестратор события пишет **сначала диск (сервис)**, потом узлы.
4. **Порядок поставки:** (A) gRPC группировок в Agent04 + тесты **без** смены UI → (B) прокси Agent05 → (C) перенос write/delete в сервис → (D) постепенно уменьшать `ChunkVirtualModelMerge` → (E) вычистить дубли в Agent05/TS.
5. **Семантика групп:** строго как `chunkArtifactGroups.ts` + `JobProjectFilesScanner` (раздел «Группировка файлов»).
6. **Явно не забыть:** `pending_chunks.json` consume-delete, `OperatorSplitArtifactPresence`, bootstrap work state, `SplitChunkMergeIntegrator` rebuild корня job, ffmpeg temp под `artifactRoot`.
7. **Файл плана:** репозиторий `.cursor/plans/iprojectartifactservice_vm_и_merge.plan.md` — при новом чате приложить или пересказать якорь.

## Зафиксированные имена и роли

- **Интерфейс:** `IProjectArtifactService`
- **Реализация:** `ProjectArtifactService`

**ProjectArtifactService в первую очередь отвечает за:**

1. **Группировки** артефактов (эквивалент сегодняшнего сканера + правил Stats: чанки, субчанки, merged split и т.д.).
2. **Восстановление контекста** работы с job / чанком / субчанком из того, что **лежит на диске** (включая `transcription_work_state.json`, legacy-bootstrap по документации Agent04, структуру каталогов).
3. **Сохранение контекста** — атомарные и согласованные обновления состояния на диске при изменениях пайплайна и операторских действий.
4. **Создание и запись файлов артефактов** — **через `ProjectArtifactService`** (или тонкие внутренние helper’ы, вызываемые только из него / из пайплайна через него): результаты транскрипции (`sub_chunk_XX_result.json`, merged, фрагменты `chunks`/`chunks_json`, итоговые `transcript.md` / combined JSON — всё, что сегодня пишут `SubChunkResultWriter`, `TranscriptionOutputWriter`, `TranscriptionWorkStateFile.Upsert*`, операторский split и т.д.). На переходном этапе допустимо **делегирование** существующим классам Agent04 при условии, что **единая точка входа** для «кто может создать путь X под job» — сервис (без прямого создания из Agent05/UI). Пайплайн и gRPC-обработчики вызывают сервис, а не разбрасывают `File.WriteAllBytes` по новым местам.
5. **Удаление артефактов и фрагментов контекста** — единая политика «что снести с диска» при операциях вроде **удаления субчанков**: файлы под `split_chunks/chunk_N/sub_chunks/` и `results/` для выбранного `subIndex`, согласованное обновление `transcription_work_state.json` (убрать/пометить строку субчанка), при необходимости инвалидация `chunk_N_merged.*` и пересборка зависимых итогов — по правилам, зафиксированным в одном месте (без разрозненных `File.Delete` по коду UI/API). Вызов из оркестратора после успеха — обновление Rentgen узла `…:chunk-N:sub-K` остаётся на стороне `INodeModel`, но **истина каталога** меняется через `ProjectArtifactService`.

**Источник правды для персистентного состояния:** диск в layout Agent04/job; сервис даёт **универсальный доступ** (создание/чтение/запись/**удаление**/запрос срезов) для остальных компонентов, без размазывания путей и эвристик по разным классам.

## Инвентаризация Agent04: кто сейчас работает с файлами

Ниже — обзор по коду `agent04/Agent04`, чтобы не потерять виды артефактов при выносе логики в `ProjectArtifactService`.


| Область                    | Класс / место                                                                                      | Что делает с диском                                                                                                                     |
| -------------------------- | -------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| Корень job / artifact root | `TranscriptionPaths.ResolveArtifactRoot`, `SubmitJob` + `IJobArtifactRootRegistry`                 | Регистрация `jobId → artifactRoot`, разрешение корня относительно workspace и входного файла.                                           |
| Work state                 | `TranscriptionWorkStateFile`                                                                       | Атомарная запись `transcription_work_state.json`, merge строк чанков/субчанков.                                                         |
| Bootstrap legacy           | `TranscriptionWorkStateBootstrapper`                                                               | При отсутствии state — вывод первого файла из `chunks/` + `chunks_json/` (+ ветки manifest); `Directory.CreateDirectory(artifactRoot)`. |
| Очередь чанков             | `EnqueueTranscriptionWork` (gRPC), `PendingChunksReader`                                           | Запись `pending_chunks.json`; чтение и **удаление** после consume.                                                                      |
| Отмена                     | `CancellationManager`                                                                              | Каталог `cancel_signals/`, флаги chunk/sub, `File.Delete` при clear.                                                                    |
| Pre-split чанки            | `AudioChunker`, пайплайн                                                                           | Создание `chunks/*` в `split_workdir` из конфига.                                                                                       |
| Конвертация входа          | `AudioUtils`                                                                                       | WAV/промежуточные файлы под `artifactRoot`, временные выходы ffmpeg, удаление temp.                                                     |
| Кэш API                    | `TranscriptionCache`                                                                               | Каталог `cache/`, `*.manifest.json`, `File.Create` для записи.                                                                          |
| Итоги job                  | `TranscriptionOutputWriter`                                                                        | `transcript.md`, combined/raw JSON, посегментная запись по шаблонам конфига.                                                            |
| Per-chunk JSON             | `TranscriptionOutputWriter` + пайплайн                                                             | Каталог `chunks_json` (или `per_chunk_json_dir`).                                                                                       |
| Операторский split         | `TranscriptionGrpcService` (Split), `OperatorChunkSplitPlanner`, `AudioUtils.ExtractAudioSegment*` | Создание `split_chunks/chunk_N/sub_chunks/*_sub_XX.*`.                                                                                  |
| Субчанк после split        | `TranscriptionPipeline.TranscribeSplitSubChunkAsync`, `SubChunkResultWriter`                       | `results/sub_chunk_XX_result.json` + upsert work state.                                                                                 |
| Merge субчанков            | `SplitChunkMergeIntegrator`                                                                        | `chunk_N_merged.json/.md`, пересборка job-level `transcript.md` / combined JSON при наличии `openai_response.json` и т.п.               |
| Пересборка combined        | `TranscriptionPipeline.RebuildCombinedOutputsFromPerChunkJsonAsync`                                | Повторная запись итогов из `chunks_json` + `chunks/`.                                                                                   |
| Диагностика                | `TranscriptionDiagnosticsSink`                                                                     | Опциональная запись логов (пути привязаны к job/chunk).                                                                                 |
| Проверки перед split       | `OperatorSplitArtifactPresence`                                                                    | Только чтение каталога (нет записи) — для группировки/политики «есть ли артефакты».                                                     |
| Чтение карты чанков        | `TranscriptionChunkOnDiskReader`                                                                   | Индексы из имён `part_NNN` под `chunks/` — для total chunks и валидации.                                                                |


## Что явно отнести к ProjectArtifactService (дополнение к п. 1–5 выше)

Ранее в плане не были названы отдельными строками, но по инвентаризации **должны оказаться под политикой сервиса** (через API сервиса или внутренние модули, вызываемые только оттуда):

- **Разрешение и кэш `artifactRoot`** для job (согласование с `IJobArtifactRootRegistry` и правилами `job_directory_relative` / безопасного combine путей из `TranscriptionGrpcService.ResolveChunkCancelBase`).
- **pending_chunks.json** — запись (очередь) и consume-delete как часть жизненного цикла артефактов job.
- **cancel_signals/** — создание/удаление флагов отмены как «служебные артефакты» job (сейчас `CancellationManager`).
- **Каталоги из `TranscriptionConfig`:** `split_workdir`, `cache_dir`, `split_chunks_dir`, `per_chunk_json_dir`, `converted_wav`/прочие пути вывода — единое место, где документирован **layout** и запрет на пути вне `artifactRoot`.
- **Кэш транскрипции** (`TranscriptionCache` + manifest) — чтение/запись как часть персистентного контекста качества/повторов.
- **Bootstrap первого work state** (`TranscriptionWorkStateBootstrapper`) — не дублировать снаружи сервиса; при переносе — вызываться из сервиса или сливаться с `LoadContext`.
- **Пересборка итогов** (`SplitChunkMergeIntegrator.TryRebuildMainJobOutputsAsync`, `RebuildCombinedOutputsFromPerChunkJsonAsync`) — любая запись, затрагивающая корневые `transcript.md`/JSON, согласована с правилами сервиса (и с удалением субчанков / merged).
- **Операторский split** — физическое создание файлов в `sub_chunks/` сейчас в gRPC-сервисе; целевой вариант — вызов через `ProjectArtifactService` (или метод сервиса из того же handler’а), чтобы split и delete subchunk шли из одной политики имён и каталогов.

**Что можно оставить «рядом», но с жёстким контрактом путей:** ffmpeg-временные файлы в `AudioUtils` — не обязательно прокидывать через публичный API сервиса, если все пути **строго под** `artifactRoot` и очищаются по правилам; либо сервис выдаёт `ITempArtifactScope` для конвертации.

## Что не забираем у соседей

- **Rentgen** (`INodeModel` / `INodeQuery`): по-прежнему узловая модель, прогресс, метаданные узлов; при старте/опросе взаимодействует с контекстом так же по контракту, но **не заменяет** персистентный слой на диске — договорённость о синхронизации с `ProjectArtifactService` остаётся явной.
- **ChunkVirtualModelMerge** (Agent05): **продолжает выполнять свою роль** на границе **входящий поток gRPC / снимок в store** — защита снимка UI от некорректных или неполных обновлений, пока перенос не завершён.

## Стратегия переноса из ChunkVirtualModelMerge

- Перенос **постепенный**: каждый переносимый инвариант (плейсхолдеры Pending, сохранение терминальных строк, склейка логов, строки «только в previous») по мере готовности **реализуется в Agent04** внутри `ProjectArtifactService` (или в вызываемых им компонентах), так чтобы **источник VM, идущий из Agent04**, уже был согласован с диском и политикой контекста.
- **ChunkVirtualModelMerge** сужается по мере того, как входящие обновления становятся достаточными; полное удаление не обязательно и решается на финальном шаге.
- **В самом конце работ** — отдельный проход: зафиксировать документом/комментарием в коде, **что именно осталось** в `ChunkVirtualModelMerge` (ожидаемо минимальный адаптер «два обновления подряд / гонка стрима и локального enrich», если такие сценарии сохранятся).

## Детальные фазы (пошагово; совпадают с todos в frontmatter)

Каждая фаза **завершается коммитом** и по возможности **зелёными тестами**, чтобы следующий сеанс мог продолжить с чёткой точки.

### Фаза 0 — Контракт и решения

- Описать методы `IProjectArtifactService` (имена и параметры): как минимум `ResolveArtifactRoot`, `GetGroupedArtifacts` (или эквивалент), далее `UpsertWorkState*`, `WritePendingChunks`, `DeleteSubChunkArtifacts`, … (уточнить список до начала кода).
- **Решение по VM:** включать ли строки VM в ответ gRPC групп на фазе 2, или только файлы + склейка VM в Agent05 до фазы 7 — зафиксировать одной строкой здесь и в комментарии к proto.
- **DoD:** markdown-таблица методов в плане или `docs/` фрагмент + ссылка из `Agent04/README.md` (одна строка).

**Сделано (фаза 0):** таблица методов и политика VM — в `[agent04/docs/PROJECT_ARTIFACT_SERVICE.md](agent04/docs/PROJECT_ARTIFACT_SERVICE.md)`; комментарий в `[agent04/Agent04/Proto/transcription.proto](agent04/Agent04/Proto/transcription.proto)`; ссылка в `[agent04/README.md](agent04/README.md)`. **VM:** на фазе 2 в ответе групп — **только файлы**; строки VM для Stats остаются в Agent05 до фазы 7.

### Фаза 1 — Каркас и корень артефактов

- Реализация `ProjectArtifactService` + DI в `[Agent04ServiceRegistration.cs](agent04/Agent04/Composition/Agent04ServiceRegistration.cs)`.
- Интеграция с `IJobArtifactRootRegistry` и логикой безопасного пути (аналог `ResolveChunkCancelBase` — либо вызов существующего хелпера из сервиса).
- **DoD:** unit-тесты: нормализация пути, отказ при `..`, согласованность с зарегистрированным root.

**Сделано (фаза 1):** `IProjectArtifactService.ResolveJobArtifactRoot` + `ProjectArtifactService`, DI, `TranscriptionGrpcService.ResolveChunkCancelBase` делегирует сервису; тесты — `[agent04/Agent04.Tests/ProjectArtifactServiceTests.cs](agent04/Agent04.Tests/ProjectArtifactServiceTests.cs)`.

### Фаза 2 — Только чтение: группировки + gRPC (фаза A API)

- Порт логики групп на C# (эквивалент `buildChunkGroups` + метаданные файла как в сканере).
- Новый RPC в `[transcription.proto](agent04/Agent04/Proto/transcription.proto)` (или отдельный сервис — выбрать и не менять без причины).
- Тесты на каталоги из `agent-browser/runtime/` и синтетические кейсы (`part_NNN`, merged, субчанки).
- **DoD:** контрактные тесты Agent04; UI **не** ломается (ещё не подключён).

**Сделано (фаза 2):** `JobArtifactDirectoryScanner`, `ChunkArtifactGrouping`, `IProjectArtifactService.GetChunkArtifactGroupsAsync`, RPC `**GetChunkArtifactGroups`** в `TranscriptionService`, тесты `[ChunkArtifactGroupingTests.cs](agent04/Agent04.Tests/ChunkArtifactGroupingTests.cs)`.

### Фаза 3 — Запись через сервис (делегирование)

- Обёрнуть вызовы `TranscriptionWorkStateFile`, запись `pending_chunks.json`, `CancellationManager` операции, при необходимости кэш — единая точка из сервиса.
- Пайплайн пока может вызывать статические методы **внутри** обёрток сервиса (тонкий фасад).
- **DoD:** нет новых прямых `File.Write*` в новых путях вне сервиса; регрессионные тесты существующих сценариев.

**Сделано (фаза 3):** модели work state перенесены в Application (`TranscriptionWorkStateModels.cs`); фасад в `ProjectArtifactService` для work state, pending_chunks, `GetCancellationManager`; `TranscriptionPipeline` и `TranscriptionGrpcService`/`EnqueueTranscriptionWork`/`ChunkCommand` cancel и `TryLoadWorkState`/`ResolveTotalChunksHint` через сервис; bootstrap сохраняет state через `IProjectArtifactService`. **Кэш** (`TranscriptionCache`) пока не проксирован — по плану опционально, отдельный шаг при необходимости.

### Фаза 4 — Пайплайн и gRPC: создание файлов

- Операторский split, `SubChunkResultWriter`, вывод пайплайна — вызовы через `ProjectArtifactService` (или методы сервиса, вызываемые из `TranscriptionGrpcService` / `TranscriptionPipeline`).
- **DoD:** поведение split + transcribe_sub идентично текущему (интеграционный или приёмочный сценарий).

**Сделано (фаза 4):** `ProjectArtifactService` принимает `IAudioUtils`, `ITranscriptionOutputWriter`, `WorkspaceRoot`, `IHostEnvironment`; фасад markdown/JSON/per-chunk + `WriteSubChunkTranscriptionResult`; `TryOperatorSplitAsync` (логика перенесена из `TranscriptionGrpcService`); `TranscriptionPipeline` пишет вывод только через `IProjectArtifactService`; `TranscriptionGrpcService.Split` вызывает сервис (поле `IAudioUtils` из gRPC убрано).

### Фаза 5 — Удаление субчанков и согласованность

- Реализовать политику удаления (файлы sub_chunks/results, work state, merged, опционально rebuild) в одном месте.
- **DoD:** тест «после delete группы/UI не видят старый merged»; документировать edge cases в комментарии к методу.

**Сделано (фаза 5):** `TranscriptionWorkStateFile.TryRemoveSubChunkRowAsync`; `TryDeleteSubChunkArtifactsAsync` (audio по `_sub_KK`, `sub_chunk_KK_result.json`, удаление `**chunk_N_merged.json/.md`** чтобы не оставлять устаревший merged, cancel flag, work state); gRPC `**CHUNK_COMMAND_ACTION_DELETE_SUB_CHUNK`** + проверка узла субчанка **Running** → `FailedPrecondition`; тесты `[SubChunkDeletionTests.cs](agent04/Agent04.Tests/SubChunkDeletionTests.cs)`. Полный **rebuild** job-level после delete не вызывается (как и в agent05 delete-bundle); при необходимости оператор может **RebuildCombined** отдельно.

### Фаза 6 — Agent05 прокси (фаза B)

- Клиент gRPC + маршрут в `[JobsController](agent05-ui-control/API/Controllers/JobsController.cs)` или отдельный endpoint; JSON совместим с текущим UI.
- **DoD:** ручная или автоматическая проверка Stats на одном job.

**Сделано (фаза 6):** синхронизирован `[transcription.proto](agent05-ui-control/API/Proto/transcription.proto)` с Agent04 (в т.ч. `GetChunkArtifactGroups`, `DELETE_SUB_CHUNK` в enum). `ITranscriptionServiceClient.GetChunkArtifactGroupsAsync` + `[TranscriptionGrpcClient](agent05-ui-control/API/Features/Jobs/Infrastructure/TranscriptionGrpcClient.cs)`; DTO `ChunkArtifactGroupsResult` / `ChunkArtifactGroupJson` в Application; `GET /api/jobs/{id}/chunk-artifact-groups`; `[ChunkArtifactGroupsControllerTests.cs](agent05-ui-control/API.Tests/ChunkArtifactGroupsControllerTests.cs)`. UI: `fetchJobChunkArtifactGroups`, `mergeChunkGroupVm` в `[chunkArtifactGroups.ts](agent05-ui-control/UI/src/utils/chunkArtifactGroups.ts)`, Stats в `[ChunkControlsStats.tsx](agent05-ui-control/UI/src/components/ChunkControlsStats.tsx)` при наличии `agent04JobId` берёт группы с прокси и сливает VM из снимка; при ошибке gRPC — откат к `buildChunkGroups` по `GET .../files`. В enum добавлен `TranscriptionChunkAction.DeleteSubChunk` (числовое совпадение с proto).

### Фаза 7 — VM / merge

- Перенос инвариантов из `[ChunkVirtualModelMerge.cs](agent05-ui-control/API/Features/Jobs/Infrastructure/ChunkVirtualModelMerge.cs)` в Agent04 по одному (плейсхолдеры, лог, orphan rows).
- **DoD:** документ «что осталось в Merge» в коде или `docs/`; тесты merge перенесены/дублированы.

### Фаза 8 — Вычистка дублей (фаза C)

- Удалить или упростить `[JobProjectFilesScanner](agent05-ui-control/API/Features/Jobs/Infrastructure/JobProjectFilesScanner.cs)` и `[chunkArtifactGroups.ts](agent05-ui-control/UI/src/utils/chunkArtifactGroups.ts)` только после стабильного контракта.
- **DoD:** меньше кода в Agent05; поведение Stats/файлов как golden-скриншоты или снимки JSON.

## Риски и «не потерять при реализации»

- **Две эвристики legacy:** Agent04 bootstrap vs Agent05 enricher — при переносе групп в Agent04 выровнять до одной политики (см. LEGACY_WORK_STATE_RECOVERY).
- **Гонки:** стрим gRPC + диск — `ChunkVirtualModelMerge` не удалять раньше времени.
- **Строгий путь job:** `Agent04:StrictChunkCancelPath` и `job_directory_relative` — не сломать при рефакторинге resolve root.
- **Семафор merge:** `SplitChunkMergeIntegrator` locks — не дублировать второй слой блокировок в сервисе без нужды.

## Ключевые файлы для навигации (указатели)


| Назначение        | Путь                                                                                                                                                                         |
| ----------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| gRPC транскрипции | `[agent04/Agent04/Services/TranscriptionGrpcService.cs](agent04/Agent04/Services/TranscriptionGrpcService.cs)`                                                               |
| Пайплайн          | `[agent04/Agent04/Features/Transcription/Infrastructure/TranscriptionPipeline.cs](agent04/Agent04/Features/Transcription/Infrastructure/TranscriptionPipeline.cs)`           |
| Work state        | `[agent04/Agent04/Features/Transcription/Infrastructure/TranscriptionWorkStateFile.cs](agent04/Agent04/Features/Transcription/Infrastructure/TranscriptionWorkStateFile.cs)` |
| Merge split       | `[agent04/Agent04/Features/Transcription/Infrastructure/SplitChunkMergeIntegrator.cs](agent04/Agent04/Features/Transcription/Infrastructure/SplitChunkMergeIntegrator.cs)`   |
| Референс групп UI | `[agent05-ui-control/UI/src/utils/chunkArtifactGroups.ts](agent05-ui-control/UI/src/utils/chunkArtifactGroups.ts)`                                                           |
| Референс сканера  | `[agent05-ui-control/API/Features/Jobs/Infrastructure/JobProjectFilesScanner.cs](agent05-ui-control/API/Features/Jobs/Infrastructure/JobProjectFilesScanner.cs)`             |
| Merge снимка      | `[agent05-ui-control/API/Features/Jobs/Infrastructure/ChunkVirtualModelMerge.cs](agent05-ui-control/API/Features/Jobs/Infrastructure/ChunkVirtualModelMerge.cs)`             |


## API сгруппированных файлов: сначала Agent04, потом Agent05

**Да, это запланировано именно в таком порядке:**

1. **Фаза A — Agent04:** добавить **новый** контракт (предпочтительно **gRPC** рядом с существующим `TranscriptionService`, либо отдельный метод в том же сервисе — на этапе реализации) + DTO «сгруппированные описания файлов» (эквивалент смысла `buildChunkGroups` + метаданные файлов). Реализация опирается на `ProjectArtifactService`. Покрыть **контрактными тестами** на sample-каталоги (в т.ч. из `agent-browser/runtime/`), не трогая UI.
2. **Фаза B — agent05-ui-control:** `JobsController` / клиент gRPC **проксируют** вызов в Agent04 и отдают UI **тот же JSON**, что сегодня (обратная совместимость), либо постепенно переводят UI на новое поле ответа.
3. **Фаза C (позже):** убрать дублирование на стороне Agent05 (`JobProjectFilesScanner` + логика групп в TS), когда ответ Agent04 признан полным и стабильным.

Таким образом **API в Agent04 готовится и стабилизируется первым**; перевод Agent05 — отдельный, контролируемый шаг без «большого взрыва».

## Группировка файлов: что внутри чанка / субчанка и что «снаружи»

Ниже — **целевая семантика**, совпадающая с текущей логикой UI `[chunkArtifactGroups.ts](agent05-ui-control/UI/src/utils/chunkArtifactGroups.ts)` и сканера `[JobProjectFilesScanner.cs](agent05-ui-control/API/Features/Jobs/Infrastructure/JobProjectFilesScanner.cs)`; `ProjectArtifactService` и новый API в Agent04 должны **воспроизводить те же правила** (чтобы фаза B не меняла поведение Stats).

### Шаг 1: множество индексов «логических чанков»

- Если в контексте job известно **totalChunks > 0** (из снимка / work state): строятся группы **строго для индексов `0 .. totalChunks - 1`** — даже если на диске нет файлов для части индексов (пустые группы допустимы).
- Иначе (узкие архивы без total): индекс собирается как **объединение**:
  - индексы из строк VM (поле `index` у строк основного и субчанка);
  - для файлов в `chunks/` и `chunks_json/`: либо **part_NNN** / **_part_NNN** в имени (приоритет над сырым `index`, чтобы не принять год из даты за номер чанка), либо поле **index** со сканера;
  - **parentIndex** из записей `split_chunks/…`.

### Шаг 2: для каждого индекса `N` — группа **основного чанка** (ChunkGroup)

В группу **попадают только** артефакты пайплайна «основного» чанка и операторского split для этого `N`:


| Поле группы                 | Источник на диске                                                                           | Правило                                                                                                                                                                                                                                                                                                   |
| --------------------------- | ------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Основное аудио / JSON чанка | `chunks/`*, `chunks_json/`*                                                                 | Файл относится к `N`, если совпал индекс по правилу выше (`part_*` или `file.index`).                                                                                                                                                                                                                     |
| **Субчанки**                | `split_chunks/chunk_N/sub_chunks/`*, `split_chunks/chunk_N/results/sub_chunk_*_result.json` | Все файлы с `parentIndex == N`, **кроме** merged (см. ниже), группируются по **subIndex** (`_sub_K` в имени или номер из `sub_chunk_K_result.json`). В каждой субгруппе отдельно списки audio (обычно в `sub_chunks`) и text/json.                                                                        |
| **Merged split**            | `split_chunks/chunk_N/chunk_N_merged.json` / `.md`                                          | Лежат в **корне** папки `chunk_N`, не внутри субгруппы; в модели UI — отдельное поле `mergedSplitFiles` у **родительской** группы `N`.                                                                                                                                                                    |
| Строка VM основного чанка   | не с диска файлов                                                                           | Сейчас: из **job.chunks.chunkVirtualModel** (строка с `isSubChunk !== true` и `index === N`). При API из Agent04: либо сервис подмешивает VM из Rentgen/work state по `jobId`, либо ответ содержит только файлы и Agent05/UI по-прежнему склеивают VM из снимка — решение фиксируется при контракте gRPC. |


**В группу чанка `N` не попадают:** корневые загрузки, корневые транскрипты, `intermediate_results/`, `converted_wav/`, чужие `chunk_M` при `M ≠ N`, а также файлы в `chunks/` с индексом, который **не вошёл** в множество шага 1 (они не отображаются внутри строки Stats для конкретного `N`; см. «осиротевшие» ниже).

### Шаг 3: группа **субчанка** (SubChunkGroup)

- Ключ: `(parentChunkIndex = N, subIndex = K или null, если парсер не вытащил индекс)`.
- **Audio:** файлы из `sub_chunks/` с распознанным `_sub_K` (или без индекса — одна группа с `subIndex: null`).
- **JSON/text:** те же файлы с `kind === text` в той же корзине; результаты из `results/` попадают в ту же субгруппу по `K`, с флагами «есть транскрипт» как сейчас в сканере.
- **Строка VM субчанка:** из снимка, строка с `isSubChunk` и `(parentChunkIndex, subChunkIndex)` — аналогично основному чанку.

Пустые субгруппы (нет ни audio, ни json) **отбрасываются** (как в `buildSubChunkGroups`).

### Что **не** входит в дерево групп для «Chunk controls (Stats)»

Эти категории **участвуют в других местах UI** (панель файлов проекта), а **не** в `buildChunkGroups`:

- **original** — исходное аудио в корне job;
- **transcripts** — корневые transcript*, `.md`, `.json` в корне по правилам сканера;
- **intermediate** — `intermediate_results/`;
- **converted** — `converted_wav/`.

В **[ProjectFilesView](agent05-ui-control/UI/src/components/ProjectFilesPanel.tsx)** при полном режиме порядок секций: **Original → Transcripts → Chunks → Chunk JSON → Intermediate → Converted → Split chunks** (плоский список всех `splitChunks`). То есть визуально «ниже секций чанков/JSON» идут **Intermediate, Converted**, затем **вся split_chunks одним списком** — это и есть «ниже чанков» в смысле панели файлов, не внутри виджета Stats.

**splitChunks в плоской секции:** те же файлы, что разложены по дереву в Stats; дублирование осознанное (Stats — иерархия по `N`/`sub`, панель — один каталог `split_chunks`).

### Особый случай: «осиротевшие» файлы

Файл лежит в `chunks/` или `chunks_json/`, но его индекс **не совпал** ни с одним `N` из шага 1 (или при `total > 0` индекс вне `0..total-1`): в **Chunk controls (Stats)** он **ни в одной группе не показывается**; в **Project files** он всё равно виден в секции Chunks / Chunk JSON.

---

## Поток результатов транскрипции: не «две рассылки от Rentgen»

**Краткий ответ:** в плане **не** заложено, что **Rentgen сам** рассылает события **в два направления** (UI по gRPC и в `ProjectArtifactService`). Такой вариант даёт риск рассинхрона, двойных записей и неочевидного порядка.

**Целевая схема:** один **оркестратор события** в Agent04 (пайплайн транскрипции / обработчик завершения чанка / ошибки — тот же слой, что сегодня обновляет store и узлы) **по очереди** или в одной согласованной операции:

1. **ProjectArtifactService** — запись **истины на диске**: артефакты, `transcription_work_state.json`, прочий персистентный контекст.
2. **Rentgen (`INodeModel`)** — обновление **узловой** модели (прогресс, статус узла чанка/субчанка, метаданные), из которой как и сейчас собирается VM в `BuildChunkVirtualModel`.

Дальше **UI** (как сейчас) получает обновления **не напрямую от Rentgen**, а через цепочку **Agent04 gRPC** (`StreamJobStatus` / `GetJobStatus` → снимок в Agent05 → SSE/HTTP). Достаточно, чтобы к моменту формирования ответа **диск и узлы были согласованы** общим порядком вызовов; отдельного «дублирующего канала от Rentgen в UI» не требуется.

**Если Rentgen вынесен в отдельный процесс:** тогда либо (а) тот же оркестратор шлёт **два вызова** (в сервис персистенции и в Rentgen), либо (б) один из слоёв подписан на события другого — но **источник события** остаётся пайплайн/команда, а не «внутренняя логика Rentgen как единственный инициатор записи на диск».

Итог: **два потребителя результата** (диск через `ProjectArtifactService` и виртуальная модель через Rentgen) — да; **два исходящих канала именно от Rentgen** — нет, это не целевой дизайн плана.

## Связь с предыдущим описанием потока

Текущее поведение (VM из Rentgen в `BuildChunkVirtualModel`, merge в Agent05, disk enricher в Agent05) остаётся справочным до поэтапной замены: целевое состояние — **ProjectArtifactService** как владелец дискового контекста и группировок; **Rentgen** и **ChunkVirtualModelMerge** — соседние слои с сохранённой ответственностью на время миграции. После выравнивания порядка записи (диск + узлы) ответ gRPC автоматически отражает согласованное состояние для UI без отдельной «второй рассылки» от Rentgen.