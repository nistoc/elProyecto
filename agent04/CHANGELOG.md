# Changelog

All notable changes to the Agent04 project are documented here.

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

## [Unreleased]

### Added

- **Workspace root:** Required `WorkspaceRoot` (or `workspace_root`) in appsettings or environment; validated at startup (app exits if directory does not exist). All request paths are relative to this root.
- **Relative paths only:** `configPath` and `inputFilePath` in REST and gRPC are resolved relative to workspace root. Absolute `inputFilePath` is rejected with 400.
- **Unique transcript names:** Output transcript filenames include job id (e.g. `{base}_{jobId}_transcript.md`) to preserve history; pattern supports `{jobId}`.
- **Proto comment:** `SubmitJobRequest` documented: paths are relative to instance workspace_root from config.

### Removed

- **CLI mode:** Support for `--config` and one-shot console run removed. Agent04 runs only as a web service (HTTP + gRPC). Use `dotnet run` to start the host.
