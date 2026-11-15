# Changelog

All notable changes to the agent06-improver-dot-net (TranslationImprover) project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Changed

- **Built-in refine prompt reworded as copying with one exception.** The built-in user template (`FilePromptLoader.DefaultPrompt`) and the system prompt (`RefinePipeline.SystemPrompt`) no longer say that the transcript "contains Russian speech where Spanish was transcribed in Cyrillic" and no longer give a numbered task to find and replace. They say: copy the lines of the batch; the transcript is bilingual on purpose, keep every word in the language and alphabet it has, the speaker's mistakes included; one kind of word is respelled — a Spanish word written in Cyrillic by ear — and such words are rare. Reason: transcripts made by `gpt-transcribe` are already right in most lines, and the old wording made `gpt-4o-mini` look for things to fix. Measured on one lesson dialogue of 593 lines (batch 5, context 2, temperature 0): the old wording changed 29 lines — about 3 for the better, about 13 Russian words translated or broken into mixed script, about 9 "corrections" of the speaker's own Spanish; a wording with a list of "never" rules and an example of each changed 47 lines (the model did the very things the examples named: Russian words written in Latin letters, whole Russian lines translated); the wording kept here changed 7 lines — 1 for the better, 6 small ones for the worse. So the refiner is now close to doing nothing on such a transcript, and its output still has to be read. A prompt saved by the operator in agent05 (Refiner tab) is used instead of the built-in one, as before. Tests: `BuiltInPromptTests`.
- **Built-in refine prompt:** the line-format instruction no longer names `speaker_N` only — it reads `- TIME SPEAKER: "text"` and says the label may be `speaker_0` or a participant name and must not be changed. Needed for dialogue transcripts merged from participant tracks (agent05 `POST /api/jobs/dialogue`), whose lines carry names. Not yet checked against the live model.

### Added

- **Per-job artifact root (refine):** **`SubmitRefineJobRequest.job_directory_relative`** — один сегмент под **`workspace_root`**; ввод/вывод и промежуточные файлы разрешаются от **`effectiveArtifactRoot`** = `workspace_root/job_directory_relative` при непустом поле; **`FilePromptLoader`** по-прежнему от глобального **`workspace_root`**. Тесты: **`RefineWorkspacePathsTests`**.
- Initial repository structure: solution TranslationImprover.slnx, project TranslationImprover.Instance (net9.0), API.Tests, nuget.config (nuget.org only).
- Workspace root: Application/WorkspaceRoot.cs, validation in Program.cs (WorkspaceRoot or workspace_root from config), GET / and GET /health.
- Ninject composition root (TranslationImproverModule), AddControllers, AddGrpc, AddOpenApi.
- Refine slice: Domain (RefineJobState, BatchInfo, BatchResult), Application (IRefineJobStore, RefineJobStatus, RefineJobListFilter, IRefinePipeline, RefineJobRequest, IOpenAIRefineClient, IPromptLoader, INodeModel, INodeQuery, NodeInfo).
- RefineJobQuery slice: IRefineJobQueryService, RefineJobQueryService (QueryBySemanticKey, GetById, Query).
- Proto refine.proto: RefinerService (SubmitRefineJob, GetRefineStatus, StreamRefineStatus, CancelRefineJob, QueryRefineJobs), messages with state Cancelled, semantic_key, tags.
- gRPC: RefinerGrpcService with path validation (relative to workspace_root), InMemoryRefineJobStore, StubRefinePipeline (sets job to Failed "Not implemented yet"); MapGrpcService in Program.
- Refine infrastructure: IRefineJobCancellation, InMemoryRefineJobCancellation; FilePromptLoader (prompt from file or default with {context}/{batch}); OpenAIRefineClient (Chat Completions, API key from config only); RefinePipeline (parse >>>>>>>/<<<<<, batches, context, callback, INodeModel); InMemoryNodeStore (INodeModel + INodeQuery, RefineJobState). Ninject bindings for pipeline, OpenAI, prompt loader, node store, cancellation; IConfiguration and IHttpClientFactory for Ninject.
- gRPC: job cancellation via IRefineJobCancellation (Register CTS, TryCancel); RefinerGrpcService uses RefinePipeline and cancellation.
- REST API: RefineController — POST /api/refine/jobs (202, Location), GET /api/refine/jobs/{id}, GET .../stream (SSE), GET .../result, POST .../cancel, GET /api/refine/jobs/query (semanticKey, status, from, to, limit, offset), GET .../nodes (tree, tag). Validation (relative paths, input required); ProblemDetails; X-Caller-Id optional.
- Configuration: OpenAI:ApiKey and OpenAI:BaseUrl in appsettings.json / appsettings.Development.json; README with run and config (WorkspaceRoot, OpenAI key from config/env only).
- Documentation: README.md (purpose, run, config, API overview); docs/RENTGEN_IMPLEMENTATION.md (Tags, semanticKey, tag = node id, node model, X-Ray).
- TranscriptParser: public static ParseStructure and CreateBatches for transcript markers (>>>>>>>/<<<<<) and batching; RefinePipeline uses TranscriptParser.
- Unit tests: TranscriptParserTests (ParseStructure, CreateBatches), RefineJobQueryTests (SemanticKey filter, QueryBySemanticKey, GetById), InMemoryNodeStoreTests (GetNodeByScopeAndId, GetTreeByScope, CompleteNode).
