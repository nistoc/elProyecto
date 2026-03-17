# Agent04 — Transcription service (.NET)

.NET reimplementation of the agent01 transcription pipeline: OpenAI transcription (diarization), ffmpeg chunking, cache by fingerprint, merge, Markdown/JSON output. Clean Architecture, vertical slice `Features/Transcription`, REST + gRPC. **Web service only** (no CLI); all calls go through HTTP or gRPC.

## Requirements

- .NET 9
- ffmpeg / ffprobe on PATH (or set in config)
- OpenAI API key (e.g. `env:OPENAI_API_KEY` in config)

## Configuration

- **Workspace root (required):** set `WorkspaceRoot` or `workspace_root` in `appsettings.json` or environment to an **absolute path** of the workspace directory. The app checks that this directory exists at startup and exits if it does not. All paths in API requests are relative to this root.
- **Job config:** same contract as agent01. Example: `Agent04/config/default.json`. Keys include `file`/`files`, `openai_api_key` (e.g. `env:OPENAI_API_KEY`), `model`, `md_output_path`, `raw_json_output_path`, `cache_dir`, `split_workdir`, etc. Paths in job config are relative to the workspace root.

## Running

Start the web host:

```bash
cd Agent04
dotnet run
```

Ensure `appsettings.json` (or environment) contains a valid `WorkspaceRoot` path before starting.

- **Порты (профиль http):** gRPC — **http://localhost:5032** (только HTTP/2, h2c); REST и OpenAPI — **http://localhost:5034**. На Windows без TLS для gRPC нужен отдельный endpoint с h2c (см. `Program.cs` → `ConfigureKestrel`).
- **POST /api/transcription/jobs** — submit job. Body: `{ "configPath": "config/default.json", "inputFilePath": "project1/audio.m4a", "tags": ["optional"] }`. `configPath` and `inputFilePath` are **relative to workspace_root**; absolute paths in `inputFilePath` are rejected with 400. Returns `202 Accepted` and `jobId`; `Location` header points to the job.
- **GET /api/transcription/jobs/{id}** — job status (state, progress, phase, paths when completed).
- **GET /api/transcription/jobs** — list jobs (query: `status`, `limit`, `offset`).
- **GET /api/transcription/jobs/query** — query by `tag`, `status`, `from`, `to`, `limit`, `offset`.
- **GET /api/transcription/jobs/{id}/nodes** — virtual model nodes for the job (flat list; `?tree=true` for hierarchy).

Error responses use RFC 7807 ProblemDetails. OpenAPI spec: `/openapi/v1.json` (when development).

### gRPC

Same host exposes gRPC. Paths in the request are interpreted relative to the instance's workspace_root (from config):

- **TranscriptionService.SubmitJob** — submit job (config_path, input_file_path, optional tags). Paths relative to workspace_root.
- **TranscriptionService.GetJobStatus** — get status by job_id.
- **TranscriptionService.StreamJobStatus** — server stream of status updates for a job.

Proto: `Agent04/Proto/transcription.proto`.

## Monitoring

- **Polling:** `GET /api/transcription/jobs/{id}` or gRPC `GetJobStatus` to read state, progress, phase, and (when completed) `md_output_path`, `json_output_path`.
- **List:** `GET /api/transcription/jobs` for all jobs with optional `status` filter.
- **Query by tag:** `GET /api/transcription/jobs/query?tag=...` for jobs with a given tag (if submitted with `tags`).
- **Real-time:** gRPC `StreamJobStatus(job_id)` for a stream of status updates until the job completes or fails.
- **Virtual node tree:** `GET /api/transcription/jobs/{id}/nodes?tree=true` for hierarchical step/chunk nodes (job → chunking → transcribe → chunk-0..N → merge).

## RENTGEN fact (Knowledge Store)

To register the virtual-model implementation document in Knowledge Store (http://localhost:5173), run (with Knowledge Store up):

```powershell
cd agent04/scripts
./register-rentgen-fact.ps1
```

Optional: `$env:KNOWLEDGE_STORE_URL = "http://localhost:5173"`.

## Project layout

- `Agent04/` — main project (ASP.NET Core + gRPC)
- `Agent04/Composition/` — Ninject module (Agent04Module) — composition root
- `Agent04/Features/Transcription/` — vertical slice: Domain, Application, Infrastructure
- `Agent04/Proto/` — gRPC `.proto`
- `Agent04/Controllers/` — REST transcription controller
- `Agent04/Services/` — gRPC service implementation
- `Agent04/config/default.json` — default config
- `docs/RENTGEN_IMPLEMENTATION.md` — virtual model requirements (for Knowledge Store fact)
- `scripts/register-rentgen-fact.ps1` — script to POST the fact to Knowledge Store

## Differences from agent01

- **Web service only:** no CLI; start with `dotnet run`, all transcription via REST or gRPC.
- **Workspace root:** one required root directory per instance (appsettings/environment); all request paths are relative to it.
- Job status store and progress updates; optional tags and query by tag.
- Output format (Markdown and JSON) is compatible so consumers can use results from either agent.
