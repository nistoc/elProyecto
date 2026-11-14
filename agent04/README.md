# Agent04 — Transcription service (.NET)

.NET reimplementation of the agent01 transcription pipeline: OpenAI transcription (diarization), ffmpeg chunking, cache by fingerprint, merge, Markdown/JSON output. Clean Architecture, vertical slice `Features/Transcription`. **Web service only** (no CLI); **public API — gRPC only** (HTTP REST/OpenAPI removed; orchestrators such as agent05 use gRPC).

## Requirements

- .NET 9
- ffmpeg / ffprobe on PATH (or set in config)
- OpenAI API key (e.g. `env:OPENAI_API_KEY` in config)

## Configuration

- **Workspace root (required):** set `WorkspaceRoot` or `workspace_root` in `appsettings.json` or environment to an **absolute path** of the workspace directory. The app checks that this directory exists at startup and exits if it does not. All paths in gRPC requests are relative to this root.
- **Job config:** same contract as agent01. Example: `Agent04/config/default.json`. Keys include `file`/`files`, `openai_api_key` (e.g. `env:OPENAI_API_KEY`), `model`, `md_output_path`, `raw_json_output_path`, `cache_dir`, `split_workdir`, etc. Paths in job config are relative to the workspace root.

## Running

Start the web host:

```bash
cd Agent04
dotnet run
```

Ensure `appsettings.json` (or environment) contains a valid `WorkspaceRoot` path before starting.

- **Console logging:** timestamps are **UTC** (see `Program.cs`, `SimpleConsole` + `UseUtcTimestamp`).

- **Порт (профили http/https):** **http://localhost:5032** — только **HTTP/2 (h2c)** для gRPC. На Windows без TLS для gRPC нужен отдельный endpoint с h2c (см. `Program.cs` → `ConfigureKestrel`).

## gRPC API

Paths in requests are relative to the instance `workspace_root` (from config):

- **TranscriptionService.SubmitJob** — submit job (`config_path`, `input_file_path`, optional `tags`, optional `callback_url`).
- **TranscriptionService.GetJobStatus** — status by `job_id`.
- **TranscriptionService.StreamJobStatus** — server stream of status updates until terminal state.
- **TranscriptionService.QueryJobs** — list/filter jobs (semantic key, status, time range, limit/offset).
- **TranscriptionService.ChunkCommand** — операторские действия по чанку (`action`, `chunk_index`, опционально **`split_parts`** для **Split**); **Cancel** помечает чанк к пропуску (per-job cancel dir) и отменяет in-flight HTTP через токен; **Skip / Retranscribe** могут отвечать `not_implemented`. **Split** при `split_parts >= 2` режет выбранный файл чанка (ffmpeg) в `split_chunks/chunk_{N}/sub_chunks/` через **`IProjectArtifactService`**. **DeleteSubChunk** (`CHUNK_COMMAND_ACTION_DELETE_SUB_CHUNK`, `sub_chunk_index`) удаляет артефакты одного субчанка (audio, result json, merged, cancel flag, строка work state); при **Running** в Rentgen для узла субчанка — `FailedPrecondition`. Метаданные команды пишутся в виртуальную модель узлов (RENTGEN), если зарегистрирован `INodeModel`.
- **TranscriptionService.EnqueueTranscriptionWork** — записывает `pending_chunks.json` (индексы чанков для следующего прогона пайплайна) рядом с артефактами job; при следующем **SubmitJob** список пересекается с доступными индексами.
- **TranscriptionService.GetChunkArtifactGroups** — только чтение: группы файлов чанков / split (как Stats в agent05), без строк VM; `total_chunks` в запросе можно взять из снимка job или 0 (тогда из `transcription_work_state.json` / эвристика по файлам).

Proto: `Agent04/Proto/transcription.proto`.

Контракт персистентного слоя артефактов job на диске: **`docs/PROJECT_ARTIFACT_SERVICE.md`** (`IProjectArtifactService` / `ProjectArtifactService`).

> **RENTGEN / узлы:** ранее дерево узлов отдавалось по HTTP (`GET .../nodes`). Внешнего HTTP больше нет; для списка заданий используйте **QueryJobs**. При необходимости дерева узлов — расширение `.proto` или отдельный read-сервис (см. `docs/RENTGEN_IMPLEMENTATION.md`). Аудит чанков, под-чанков и связи с RENTGEN — **`docs/CHUNKS_AND_RENTGEN.md`**.

## Parallel transcription & HTTP logs

- **`parallel_transcription_workers`** in the job config caps how many chunks are transcribed **at the same time** (clamped to **1–64**; default in repo sample **6**). Values **> 32** log a warning (higher risk of **429** / rate limits). The scheduler uses a **sliding window** (`Parallel.ForEachAsync`): when one chunk finishes, the next index in the queue can start without waiting for the rest of a former “batch”. Manifest/cache writes stay serialized per job so the cache file is not corrupted under concurrency.
- **Chunk failures:** a failure on one chunk does **not** abort other chunks (unless the error is classified as global: **401/403**, **insufficient_quota**, etc. — then the whole job stops). Markdown/JSON merge order is preserved by index.
- **Disk state:** `transcription_work_state.json` in the job artifact root is updated as chunks progress; if missing, a legacy bootstrap may create it from `chunks/` + `chunks_json/` (see `docs/LEGACY_WORK_STATE_RECOVERY.md`). **`pending_chunks.json`** can be written by **`EnqueueTranscriptionWork`** gRPC and is consumed on the next pipeline run (intersected with any explicit chunk filter).
- **Chunk timeline:** `timeline/chunks.json` in the job artifact root is written right after chunking: for every main chunk its file name, `offset_sec` (where the chunk starts in the audio that was split) and `emit_guard_sec` (leading overlap with the previous chunk). `chunks_json/` keeps chunk-local times, so **rebuild_combined** and **retranscribe** read this file to put a regenerated `chunks_md/*.md` back on the recording's timeline. Jobs transcribed before the file existed get the same numbers derived from the first chunk's duration and `chunk_overlap_sec`.
- **Job settings:** `job_settings.json` in the job folder (written by the orchestrator at upload) holds the settings of that one job; they go on top of the shared config, which stays as it is for every other job. Shape: `{ "schema_version": 1, "transcription": { "silence_keep_sec": 1.0 } }`. Accepted keys: `silence_compress_enabled` (true/false), `silence_keep_sec` (0.05–5), `model` (a model id). **SubmitJob** and the chunk commands that read the config (**TranscribeSub**, **retranscribe**, **rebuild_combined**, **rebuild_split_merged**) apply the file; a file that cannot be read as settings (not JSON, an unknown key, a value out of range) rejects the request — a job never runs silently with other settings than asked. A job that names another `model` than the shared config also gets that model's answer format: the diarize model `diarized_json` + `chunking_strategy: auto`, `whisper-1` `verbose_json` (lines with times, no speakers), other models plain `json` (text without times). The model of a call now comes from the job config (`model`); before, the client always used the model of the service.
- **Run settings record:** when a job is accepted (`SubmitJob`), `run_settings.json` is written into the job folder — the shared config with the job's own settings on top, i.e. what the pipeline is started with: `{ "schema_version": 1, "written_at": "2026-11-14T00:10:00Z", "model": "gpt-4o-transcribe-diarize", "silence_compress_enabled": true, "silence_keep_sec": 1.0, "from_job_settings": ["silence_keep_sec"] }`. `from_job_settings` lists the keys that came from `job_settings.json`; `silence_keep_sec` matters only when compression is on. It is a record for readers (the orchestrator shows it in the job view): nothing reads it back, a write failure is only logged, chunk commands do not rewrite it.
- **Kept pause:** the silence left in place of a pause is `silence_keep_sec`, but never more than the pause itself — with `silence_keep_sec: 1.0` a pause of 0.5 s stays 0.5 s instead of growing to 1 s. The shared config keeps **1.0 s** (`silence_keep_sec` in `config/default.json`; it was 0.2 s until 2026-11-13): on a one-voice track the model merged phrases separated by long pauses when it heard only 0.2 s between them and put the line 6–20 s early; with 1.0 s the checked phrases stand within 0.5 s of an uncompressed run, at about 20 % more tokens. A job can still take another value through its job settings.
- **Chunk the server refuses:** when the request with time marks gets HTTP 5xx three times, the chunk is cut into `server_error_split_parts` parts (shared config: **4**; 0 or 1 switches it off; a part is not made shorter than 10 s, so a chunk under 20 s is not cut), each part is transcribed on its own, and the parts are put back into one answer of the chunk with times counted from its start. The cache, `chunks_json` and the md writers read that answer as any other; its block `auto_split` says how it was assembled (the parts, their places, the usage of each, how many came back without time marks), and `usage` is the sum of the parts. Before, the client's next request had no `response_format` and the diarize model answered plain text: the whole chunk became one line without time marks and was repaired by an operator split. That old way still applies when the split is off, the chunk is too short, or the parts cannot be cut or transcribed. A part the server refuses three times as well comes back as text and stands at the start of that part. The part files are temporary and are removed after the merge; speaker letters of different parts are independent, as they are between chunks. Checked by tests against a scripted server (`ServerErrorChunkSplitTests`); not yet seen on a live HTTP 500.
- **Sub-chunk timeline:** an operator **Split** cuts a chunk into parts that are transcribed on their own, so the segments of a part count seconds from the start of that part. `split_chunks/chunk_N/sub_chunks_timeline.json` (written by **Split**) keeps where every part starts inside the chunk (`offset_sec`) and how much of it repeats the previous part (`emit_guard_sec`); the merge of the parts (after the last **TranscribeSub** and on **rebuild_split_merged**) reads it. A split made before this file existed is measured from its audio — duration of the chunk and of the first part — and the file is written then with `derived: true`. When the places cannot be established the merge answers `split_merge_sub_offsets_unknown` and writes nothing, instead of stacking all parts at the start of the chunk.
- **Speech map:** with `silence_compress_enabled` the model hears a shorter file — every silence region is replaced by a `silence_keep_sec` gap — and answers in seconds of that file. `timeline/speech_map.json` in the job artifact root lists the kept pieces of the source (`source_start_sec`, `source_end_sec`) and where each stands in the compressed file (`output_start_sec`); the file is assembled from the same plan, so the table is exact. Every writer of transcript lines (first run, **rebuild_combined**, split merge) translates the time of a line back through this table, so `chunks_md/*.md` and the combined transcript carry times of the **recording**; chunk JSON keeps the raw times of the model. A segment that starts in the last 0.35 s of a piece and runs into the next one is placed at the start of the next piece (the model tends to mark a phrase slightly early, and without this the line would land before the whole pause that was cut). A run without compression removes the file. Jobs transcribed before the map existed keep times of the compressed file.
- **`StreamJobStatus` polling:** default **2000 ms** between polls (`Agent04:StreamJobStatusPollMs` in `appsettings.json`) to reduce load; VM updates in Agent04 are immediate; UI sees changes within ~poll interval + network.
- **Chunk cancel (gRPC / UI ×):** while a chunk’s OpenAI request is in flight, the pipeline polls cancel flags and cancels the HTTP call via `CancellationToken` (cooperative abort). Cached chunks skip HTTP and are not affected.
- **HttpClient / «зависшие» запросы:** не пересоздавайте `HttpClient` ради «убить» один зависший вызов — используйте **отмену по токену** и **таймауты** (`HttpClient.Timeout`, дедлайн на токене). Отмена кооперативная: сокет на стороне провайдера может закрыться не мгновенно. Экземпляр клиента из DI держите долгоживущим; `Dispose` — при остановке приложения или смене скоупа, не после каждой ошибки.
- **Logs:** `OpenAITranscriptionClient` emits structured lines for each HTTP call: `AgentJobId`, `ChunkIndex`, `HttpAttemptId`, `ParallelWorkersConfigured`, process-wide **`InFlight`** count, duration, and on failure **HTTP status** with **Category** `auth` (401/403), `rate_limit` (429), `client_error`, `server_error`, `timeout` (**Reason=http_client_timeout** where applicable), plus a truncated response body (no API key / `Authorization`). With **`parallel_transcription_workers` > 1**, log order across chunks is not start order — correlate by **`ChunkIndex`** / **`HttpAttemptId`**.

## Monitoring

- **Polling:** gRPC `GetJobStatus` — state, progress, phase, `md_output_path` / `json_output_path` when completed.
- **List / query:** gRPC `QueryJobs`.
- **Real-time:** gRPC `StreamJobStatus(job_id)`.

## RENTGEN fact (Knowledge Store)

To register the virtual-model implementation document in Knowledge Store (http://localhost:5173), run (with Knowledge Store up):

```powershell
cd agent04/scripts
./register-rentgen-fact.ps1
```

Optional: `$env:KNOWLEDGE_STORE_URL = "http://localhost:5173"`.

## Project layout

- `Agent04/` — main project (ASP.NET Core + gRPC)
- `Agent04/Composition/` — DI registration (`Agent04ServiceRegistration`)
- `Agent04/Features/Transcription/` — vertical slice: Domain, Application, Infrastructure
- `Agent04/Proto/` — gRPC `.proto`
- `Agent04/Services/` — gRPC service implementation
- `Agent04/config/default.json` — default config
- `docs/RENTGEN_IMPLEMENTATION.md` — virtual model requirements (for Knowledge Store fact)
- `docs/CHUNKS_AND_RENTGEN.md` — чанки, pre_split vs операторский split, RENTGEN, связка с agent05

## Differences from agent01

- **Web service only:** no CLI; start with `dotnet run`; transcription via **gRPC** only.
- **Workspace root:** one required root directory per instance (appsettings/environment); all request paths are relative to it.
- Job status store and progress updates; optional tags; **QueryJobs** on gRPC for filtered lists.
- Output format (Markdown and JSON) is compatible so consumers can use results from either agent.
