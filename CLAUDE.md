# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Pipeline for transcribing Spanish lesson audio recordings: audio → diarized transcript (OpenAI `gpt-4o-transcribe-diarize`) → "refining" (fixing Spanish words the ASR wrote in Cyrillic, e.g. «пор фавор» → «por favor») → web UI to monitor jobs and edit transcripts. Three services, one monorepo:

| Directory | Service | Stack | Port |
|---|---|---|---|
| `agent04/` | Transcriber | .NET 9, **gRPC only** (no REST) | 5032 (h2c) |
| `agent06-improver-dot-net/` | Refiner (TranslationImprover) | .NET 9, gRPC (h2c) + REST | 5033 gRPC / 5034 REST |
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

**Job artifact layout** (inside `{workspace}/{jobId}/`): source audio, `transcript.md`, `transcript_fixed*.md` (refiner output), `response.json`, `chunks/`, `chunks_json/`, `converted_wav/`, `intermediate_results/`, `split_chunks/chunk_N/` (operator splits; `sub_chunks_timeline.json` there keeps where each part starts inside the chunk, the merge of the parts reads it), `transcription_work_state.json` (chunk progress on disk), `timeline/chunks.json` (each chunk's offset and overlap guard in the audio that was split; rebuild and retranscribe read it), `timeline/speech_map.json` (only when silence was cut out: kept pieces of the source and their place in the compressed file; md writers use it to print times of the recording), `track_source.json` (only when the job was started from the lessons list: which file of the recordings folder it transcribes), `job_settings.json` (only when the operator chose settings for this job at upload: pause kept, compression, model; agent04 puts them on top of the shared config), `run_settings.json` (what the job started with: model, compression, pause kept, which keys came from `job_settings.json`; agent04 writes it in `SubmitJob`, agent05 shows it as `runSettings` of the job and reads the run's artifacts instead for jobs older than the record), `pending_chunks.json` (indices for next run, written by `EnqueueTranscriptionWork` gRPC).

**Dialogue jobs.** agent05 `POST /api/jobs/dialogue` merges the combined transcripts of participant-track jobs (one job per Zoom participant file) into a job folder of its own: `transcript_dialogue_<stamp>.md` (speaker labels replaced by participant names, lines in time order) plus `dialogue.json` (tracks and merge log). No audio, no agent04 call; the source jobs are only read, and the job waits in `awaiting_refiner` like any other. `POST /api/jobs/{id}/dialogue/rebuild` writes a new merge next to the old ones.

**Lessons from the recordings folder.** agent05 `Recordings:RootPath` (currently `C:/trash/spanish`) is **read only**: `GET /api/sessions` scans it into lesson (day folder `YYYY.MM.DD[ title]`, or Zoom period folders left in the root) → period (`YYYY-MM-DD HH.mm.ss <meeting>` folder, or a flat audio file of an old day) → track (`mixed` = `audio<id>.m4a`, `participant` = `Audio Record/audio<Name><N><id>.m4a`) and ties jobs of the workspace to tracks (`TrackRunsIndex`: by `track_source.json`, by name + size of the audio in the job folder, by size + first 64 KB). `POST /api/sessions/runs` copies a track into a new job folder, writes `track_source.json` and queues the usual (paid) pipeline. UI: the "Lessons" panel (`LessonsPanel.tsx`).

**Job config contract.** agent04 job configs (`agent04/Agent04/config/default.json`) use the legacy agent01 JSON schema: `file`, `model`, `openai_api_key: "env:OPENAI_API_KEY"`, `pre_split`, `target_chunk_mb`, `parallel_transcription_workers`, `md_output_path`, etc. One job can override `silence_compress_enabled`, `silence_keep_sec` and `model` without touching that file: UI upload form → agent05 `POST /api/jobs` form field `settings` → `{jobDir}/job_settings.json` → agent04 `JobSettingsFile.Apply` in `SubmitJob` and the chunk commands.

**gRPC over plain HTTP (h2c).** No TLS anywhere locally. agent05's client sets `Http2UnencryptedSupport`; agent04's Kestrel listens on 5032 as HTTP/2-only (`ConfigureKestrel` in `Program.cs`). agent06 does the same on 5033 (gRPC) and serves REST/health on 5034. Breaking this yields `HTTP_1_1_REQUIRED` errors.

## agent04 internals (where most work happens)

Vertical slice in `agent04/Agent04/Features/Transcription/` split Domain / Application (interfaces + models) / Infrastructure (implementations); DI wiring in `Composition/Agent04ServiceRegistration.cs`; gRPC surface in `Services/TranscriptionGrpcService.cs` + `Proto/transcription.proto`. Key concepts:

- **Chunking**: audio is pre-split by ffmpeg into `chunks/`, transcribed with a sliding-window scheduler (`parallel_transcription_workers`, clamp 1–64), cached by SHA256 fingerprint, merged in index order into Markdown/JSON. A chunk the server refuses three times (HTTP 5xx) is cut into `server_error_split_parts` parts (config, default 4) and put back as one timed answer of the chunk (`ServerErrorChunkSplit`; block `auto_split` in the chunk's JSON) instead of becoming one line without time marks.
- **Plain-text model (`gpt-transcribe`)**: a job whose `model` starts with `gpt-transcribe` (job settings; the shared default stays the diarize model) goes through `SpeechPiecesTranscription` — the chunk is asked whole and in pieces cut at its pauses (`SpeechPiecePlanner`, silence found inside the chunk file), the whole text is laid onto the pieces by word alignment (`SpeechPieceWordAlignment`), and the answer is assembled in the shape of a timed one (block `speech_pieces` in the chunk's JSON), so cache, md writers, retranscribe and sub-chunks need nothing special. It writes Spanish in Latin and Russian in Cyrillic (`speech_pieces_languages`, `speech_pieces_prompt` in the shared config) and has no speakers — meant for participant tracks. A runaway answer (one word repeated thousands of times) is dropped and asked again.
- **Operator chunk commands** (`ChunkCommand` gRPC, driven from the UI's chunk panel via agent05 `POST /api/jobs/{id}/chunk-actions`): cancel (cooperative, via per-job cancel-flag dir `.agent04_chunk_cancel/` + HTTP CancellationToken), split into sub-chunks (`split_chunks/chunk_N/sub_chunks/`), transcribe_sub, retranscribe, rebuild_combined/rebuild_split_merged, DeleteSubChunk.
- **"RENTGEN" / virtual model**: a node tree (`INodeModel`, `ChunkVirtualModelFromWorkState`, VM merge) describing per-chunk state for the UI; docs in `agent04/docs/RENTGEN_IMPLEMENTATION.md` and `CHUNKS_AND_RENTGEN.md`.
- `Agent04.Tests/ChunkArtifactGroupingTests` optionally scans a real sample job under the repo-root `runtime/` — it no-ops if the folder is absent.

## Conventions

- Conventional commits with agent scope: `feat(agent04): …`, `chore: …`.
- Console log timestamps are **UTC** in all services (configured in each `Program.cs`) — keep it that way.
- API/frontend standards and parity docs live in `agent05-ui-control/docs/` (`API_STANDARDS.md`, `FRONTEND_STANDARDS.md`, `JOB_FILES_SCANNER_AND_AGENTS.md`); agent04 behavior docs in `agent04/docs/`.
- READMEs are the source of truth for endpoint tables: root `README.md` (topology), plus each agent's `README.md`.
