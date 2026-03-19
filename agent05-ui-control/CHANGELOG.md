# Changelog

All notable changes to XtractManager (agent05-ui-control) are documented here.

## [Unreleased]

- Backend/UI (шаг 8 плана): поле **JobSnapshot.files** больше не заполняется; плоский сканер корня **JobDirectoryFileScanner** удалён. Список файлов только через **GET /api/jobs/{id}/files**. README: диаграмма потока данных (mermaid).
- Backend: **PUT /api/jobs/{id}/files/content?path=** — сохранение существующего текстового файла в каталоге задания (UTF-8, лимит 50 MB); общая валидация пути с GET. Frontend: кнопка «Править» / модальное окно для `kind === text`.
- Backend: пайплайн передаёт в agent06 **OutputFilePath** — относительный путь `{jobId}/transcript_fixed.md` (корень = `Agent06:WorkspaceRoot` или при пустом значении — `Jobs:WorkspacePath`), чтобы refined-транскрипт появлялся в папке задания при совпадении workspace с agent06. Конфиг `Agent06:WorkspaceRoot`, свойство `IJobWorkspace.WorkspaceRootPath`.
- Backend: **GET /api/jobs/{id}/files** — структурированный список файлов задания (категории как в agent-browser); **GET /api/jobs/{id}/files/content?path=** — безопасная раздача файла из каталога задания (Range для аудио). README: таблица эндпоинтов и разделы «Файлы проекта», «Раздача файла».
- Backend: исправлена ошибка gRPC `HTTP_1_1_REQUIRED` (0xd) при вызове agent04/agent06 по `http://`: включён `Http2UnencryptedSupport` в API (Program.cs); в Agent04 добавлен отдельный порт 5032 для gRPC (h2c) через `ConfigureKestrel` (Http2 + AllowAlternateSchemes), REST на 5034; в README — секция «gRPC по HTTP (без TLS)».
- Backend: интеграция с agent06 только по gRPC (RefinerGrpcClient, конфиг Agent06:GrpcAddress).
- Backend: DELETE /api/jobs/{id}, поля CreatedAt/CompletedAt в JobSnapshot и в списке, фильтр from/to в GET /api/jobs.
- Frontend: скелет UI (React + Vite + TypeScript), типы и API-слой (createJob, fetchJobsList, fetchJob, deleteJob, subscribeToJob с реконнектом), хуки useJob и useLogBuffer, компоненты StepCard, UploadCard, JobsList, LogsSection, ResultSection, макет Topbar/Sidebar и четыре шага (Upload, Transcriber, Refiner, Result), i18n (en/ru/es), AUTH.md.

## [0.1.0] — начальное состояние

- Backend: .NET 9, XtractManager.Instance, in-memory store, broadcaster, POST /api/jobs (multipart, tags), GET /api/jobs, GET /api/jobs/{id}, GET /api/jobs/{id}/stream (SSE), пайплайн транскрипция (agent04 gRPC) + refiner (agent06).
- Конфигурация: Agent04 (GrpcAddress, ConfigPath, WorkspaceRoot), Agent06 (GrpcAddress), Jobs (WorkspacePath).
- GET /health, OpenAPI в Development.
