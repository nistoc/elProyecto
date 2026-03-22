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
| `GetGroupedArtifactsAsync` (или эквивалент по имени из proto) | 2 | Сгруппированные файлы job (семантика как `chunkArtifactGroups.ts` + `JobProjectFilesScanner`). |
| `UpsertWorkState*` / загрузка контекста | 3+ | Атомарные обновления `transcription_work_state.json`, восстановление из диска. |
| `WritePendingChunks` / consume | 3+ | Очередь `pending_chunks.json`. |
| Операции cancel-сигналов | 3+ | Каталог `cancel_signals/` через политику сервиса. |
| Запись артефактов пайплайна (делегирование существующим writer’ам) | 4 | Split, sub-chunk results, transcript outputs — единая точка входа. |
| `DeleteSubChunkArtifacts` | 5 | Удаление файлов субчанка + согласованный work state / merged. |

Имена методов 3–5 уточняются при реализации; в таблице зафиксированы обязанности из плана миграции.
