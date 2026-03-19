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

- **Порт (профили http/https):** **http://localhost:5032** — только **HTTP/2 (h2c)** для gRPC. На Windows без TLS для gRPC нужен отдельный endpoint с h2c (см. `Program.cs` → `ConfigureKestrel`).

## gRPC API

Paths in requests are relative to the instance `workspace_root` (from config):

- **TranscriptionService.SubmitJob** — submit job (`config_path`, `input_file_path`, optional `tags`, optional `callback_url`).
- **TranscriptionService.GetJobStatus** — status by `job_id`.
- **TranscriptionService.StreamJobStatus** — server stream of status updates until terminal state.
- **TranscriptionService.QueryJobs** — list/filter jobs (semantic key, status, time range, limit/offset).

Proto: `Agent04/Proto/transcription.proto`.

> **RENTGEN / узлы:** ранее дерево узлов отдавалось по HTTP (`GET .../nodes`). Внешнего HTTP больше нет; для списка заданий используйте **QueryJobs**. При необходимости дерева узлов — расширение `.proto` или отдельный read-сервис (см. `docs/RENTGEN_IMPLEMENTATION.md`).

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

## Differences from agent01

- **Web service only:** no CLI; start with `dotnet run`; transcription via **gRPC** only.
- **Workspace root:** one required root directory per instance (appsettings/environment); all request paths are relative to it.
- Job status store and progress updates; optional tags; **QueryJobs** on gRPC for filtered lists.
- Output format (Markdown and JSON) is compatible so consumers can use results from either agent.
