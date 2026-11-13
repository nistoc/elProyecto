# Changelog

All notable changes to the Agent04 project are documented here.

## [Unreleased]

- **Пауза по умолчанию 1,0 с:** в общем `config/default.json` `silence_keep_sec` теперь `1.0` (было `0.2`). На дорожке одного голоса при 0,2 с модель склеивала фразы, разделённые долгими паузами, и ставила строку на 6–20 с раньше; при 1,0 с проверенные фразы стоят в пределах 0,5 с от прогона без сжатия (136 строк против 112), цена дорожки выше примерно на 20 %. Другое значение для одного задания — через настройки задания.
- **Настройки одного задания (`job_settings.json`):** файл в папке задания (его пишет agent05 при загрузке) кладётся поверх общего config — общий `config/default.json` для остальных заданий не меняется. Ключи: `silence_compress_enabled`, `silence_keep_sec` (0,05–5), `model`. Файл применяют **SubmitJob** и команды чанков, читающие config; файл, который нельзя прочитать как настройки (не JSON, чужой ключ, значение вне границ), отклоняет запрос — задание не уходит молча с другими настройками. Модели, отличной от общей, подставляется её формат ответа (diarize — `diarized_json` + `chunking_strategy: auto`, `whisper-1` — `verbose_json`, прочие — `json`). Новое: **`JobSettingsFile`**, `TranscriptionConfig.WithOverrides`. Тесты: **`JobSettingsFileTests`**.
- **Модель на вызов:** `TranscriptionClientOptions.Model` — клиент берёт модель из config задания (`model`), а не только ту, с которой создан; раньше ключ `model` в config задания на запрос не влиял. При общем `default.json` модель та же, что была. Тесты: **`OpenAITranscriptionClientModelTests`**.
- **Пауза не длиннее настоящей:** вместо паузы остаётся `silence_keep_sec`, но не больше самой паузы (`SpeechTimeMap.GapFor`); при `silence_keep_sec: 1.0` пауза 0,5 с остаётся 0,5 с, а не растёт до 1 с. Сборка файла делает по одному файлу тишины на длину паузы (мс). При значениях по умолчанию (0,2 с при минимуме детектора 0,3 с) файл собирается как раньше. Тесты: **`SilenceGapTests`** (в том числе сборка настоящим ffmpeg).
- **Операторский split — место части внутри chunk:** результат sub-chunk сохранялся с `offset = 0`, а план разбиения нигде не записывался, поэтому слияние (`chunk_N_merged.*` и общий transcript) ставило все части в начало chunk и их строки перемешивались (на уроке 23.10: четыре части по 40 с встали на 0–40 с вместо 0–158 с). Теперь **Split** пишет **`split_chunks/chunk_N/sub_chunks_timeline.json`** (`offset_sec`, `duration_sec`, `emit_guard_sec` каждой части), **TranscribeSub** кладёт смещение в `sub_chunk_XX_result.json`, а слияние берёт место части из этого файла. Для split, сделанного раньше, место восстанавливается по длительности звука (chunk и первая часть) и файл дописывается с `derived: true`; **rebuild_split_merged** чинит такой transcript без новой транскрипции. Если место установить нельзя, слияние отвечает `split_merge_sub_offsets_unknown` и ничего не пишет. Тесты: **`SubChunkTimelineTests`**, `SplitChunkMergeIntegratorRebuildTests`.
- **Карта речи (время после сжатия тишины):** при `silence_compress_enabled` модель слышит укороченный файл и отвечает в его секундах, поэтому строки transcript стояли на сжатой шкале (на уроке 20,9 мин последняя строка была на 1073,7 с вместо 1249,7 с). Теперь сжатие собирает файл по единому плану **`SpeechTimeMap.Plan`** и сохраняет таблицу кусков в **`timeline/speech_map.json`** (`source_start_sec`, `source_end_sec`, `output_start_sec`); **`TranscriptionOutputWriter`** переводит время каждой строки обратно на шкалу записи — в первом прогоне, в **rebuild_combined** и при split merge. Сегмент, начавшийся в последние 0,35 с куска и продолжающийся в следующем, ставится на начало следующего куска. Прогон без сжатия удаляет таблицу. JSON чанков хранит времена модели как есть. Проверка на настоящих файлах: положение 156 и 227 кусков в сжатом файле совпало с таблицей до sample; у 84 строк смешанной записи, найденных по тексту в дорожках участников, медиана расхождения времени 0,32 с (было 116 с). Тесты: **`SpeechTimeMapTests`**, `ChunkTimelineTests.Rebuild_translates_times_of_a_silence_compressed_job_to_the_recording`.
- **Шкала времени при rebuild:** `rebuild_combined` и `retranscribe` брали chunk с диска как `ChunkInfo(path, 0, 0)`, поэтому заново созданный `chunks_md/*.md` начинался с нуля в каждом chunk и повторял перекрытие с предыдущим. Теперь смещение и emit guard каждого chunk пишутся после нарезки в **`timeline/chunks.json`** (`ChunkTimelineFile`) и читаются оттуда; для заданий без этого файла они выводятся из длительности первого chunk и `chunk_overlap_sec`. Существующие `chunks_md` по-прежнему не перезаписываются. Тесты: `ChunkTimelineTests`.
- **ffmpeg и региональные настройки:** `AudioChunker` собирал `-ss` / `-t` через `$"{t:F3}"`; при десятичной запятой (ru-RU, es-ES) ffmpeg отвечал `Invalid duration for option ss: 0,000`, и задание падало на нарезке, до транскрибации. Секунды теперь форматируются с `InvariantCulture` (`AudioChunker.FfmpegSeconds`), `FormatMb` — тоже. Тесты: `AudioChunkerFfmpegSecondsTests`.
- **Параллельная транскрипция (план):** скользящее окно — `Parallel.ForEachAsync` с `MaxDegreeOfParallelism = parallel_transcription_workers`; `StepComplete` для успеха сразу после чанка; локальный фейл одного чанка не отменяет остальные; глобальный стоп только по **401/403**, `insufficient_quota` и т.п. (`TranscriptionWorkflowAbort`). Порядок markdown/JSON по индексу через буфер `nextAppendIndex`. Файл **`transcription_work_state.json`** (атомарная запись); bootstrap legacy без state — **`TranscriptionWorkStateBootstrapper`** (см. **`docs/LEGACY_WORK_STATE_RECOVERY.md`**). **`pending_chunks.json`** + gRPC **`EnqueueTranscriptionWork`**. **`StreamJobStatus`**: пауза **2000 ms** (конфиг **`Agent04:StreamJobStatusPollMs`**). VM: дополнительные строки для узлов **`…:transcribe:chunk-{i}:sub-{j}`** в **`chunk_virtual_model`** (`is_sub_chunk`, parent/sub). Тесты: **`TranscriptionWorkflowAbortTests`**, **`PendingChunksReaderTests`**.
- **JSON артефакты и Unicode:** сериализация сырого ответа транскрипции (chunk/combined JSON, кэш-манифест, fallback `ParseSegments`, webhook) через **`TranscriptionJsonSerializerOptions`** с **`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`** — в файлах и телах запросов кириллица и знаки вроде **¿** больше не превращаются в `\uXXXX` (как **`ensure_ascii=False`** в agent01). Тест **`TranscriptionJsonSerializerOptionsTests`**.
- **`TranscriptionConfig.Get<T>`:** значения вроде **`parallel_transcription_workers`** и **`reencode_bitrate_kbps`** из JSON (числа как `double`) корректно читаются для **`int?`** / **`double?`**; раньше `Get<int?>` возвращал `null` → в пайплайне срабатывал fallback **4** вместо значения из `config/default.json`. Тест **`TranscriptionConfigDefaultJsonTests`** + копия **`default.json`** в выход тестов.
- **Отмена чанка во время HTTP:** пока идёт **`TranscribeAsync`**, фоновый опрос **`ICancellationManager.IsCancelled`** отменяет связанный **`CancellationToken`**, чтобы оборвать запрос к API (кооперативно). Узел чанка завершается в **`Cancelled`**; отмена задания целиком (`CancellationToken` пайплайна) по-прежнему пробрасывается. Тест **`ChunkCancelWhileTranscribingTests`**.
- **Параллельная транскрипция:** ключ **`parallel_transcription_workers`** ограничивает число чанков, обрабатываемых одновременно (по умолчанию в примере конфига **6**, clamp **1–64**, предупреждение при **> 32**). Запись кэша/манифеста сериализована **`SemaphoreSlim(1)`** на задание. Порядок слияния markdown/JSON по-прежнему по индексу чанка.
- **Логи OpenAI HTTP:** старт/успех/ошибка с `AgentJobId`, `ChunkIndex`, `ParallelWorkersConfigured`, счётчиком **`InFlight`**, длительностью; при ошибке — статус, **Category** (`auth`, `rate_limit`, …), усечённое тело ответа; отдельно таймаут vs отмена по токену. Секреты в лог не пишутся.
- **Диагностика «тишины» после сплита:** `TranscriptionPipeline` пишет сводку первого батча и для чанка **0** — старт `ProcessChunkAsync` (файл, параллелизм).
- **gRPC `StreamJobStatus` / `GetJobStatus`:** в ответ добавлено **`chunk_virtual_model`** — по одной записи на чанк (`chunk_index`, `started_at`, `completed_at`, `state`) из виртуальной модели (`{jobId}:transcribe:chunk-{i}`). Поток шлёт обновление и при изменении только чанков (таймеры в UI без сброса при F5).
- **Chunk split после `convert_to_wav`:** шаблон `chunk_naming` с расширением `.m4a` больше не приводит к ошибке ffmpeg («pcm_s16le … not supported in container ipod»): при нарезке с `-c copy` расширение выходных чанков выравнивается с расширением исходного файла (например WAV). См. **`AudioChunkNaming.AlignOutputExtensionWithSource`**, тесты **`AudioChunkNamingTests`**.
- **Артефакты и отмена чанков в каталоге аудио:** корень артефактов = родитель входного файла под **`WorkspaceRoot`** (`TranscriptionPaths.ResolveArtifactRoot`); пайплайн пишет md/json, cache, chunks, wav, chunks_json и сигналы отмены в этот каталог. **`IJobArtifactRootRegistry`**: регистрация **`agent04JobId → artifactRoot`** при приёме задания, снятие при терминальном статусе. **`ChunkCommandRequest.job_directory_relative`**: валидация под workspace; разрешение базы отмены: registry → proto → legacy корень workspace (предупреждение для Running без registry; опционально **`Agent04:StrictChunkCancelPath`**). Тесты: **`TranscriptionPathsTests`**, **`JobArtifactRootRegistryTests`**.
- **Логи старта транскрипции:** `TranscriptionGrpcService` пишет в консоль входящий `SubmitJob` (корень workspace, пути конфига и входа), причины отказа до `RpcException`, успешное принятие задания; при падении пайплайна в фоне — `LogError` с исключением и путём к входному файлу.
- **gRPC `SubmitJob` / конфиг:** если `config/...` нет под **WorkspaceRoot** (например, корень только с папками заданий), путь ищется во **вторую очередь** относительно **content root** приложения Agent04 (`config/default.json` рядом с проектом/сборкой). В **Agent04.csproj** добавлено копирование `config/**` в выходную папку для `dotnet run` / publish. Устраняет `InvalidArgument: Config file not found` при вызове из XtractManager с общим `WorkspaceRoot` на каталог job-файлов.
- **RENTGEN:** при обработке **`ChunkCommand`** (все действия с валидным `chunk_index`) вызывается **`EnsureNode`** для узла `{jobId}:transcribe:chunk-{i}` с метаданными `operator_action` и `operator_action_at` (если зарегистрирован `INodeModel`). Документ аудита: **`docs/CHUNKS_AND_RENTGEN.md`**.
- **gRPC `ChunkCommand`:** отмена обработки чанка по индексу (`ChunkCommandAction.Cancel`) через фабрику **`ICancellationManagerFactory`** (каталог сигналов на задание). Действия **Skip / Retranscribe / Split** пока отвечают `ok=false`, `not_implemented`.

## [0.1.0] — 2025-03-10

### Added

- **Skeleton (Stage 0):** .NET 9 solution, ASP.NET Core host, vertical slice `Features/Transcription` (Domain, Application, Infrastructure), config from `config/default.json`, Ninject references.
- **Domain and config (Stage 1):** Domain models `ASRSegment`, `ChunkInfo`, `TranscriptionResult`; `TranscriptionConfig` with JSON load and `env:VAR` resolution.
- **Audio (Stage 2):** `IAudioUtils` (ffmpeg/ffprobe, duration, convert to WAV, reencode), `IAudioChunker` (slice with overlap, process chunks for file).
- **Cache (Stage 3):** `ITranscriptionCache` with manifest, SHA256 fingerprint, get/set by chunk; file-based implementation.
- **OpenAI client (Stage 4):** `ITranscriptionClient` with retry and fallback models; HTTP client for OpenAI transcription API; segment parsing.
- **Output (Stage 5):** `ITranscriptionOutputWriter` — Markdown (>>>>>>>, segments, <<<<<) and JSON (combined + per-chunk) in agent01-compatible format.
- **Merger and support (Stage 6):** `ITranscriptionMerger` (merge sub-chunks, overlap, deduplication), `ICancellationManager` (file-based cancel flags), `IChunkProgress` (CLI progress).
- **Pipeline (Stage 7):** `ITranscriptionPipeline` — orchestration: WAV conversion, chunking, cache, API, output; optional progress updates via `IJobStatusStore`.
- **CLI (Stage 8):** Entry point with `--config`; load config, run pipeline for configured files, print output paths.
- **REST API (Stage 9):** `POST /api/transcription/jobs`, `GET /api/transcription/jobs/{id}`, `GET /api/transcription/jobs`; in-memory job store; background execution; OpenAPI.
- **gRPC (Stage 9b):** `Proto/transcription.proto` — `SubmitJob`, `GetJobStatus`, `StreamJobStatus`; gRPC server on same host.
- **Job status and monitoring (Stage 9c):** `IJobStatusStore`; pipeline accepts optional `jobId` and store to report progress (phase, percent); REST and gRPC return live status.
- **Job query / virtual model (Stage 9d):** Tags on submit; `IJobQueryService`; `GET /api/transcription/jobs/query?tag=...&status=...&from=...&to=...`; list with full status per job.
- **RENTGEN fact (Stage 9e):** Document `docs/RENTGEN_IMPLEMENTATION.md` and script `scripts/register-rentgen-fact.ps1` to register it in Knowledge Store via `POST /api/facts`.
- **Documentation (Stage 10):** README (run modes, config, REST/gRPC, monitoring), CHANGELOG.

### Notes

- Only nuget.org packages. No outgoing gRPC clients implemented (stub for future).
- XRay attributes (optional) for virtual model are not implemented; design documented in RENTGEN_IMPLEMENTATION.md.

## [0.2.0] — 2025-03-11

### Added

- **gRPC tags:** `SubmitJobRequest.tags` in proto; gRPC service passes tags to job store.
- **ProblemDetails:** REST error responses (400, 404) use RFC 7807 ProblemDetails.
- **Job query caching:** `CachingJobStatusStore` decorator with TTL (short for Running/Pending, longer for Completed/Failed) and invalidation on job update.
- **Ninject composition root:** All Transcription bindings in `Composition/Agent04Module`; host uses `NinjectServiceProviderFactory` and `ConfigureContainer<IKernel>`.
- **Virtual node model:** `INodeModel` (EnsureNode, StartNode, CompleteNode), `INodeQuery` (GetByScope, GetTreeByScope), `InMemoryNodeStore`; pipeline records job → chunking → transcribe → chunk-N → merge; **GET /api/transcription/jobs/{id}/nodes** (`?tree=true` for hierarchy).

## [0.3.0] — 2025-03-11

### Added

- **Workspace root:** Required `WorkspaceRoot` (or `workspace_root`) in appsettings or environment; validated at startup (app exits if directory does not exist). All request paths are relative to this root.
- **Relative paths only:** `configPath` and `inputFilePath` in REST and gRPC are resolved relative to workspace root. Absolute `inputFilePath` is rejected with 400.
- **Unique transcript names:** Output transcript filenames include job id (e.g. `{base}_{jobId}_transcript.md`) to preserve history; pattern supports `{jobId}`.
- **Proto comment:** `SubmitJobRequest` documented: paths are relative to instance workspace_root from config.
- **X-Caller-Id:** Request header read and stored in job status; exposed in REST/gRPC status and in tracing (Activity tag `transcription.caller_id`).
- **TotalChunks / ProcessedChunks:** Job status includes chunk counts; pipeline updates them during transcription.
- **Callback URL:** Optional `callback_url` in SubmitJob (REST/proto); HTTP POST invoked on job Completed or Failed.
- **gRPC QueryJobs:** New RPC `QueryJobs(QueryJobsRequest)` in proto; service implements query by semantic key (tag) and filters.
- **Outbound gRPC stub:** Placeholder for future notification service; no real calls yet.
- **JobQuery slice:** Logic moved to `Features/JobQuery` (IJobQueryService, JobQueryService); `QueryBySemanticKey(semanticKey, status, from, to, limit, offset)`; REST GET /jobs/query and gRPC QueryJobs use it.
- **Rate limiting:** Configurable fixed-window policy (`RateLimit:PermitLimit`, `RateLimit:WindowSeconds` in appsettings); 429 when limit exceeded; `[EnableRateLimiting("api")]` on transcription controller.
- **Node progress and metadata:** Pipeline calls `UpdateNodeProgress(jobId, percent, phase)`; on success, root node metadata gets `md_output_path` and `json_output_path`.
- **Tracing (XRay/Activity):** Activity tags in pipeline (`job.id`, `file.input`) and controller (`transcription.caller_id`, `job.id`) for distributed tracing.
- **Unit tests:** Project `Agent04.Tests` (xUnit); tests for `AudioUtils` (CalculateSegmentTime, FormatMb, WhichOr). Solution includes the test project.

### Changed

- **Plan compliance:** `docs/PLAN_COMPLIANCE_AND_TODO.md` updated: sections 2.1–2.6 and "Итог" reflect implemented items (X-Caller-Id, callback, gRPC QueryJobs, JobQuery slice, rate limit, TotalChunks/ProcessedChunks, node progress/metadata, XRay, unit tests).
- **appsettings.json:** Added `RateLimit` section (PermitLimit, WindowSeconds). `Program.cs`: `using Microsoft.AspNetCore.RateLimiting` for fixed-window limiter.

### Removed

- **CLI mode:** Support for `--config` and one-shot console run removed. Agent04 runs only as a web service (HTTP + gRPC). Use `dotnet run` to start the host.

## [Unreleased]

### Removed

- **HTTP REST / OpenAPI:** `TranscriptionController`, `MapControllers`, `AddOpenApi`, `UseHttpsRedirection`, rate limiting (`AddRateLimiter` / `UseRateLimiter`), корневой `GET /`. Внешний контракт — **только gRPC** на **http://localhost:5032** (h2c). Удалён пакет `Microsoft.AspNetCore.OpenApi`; REST-only DTO `Features/Transcription/Application/SubmitJobRequest.cs`. Из `appsettings.json` убрана секция `RateLimit`.

### Changed

- **Один порт:** `launchSettings` указывает **5032**; Kestrel слушает только его с `HttpProtocols.Http2`. Потребители (например **agent05**) должны задавать `Agent04:GrpcAddress` = `http://localhost:5032`.

### Added

- **XRay attributes:** `[XRayNode(Ensure|Start|Complete)]` mark pipeline methods that update the virtual model (RENTGEN). Methods `EnterStep`, `CompleteStep`, `EnsureAndStartJobRoot`, etc. are decorated; explicit `INodeModel` calls remain, attributes document and reserve for future interceptor.
- **Tag = node id in virtual model:** GET /jobs/{id}/nodes accepts optional query `tag` (node id/name); returns that single node or 404. `INodeQuery.GetNodeByScopeAndId(scopeId, nodeId)` added.

### Changed

- **Semantic key vs tag:** Job list filter parameter renamed from `tag` to `semanticKey` in REST (GET /jobs/query?semanticKey=...) and in gRPC (`QueryJobsRequest.semantic_key`). `JobListFilter.Tag` renamed to `SemanticKey`. The word "tag" in the API now means only the node identifier when querying node status (GET /jobs/{id}/nodes?tag=...).
- **XRay clarification:** Removed `Activity.Current?.SetTag(...)` from pipeline and controller (those were tracing tags, not XRay). XRay in Agent04 = attributes on business methods for the virtual model; see docs/RENTGEN_IMPLEMENTATION.md.
- **Documentation:** RENTGEN_IMPLEMENTATION.md and PLAN_COMPLIANCE_AND_TODO.md updated for XRay attributes and tag/semanticKey semantics.
