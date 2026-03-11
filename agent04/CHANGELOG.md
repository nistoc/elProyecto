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

### Added

- **XRay attributes:** `[XRayNode(Ensure|Start|Complete)]` mark pipeline methods that update the virtual model (RENTGEN). Methods `EnterStep`, `CompleteStep`, `EnsureAndStartJobRoot`, etc. are decorated; explicit `INodeModel` calls remain, attributes document and reserve for future interceptor.
- **Tag = node id in virtual model:** GET /jobs/{id}/nodes accepts optional query `tag` (node id/name); returns that single node or 404. `INodeQuery.GetNodeByScopeAndId(scopeId, nodeId)` added.

### Changed

- **Semantic key vs tag:** Job list filter parameter renamed from `tag` to `semanticKey` in REST (GET /jobs/query?semanticKey=...) and in gRPC (`QueryJobsRequest.semantic_key`). `JobListFilter.Tag` renamed to `SemanticKey`. The word "tag" in the API now means only the node identifier when querying node status (GET /jobs/{id}/nodes?tag=...).
- **XRay clarification:** Removed `Activity.Current?.SetTag(...)` from pipeline and controller (those were tracing tags, not XRay). XRay in Agent04 = attributes on business methods for the virtual model; see docs/RENTGEN_IMPLEMENTATION.md.
- **Documentation:** RENTGEN_IMPLEMENTATION.md and PLAN_COMPLIANCE_AND_TODO.md updated for XRay attributes and tag/semanticKey semantics.
