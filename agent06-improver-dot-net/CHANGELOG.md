# Changelog

All notable changes to the agent06-improver-dot-net (TranslationImprover) project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

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
