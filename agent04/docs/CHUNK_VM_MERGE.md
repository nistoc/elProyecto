# Chunk virtual model merge (phase 7)

## Where merge runs

**Agent04** owns `ChunkVirtualModelMerge` (`Features/Transcription/Infrastructure/ChunkVirtualModelMerge.cs`). It merges:

1. **Client snapshot** — repeated `ChunkVirtualModelEntry` in `GetJobStatusRequest.client_chunk_virtual_model` and `StreamJobStatusRequest.client_chunk_virtual_model`.
2. **Live Rentgen VM** — built in `TranscriptionGrpcService.BuildChunkVirtualModel`.

Rules match the former Agent05 merge: weak `Pending` placeholders without timestamps do not replace terminal / running / timed / logged rows; orphan snapshot rows stay; `transcript_activity_log` is combined when neither string contains the other.

## gRPC behaviour

- **GetJobStatus** — response `chunk_virtual_model` is `Merge(client_chunk_virtual_model, live)` (empty client ⇒ live only).
- **StreamJobStatus** — server keeps accumulated merged VM per connection; each write is `Merge(accumulated, live)`. The client should pass the current job snapshot VM when opening the stream (reconnect with latest snapshot if the stream drops).
- **QueryJobs** — no client VM; response VM is live-only (same as empty client).

## What Agent05 no longer does

- Removed `ChunkVirtualModelMerge` from agent05-ui-control. Orchestrator passes `Chunks.ChunkVirtualModel` into `GetJobStatus` / `StreamJobStatus` and stores the returned list as-is.
- **JobSnapshotDiskEnricher** still augments VM from disk (e.g. sub-chunk rows) where needed — that is separate from Rentgen placeholder merge.

## UI (agent05-ui-control)

`GetChunkArtifactGroups` returns file metadata plus optional **`main_virtual_model` / `sub_virtual_model`** (JSON: `vmRow` on each group / sub-chunk). The UI uses **`overlayVmFromJobWhenMissing`**: Rentgen rows from the API win; the job snapshot fills gaps when nodes are missing. `GET .../files` + `JobProjectFilesScanner` remain only for the project file tree, not Stats grouping (phase 8).

## Tests

Merge unit tests: `Agent04.Tests/ChunkVirtualModelMergeTests.cs`. Binder tests: `Agent04.Tests/ChunkArtifactGroupVirtualModelBinderTests.cs`.
