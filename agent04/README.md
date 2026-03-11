# Agent04 — Transcription service (.NET)

.NET reimplementation of the agent01 transcription pipeline: OpenAI transcription (diarization), ffmpeg chunking, cache by fingerprint, merge, Markdown/JSON output. Clean Architecture, vertical slice `Features/Transcription`, REST + gRPC.

## Requirements

- .NET 9
- ffmpeg / ffprobe on PATH (or set in config)
- OpenAI API key (e.g. `env:OPENAI_API_KEY` in config)

## Configuration

Same contract as agent01. Default config: `Agent04/config/default.json`. Keys include:

- `file` / `files` — input audio path(s)
- `openai_api_key` — use `env:OPENAI_API_KEY` to read from environment
- `model`, `fallback_models`, `language`, `md_output_path`, `raw_json_output_path`
- `pre_split`, `chunk_overlap_sec`, `target_chunk_mb`, `cache_dir`, `split_workdir`, etc.

## Running

### CLI

Process files from a config file (same semantics as agent01):

```bash
cd Agent04
dotnet run -- --config=config/default.json
```

Or with explicit path:

```bash
dotnet run -- --config=path/to/my.json
```

Output paths are printed (Markdown and JSON).

### REST API

Run the web host (no `--config`):

```bash
dotnet run
```

- **Base URL:** http://localhost:5000 (or https, see launchSettings).
- **POST /api/transcription/jobs** — submit job. Body: `{ "configPath": "config/default.json", "inputFilePath": "optional override", "tags": ["optional","tags"] }`. Returns `202 Accepted` and `jobId`; `Location` header points to the job.
- **GET /api/transcription/jobs/{id}** — job status (state, progress, phase, paths when completed).
- **GET /api/transcription/jobs** — list jobs (query: `status`, `limit`, `offset`).
- **GET /api/transcription/jobs/query** — query by `tag`, `status`, `from`, `to`, `limit`, `offset`.
- **GET /api/transcription/jobs/{id}/nodes** — virtual model nodes for the job (flat list; `?tree=true` for hierarchy).

Error responses use RFC 7807 ProblemDetails. OpenAPI spec: `/openapi/v1.json` (when development).

### gRPC

Same host exposes gRPC:

- **TranscriptionService.SubmitJob** — submit job (config_path, input_file_path, optional tags).
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

- Single process: CLI and API in one binary; mode chosen by presence of `--config`.
- Job status store and progress updates when running via REST/gRPC; optional tags and query by tag.
- Output format (Markdown and JSON) is compatible so consumers can use results from either agent.
