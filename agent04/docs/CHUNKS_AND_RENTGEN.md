# Чанки, под-чанки и RENTGEN (аудит §5.7)

Документ фиксирует фактическое состояние **Agent04** и связку с **agent05** (`ChunkState`, SSE) по состоянию на закрытие пункта плана 5.7.

## 1. Парсинг и жизненный цикл чанков

| Аспект | Реализация |
|--------|------------|
| Нарезка | `pre_split` в конфиге: `IAudioChunker.ProcessChunksForFileAsync` → файлы в `split_workdir` (по умолчанию `chunks/`), шаблон имён `{base}_part_%03d.m4a`. При `pre_split: false` — один логический чанк (весь файл). |
| Индексы | 0-based в цикле пайплайна; соответствуют порядку в `ChunkInfo` после нарезки. |
| Статус job | `IJobStatusStore`: `TotalChunks`, `ProcessedChunks`, `CurrentPhase`, `ProgressPercent`; стрим gRPC дублирует эти поля. |
| agent05 | `TranscriptionRefinerPipeline` копирует в `JobSnapshot.Chunks`: `Total`, `Completed` = `[0..processed-1]`, `Active` = `[processed]` при `Running` и `processed < total`. |

## 2. Приостановка / отмена чанка и субчанка

| Аспект | Реализация |
|--------|------------|
| API | gRPC `ChunkCommand` с `ChunkCommandAction.Cancel`. **Основной чанк:** `chunk_index` = N, `sub_chunk_index` **не задан** в Xtract (в gRPC передаётся **-1**). **Субчанк:** `chunk_index` = родитель N, `sub_chunk_index` = K ≥ 0 → файл `cancel_signals/cancel_sub_{N}_{K}.flag`. |
| Изоляция по job | `ICancellationManagerFactory` → per-job каталог под workspace (не общий singleton `cancel_signals` для всех job). |
| Поведение пайплайна | Перед постановкой в очередь: если `IsCancelled(i)` — `StepStart`/`StepComplete(Cancelled)` в RENTGEN, чанк не в очереди; в merge не попадает. **Транскрипция субчанка:** `TranscribeSplitSubChunkAsync` опрашивает `IsSubChunkCancelled(parent, sub)` и отменяет HTTP-транскрипцию; work state и VM → `Cancelled`. |
| Итог job | Если хотя бы один чанк обработан, идёт merge; полная отмена всех чанков — граничный случай (пустой `results`), см. код пайплайна. |
| Повтор субчанка | Повторный `transcribe_sub` для того же K после `Failed` / `Cancelled` (кнопка в UI). |

## 3. Операторский split и `split_chunks/chunk_N/…`

| Аспект | Статус |
|--------|--------|
| Каталоги `split_chunks/chunk_*` из **JobProjectFilesScanner** (agent05) | В Agent04 **нет** отдельного пайплайна «оператор разбил чанк N на подчанки» с записью в эту структуру. |
| `pre_split` | Это **автоматическая** нарезка **исходного** файла на много чанков в `chunks/`, не иерархия chunk → subchunks для UI scanner. |
| gRPC `ChunkCommandAction.Split` | При `split_parts >= 2` — нарезка в `{split_chunks_dir}/chunk_{N}/sub_chunks/` (дефолт `split_chunks`), границы и overlap как в agent01 `ChunkSplitter`, имена `{base}_sub_{NN}`; транскрипцию субчанков запускает отдельно `TranscribeSub`. |

## 4. Подчанки после операторского split и merge

- gRPC `ChunkCommandAction.TranscribeSub` + `TranscriptionPipeline.TranscribeSplitSubChunkAsync` пишут `split_chunks/chunk_N/results/sub_chunk_XX_result.json` и строки work state v2.
- Когда **все** ожидаемые субчанки для `chunk_N` имеют результат на диске, `SplitChunkMergeIntegrator` вызывает `ITranscriptionMerger.MergeTranscriptions`, пишет `split_chunks/chunk_N/chunk_N_merged.json` и `.md`, и при наличии job-level `openai_response.json` **пересобирает** `transcript.md` и combined JSON, подставляя merged-сегменты вместо родительского чанка (см. `SplitChunkMergeIntegrator.cs`).
- Восстановление `ChunkCommand` после рестарта Agent04: если задан `job_directory_relative` и каталог job существует, для **Split / TranscribeSub / Retranscribe** допускается «теневой» `JobStatus` в памяти, чтобы не возвращать gRPC `NotFound` при пустом `IJobStatusStore`.

## 5. RENTGEN / виртуальная модель узлов

| Событие | Узлы |
|---------|------|
| Пайплайн | `jobId` (job) → `jobId:transcribe` (phase) → `jobId:transcribe:chunk-{i}` (chunk), плюс фазы `chunking`, `merge`. Атрибуты `[XRayNode]` на статических хелперах. |
| **ChunkCommand** | После успешной валидации вызывается `EnsureNode` на том же `chunkNodeId`, с метаданными `operator_action` (`cancel` / `skip` / `retranscribe` / `split`) и `operator_action_at` (UTC ISO). Если `INodeModel` не зарегистрирован в хосте — запись пропускается. |

Операторские команды **не** дублируются отдельным gRPC для чтения дерева; при появлении read API узлов по gRPC — эти поля уже в `Metadata` узла чанка.

## 6. Тесты Agent04 (этап плана §8)

- **`SubChunkResultReaderWriterTests`** — round-trip `sub_chunk_XX_result.json` ↔ `TranscriptionResult`.
- **`TranscriptionMergerTests`** — слияние двух субрезультатов с `parentChunkOffset` и абсолютными таймкодами.
- **`OperatorChunkSplitPlannerTests`** — границы сегментов и overlap как у agent01.
- **`CancellationManagerSubChunkTests`** — файлы `cancel_sub_{parent}_{sub}.flag` и опрос.
- Операторский **Split** по gRPC по-прежнему **не** вызывает транскрипцию API (инвариант `split-no-auto-transcribe`); автоматический тест на отсутствие вызовов — по желанию интеграционный.

## 7. Тесты agent05

- См. `API.Tests/ChunkActionsControllerTests.cs`: `POST /api/jobs/{id}/chunk-actions` с моком `ITranscriptionServiceClient`.

## 8. Восстановление state-файла (legacy)

См. [LEGACY_WORK_STATE_RECOVERY.md](LEGACY_WORK_STATE_RECOVERY.md) — эвристика §1a, если `transcription_work_state.json` отсутствует.
