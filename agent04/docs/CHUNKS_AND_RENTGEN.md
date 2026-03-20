# Чанки, под-чанки и RENTGEN (аудит §5.7)

Документ фиксирует фактическое состояние **Agent04** и связку с **agent05** (`ChunkState`, SSE) по состоянию на закрытие пункта плана 5.7.

## 1. Парсинг и жизненный цикл чанков

| Аспект | Реализация |
|--------|------------|
| Нарезка | `pre_split` в конфиге: `IAudioChunker.ProcessChunksForFileAsync` → файлы в `split_workdir` (по умолчанию `chunks/`), шаблон имён `{base}_part_%03d.m4a`. При `pre_split: false` — один логический чанк (весь файл). |
| Индексы | 0-based в цикле пайплайна; соответствуют порядку в `ChunkInfo` после нарезки. |
| Статус job | `IJobStatusStore`: `TotalChunks`, `ProcessedChunks`, `CurrentPhase`, `ProgressPercent`; стрим gRPC дублирует эти поля. |
| agent05 | `TranscriptionRefinerPipeline` копирует в `JobSnapshot.Chunks`: `Total`, `Completed` = `[0..processed-1]`, `Active` = `[processed]` при `Running` и `processed < total`. |

## 2. Приостановка / отмена чанка

| Аспект | Реализация |
|--------|------------|
| API | gRPC `ChunkCommand` с `ChunkCommandAction.Cancel`. |
| Изоляция по job | `ICancellationManagerFactory` → per-job каталог под workspace (не общий singleton `cancel_signals` для всех job). |
| Поведение пайплайна | Перед постановкой в очередь: если `IsCancelled(i)` — `StepStart`/`StepComplete(Cancelled)` в RENTGEN, чанк не в очереди; в merge не попадает. |
| Итог job | Если хотя бы один чанк обработан, идёт merge; полная отмена всех чанков — граничный случай (пустой `results`), см. код пайплайна. |

## 3. Операторский split и `split_chunks/chunk_N/…`

| Аспект | Статус |
|--------|--------|
| Каталоги `split_chunks/chunk_*` из **JobProjectFilesScanner** (agent05) | В Agent04 **нет** отдельного пайплайна «оператор разбил чанк N на подчанки» с записью в эту структуру. |
| `pre_split` | Это **автоматическая** нарезка **исходного** файла на много чанков в `chunks/`, не иерархия chunk → subchunks для UI scanner. |
| gRPC `ChunkCommandAction.Split` | При `split_parts >= 2` — ffmpeg-нарезка в `split_chunks/chunk_{N}/sub_chunks/`; метаданные оператора в RENTGEN. |

## 4. Парсинг подчанков по отдельности

- **Нет** второго уровня «подчанк» в смысле отдельных вызовов API после split оператора: каждая единица работы — один элемент `ChunkInfo` из `PrepareChunksAsync`.
- `ITranscriptionMerger` внедрён в `TranscriptionPipeline`, но **в основном цикле транскрипции не вызывается** (слияние сегментов идёт через `ITranscriptionOutputWriter.AppendSegmentsToMarkdown` и порядок чанков).

## 5. RENTGEN / виртуальная модель узлов

| Событие | Узлы |
|---------|------|
| Пайплайн | `jobId` (job) → `jobId:transcribe` (phase) → `jobId:transcribe:chunk-{i}` (chunk), плюс фазы `chunking`, `merge`. Атрибуты `[XRayNode]` на статических хелперах. |
| **ChunkCommand** | После успешной валидации вызывается `EnsureNode` на том же `chunkNodeId`, с метаданными `operator_action` (`cancel` / `skip` / `retranscribe` / `split`) и `operator_action_at` (UTC ISO). Если `INodeModel` не зарегистрирован в хосте — запись пропускается. |

Операторские команды **не** дублируются отдельным gRPC для чтения дерева; при появлении read API узлов по gRPC — эти поля уже в `Metadata` узла чанка.

## 6. Тесты agent05

- См. `API.Tests/ChunkActionsControllerTests.cs`: `POST /api/jobs/{id}/chunk-actions` с моком `ITranscriptionServiceClient`.

## 7. Восстановление state-файла (legacy)

См. [LEGACY_WORK_STATE_RECOVERY.md](LEGACY_WORK_STATE_RECOVERY.md) — эвристика §1a, если `transcription_work_state.json` отсутствует.
