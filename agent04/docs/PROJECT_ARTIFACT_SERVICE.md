# IProjectArtifactService / ProjectArtifactService

Единая точка доступа к **персистентному** состоянию job на диске (layout Agent04): разрешение корня артефактов, далее группировки, запись work state, pending_chunks, удаление субчанков и т.д. Реализация в `Agent04.Features.Transcription.Infrastructure.ProjectArtifactService`, контракт — `Agent04.Features.Transcription.Application.IProjectArtifactService`.

Подробный план внедрения: репозиторий `.cursor/plans/iprojectartifactservice_vm_и_merge.plan.md`.

## Решение по VM (gRPC групп / Stats)

До **фазы 7** новый RPC группировок (фаза 2) возвращает **только метаданные файлов** на диске. Строки виртуальной модели чанков (`ChunkVirtualModelEntry` / merge плейсхолдеры) по-прежнему склеиваются в **Agent05** (`ChunkVirtualModelMerge` + снимок job), чтобы не дублировать источник VM и не ломать гонки стрима. После выравнивания инвариантов в Agent04 политику можно сузить.

Комментарий в `Proto/transcription.proto` дублирует это решение для авторов контрактов.

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
