# IProjectArtifactService / ProjectArtifactService

Единая точка доступа к **персистентному** состоянию job на диске (layout Agent04): разрешение корня артефактов, далее группировки, запись work state, pending_chunks, удаление субчанков и т.д. Реализация в `Agent04.Features.Transcription.Infrastructure.ProjectArtifactService`, контракт — `Agent04.Features.Transcription.Application.IProjectArtifactService`.

Подробный план внедрения: репозиторий `.cursor/plans/iprojectartifactservice_vm_и_merge.plan.md`.

## Решение по VM (gRPC групп / Stats)

- **Склейка VM (плейсхолдеры Pending, orphan-строки, activity log):** выполняется в **Agent04** (`ChunkVirtualModelMerge`), см. [CHUNK_VM_MERGE.md](./CHUNK_VM_MERGE.md). Запросы `GetJobStatus` / `StreamJobStatus` принимают `client_chunk_virtual_model`; ответ уже merged. Agent05 только передаёт снимок и сохраняет результат.
- **RPC группировок** (`GetChunkArtifactGroups`) по-прежнему отдаёт **только метаданные файлов**; строки VM для Stats в UI пока подмешиваются на клиенте к группам (`mergeChunkGroupVm`), пока контракт групп не расширят.

Комментарии в `Proto/transcription.proto` согласованы с этим разделением.

## Методы интерфейса (эволюция по фазам)

| Метод | Фаза | Назначение |
|--------|------|------------|
| `ResolveJobArtifactRoot(workspaceRootFull, agent04JobId, jobDirectoryRelative)` | 1 | Корень артефактов: реестр → валидированный `job_directory_relative` → legacy workspace или strict-ошибка. Возвращает `ArtifactRootResolutionResult`. |
| `GetChunkArtifactGroupsAsync(artifactRoot, totalChunksHint, ct)` + gRPC `GetChunkArtifactGroups` | 2 | Сгруппированные файлы job (семантика как `chunkArtifactGroups.ts` + `JobProjectFilesScanner`; сканер — `JobArtifactDirectoryScanner`). |
| `TryLoadWorkStateAsync`, `SaveWorkStateAsync`, `UpsertWorkStateChunkAsync`, `UpsertWorkStateSubChunkAsync` | 3 | Фасад над `TranscriptionWorkStateFile` (модели — `TranscriptionWorkStateDocument` в Application). |
| `WritePendingChunkIndicesAsync`, `TryLoadAndConsumePendingChunksAsync` | 3 | Очередь `pending_chunks.json`. |
| `GetCancellationManager` | 3 | Фабрика `.agent04_chunk_cancel` (делегирование `ICancellationManagerFactory`). |
| `InitializeJobMarkdownOutput`, `AppendJobMarkdownSegments`, `FinalizeJobMarkdownOutput`, `SaveJobCombinedTranscriptionJson`, `SaveJobPerChunkTranscriptionJson`, `ResetJobTranscriptionSpeakerMap` | 4 | Фасад над `ITranscriptionOutputWriter` (пайплайн / `RebuildCombined`). |
| `WriteSubChunkTranscriptionResult` | 4 | Делегирование `SubChunkResultWriter`. |
| `TryOperatorSplitAsync` | 4 | Операторский split (ffmpeg-сегменты в `sub_chunks/`). |
| `TryDeleteSubChunkArtifactsAsync` | 5 | Удаление audio/results, `chunk_N_merged.*`, cancel flag, строка work state (`TranscriptionWorkStateFile.TryRemoveSubChunkRowAsync`). gRPC: `ChunkCommand` + **DELETE_SUB_CHUNK**. |

Имена методов 3–5 уточняются при реализации; в таблице зафиксированы обязанности из плана миграции.
