# Changelog

All notable changes to XtractManager (agent05-ui-control) are documented here.

## [Unreleased]

- Backend: исправлена ошибка gRPC `HTTP_1_1_REQUIRED` (0xd) при вызове agent04/agent06 по `http://`: включён `Http2UnencryptedSupport` в API (Program.cs), в README добавлена секция «gRPC по HTTP (без TLS)» с требованием к Kestrel agent04 (Protocols: Http1AndHttp2).
- Backend: интеграция с agent06 только по gRPC (RefinerGrpcClient, конфиг Agent06:GrpcAddress).
- Backend: DELETE /api/jobs/{id}, поля CreatedAt/CompletedAt в JobSnapshot и в списке, фильтр from/to в GET /api/jobs.
- Frontend: скелет UI (React + Vite + TypeScript), типы и API-слой (createJob, fetchJobsList, fetchJob, deleteJob, subscribeToJob с реконнектом), хуки useJob и useLogBuffer, компоненты StepCard, UploadCard, JobsList, LogsSection, ResultSection, макет Topbar/Sidebar и четыре шага (Upload, Transcriber, Refiner, Result), i18n (en/ru/es), AUTH.md.

## [0.1.0] — начальное состояние

- Backend: .NET 9, XtractManager.Instance, in-memory store, broadcaster, POST /api/jobs (multipart, tags), GET /api/jobs, GET /api/jobs/{id}, GET /api/jobs/{id}/stream (SSE), пайплайн транскрипция (agent04 gRPC) + refiner (agent06).
- Конфигурация: Agent04 (GrpcAddress, ConfigPath, WorkspaceRoot), Agent06 (GrpcAddress), Jobs (WorkspacePath).
- GET /health, OpenAPI в Development.
