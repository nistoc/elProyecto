# Agent04 — Transcription service (.NET)

.NET reimplementation of the agent01 transcription pipeline: OpenAI transcription (diarization), ffmpeg chunking, cache by fingerprint, merge, Markdown output. Clean Architecture, vertical slice `Features/Transcription`. **Web service only** (no CLI); **public API — gRPC only** (HTTP REST/OpenAPI removed; orchestrators such as agent05 use gRPC).

## Requirements

- .NET 9
- ffmpeg / ffprobe on PATH (or set in config)
- OpenAI API key (e.g. `env:OPENAI_API_KEY` in config)

## Configuration

- **Workspace root (required):** set `WorkspaceRoot` or `workspace_root` in `appsettings.json` or environment to an **absolute path** of the workspace directory. The app checks that this directory exists at startup and exits if it does not. All paths in gRPC requests are relative to this root.
- **Job config:** same contract as agent01 (minus job-level combined JSON). Example: `Agent04/config/default.json`. Keys include `file`/`files`, `openai_api_key` (e.g. `env:OPENAI_API_KEY`), `model`, `md_output_path`, `cache_dir`, `split_workdir`, etc. Paths in job config are relative to the workspace root.

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
