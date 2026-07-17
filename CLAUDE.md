# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Pipeline for transcribing Spanish lesson audio recordings: audio → diarized transcript (OpenAI `gpt-4o-transcribe-diarize`) → "refining" (fixing Spanish words the ASR wrote in Cyrillic, e.g. «пор фавор» → «por favor») → web UI to monitor jobs and edit transcripts. Three services, one monorepo:

| Directory | Service | Stack | Port |
|---|---|---|---|
| `agent04/` | Transcriber | .NET 9, **gRPC only** (no REST) | 5032 (h2c) |
| `agent06-improver-dot-net/` | Refiner (TranslationImprover) | .NET 9, REST + gRPC | 5033 |
| `agent05-ui-control/` | Orchestrator API (XtractManager) + React UI | .NET 9 + React 18/Vite/TS | 5010 / 5173 |

A first generation in Python (`agent01`, `agent03-trans-improver`, Node `agent-browser`) was removed in commit `88b7477`; retrieve from git history if ever needed. agent04/agent06 keep artifact and config formats compatible with it, which is why code comments still reference agent01/agent-browser.

## Commands

Each .NET service builds from its project dir (solutions are `.slnx` files at each agent root):

```bash
# agent04 (run from agent04/Agent04), agent05 API (from agent05-ui-control/API),
# agent06 (from agent06-improver-dot-net/API):
dotnet build
dotnet run

# tests — run from the agent root or the *.Tests project dir:
dotnet test
dotnet test --filter "FullyQualifiedName~OperatorChunkSplitPlannerTests"   # single class/test

# UI (from agent05-ui-control/UI):
npm install
npm run dev            # Vite on 5173, proxies /api and /health to :5010
npm run build          # tsc -b && vite build
npm run test           # vitest run
npm run test -- chunkArtifactGroups   # single spec
npm run lint           # eslint
npm run format         # prettier --write
```

Startup order for the full pipeline: agent04 and agent06 first, then agent05 API, then UI. Requirements: .NET 9, Node 18+, `ffmpeg`/`ffprobe` on PATH, `OPENAI_API_KEY` (agent06 reads it from a gitignored `.env` in `agent06-improver-dot-net/API/`).

## Cross-service contracts (the important invariants)

**Workspace root.** All three services operate on one shared jobs directory, currently `C:/trash/spanish/elProyecto/runtime` (gitignored). Layout: `{workspace}/{jobId}/` holds the uploaded audio and every artifact. Config keys that must stay in sync: `WorkspaceRoot` in `agent04/Agent04/appsettings.json`, `Jobs:WorkspacePath` + `Agent04:WorkspacePath` in `agent05-ui-control/API/appsettings*.json` (`Agent06:WorkspaceRoot` empty = falls back to `Jobs:WorkspacePath`). agent04 **exits at startup** if its `WorkspaceRoot` doesn't exist. All paths in gRPC requests are relative to the receiving service's workspace root; agent05 passes `job_directory_relative` (= jobId) so agent04/agent06 write into the job folder.

**Job flow.** UI `POST /api/jobs` (multipart) → agent05 stores file, runs pipeline in background (`TranscriptionRefinerPipeline`): gRPC `SubmitJob` to agent04 → consumes `StreamJobStatus` server-stream → on Completed reads `transcript.md`, submits to agent06 `SubmitRefineJob` → streams refine status → done. UI receives updates via SSE `GET /api/jobs/{id}/stream` (events: `snapshot` | `status` | `done`; first event is always a full snapshot). Job phases: `idle → transcriber → awaiting_refiner → refiner → completed`.

**Job artifact layout** (inside `{workspace}/{jobId}/`): source audio, `transcript.md`, `transcript_fixed*.md` (refiner output), `response.json`, `chunks/`, `chunks_json/`, `converted_wav/`, `intermediate_results/`, `split_chunks/chunk_N/` (operator splits), `transcription_work_state.json` (chunk progress on disk), `pending_chunks.json` (indices for next run, written by `EnqueueTranscriptionWork` gRPC).

**Job config contract.** agent04 job configs (`agent04/Agent04/config/default.json`) use the legacy agent01 JSON schema: `file`, `model`, `openai_api_key: "env:OPENAI_API_KEY"`, `pre_split`, `target_chunk_mb`, `parallel_transcription_workers`, `md_output_path`, etc.

**gRPC over plain HTTP (h2c).** No TLS anywhere locally. agent05's client sets `Http2UnencryptedSupport`; agent04's Kestrel listens on 5032 as HTTP/2-only (`ConfigureKestrel` in `Program.cs`). Breaking this yields `HTTP_1_1_REQUIRED` errors.

## agent04 internals (where most work happens)

Vertical slice in `agent04/Agent04/Features/Transcription/` split Domain / Application (interfaces + models) / Infrastructure (implementations); DI wiring in `Composition/Agent04ServiceRegistration.cs`; gRPC surface in `Services/TranscriptionGrpcService.cs` + `Proto/transcription.proto`. Key concepts:

- **Chunking**: audio is pre-split by ffmpeg into `chunks/`, transcribed with a sliding-window scheduler (`parallel_transcription_workers`, clamp 1–64), cached by SHA256 fingerprint, merged in index order into Markdown/JSON.
- **Operator chunk commands** (`ChunkCommand` gRPC, driven from the UI's chunk panel via agent05 `POST /api/jobs/{id}/chunk-actions`): cancel (cooperative, via per-job cancel-flag dir `.agent04_chunk_cancel/` + HTTP CancellationToken), split into sub-chunks (`split_chunks/chunk_N/sub_chunks/`), transcribe_sub, retranscribe, rebuild_combined/rebuild_split_merged, DeleteSubChunk.
- **"RENTGEN" / virtual model**: a node tree (`INodeModel`, `ChunkVirtualModelFromWorkState`, VM merge) describing per-chunk state for the UI; docs in `agent04/docs/RENTGEN_IMPLEMENTATION.md` and `CHUNKS_AND_RENTGEN.md`.
- `Agent04.Tests/ChunkArtifactGroupingTests` optionally scans a real sample job under the repo-root `runtime/` — it no-ops if the folder is absent.

## Conventions

- Conventional commits with agent scope: `feat(agent04): …`, `chore: …`.
- Console log timestamps are **UTC** in all services (configured in each `Program.cs`) — keep it that way.
- API/frontend standards and parity docs live in `agent05-ui-control/docs/` (`API_STANDARDS.md`, `FRONTEND_STANDARDS.md`, `JOB_FILES_SCANNER_AND_AGENTS.md`); agent04 behavior docs in `agent04/docs/`.
- READMEs are the source of truth for endpoint tables: root `README.md` (topology), plus each agent's `README.md`.
