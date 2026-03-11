# RENTGEN — Виртуальная абстрактная модель (TranslationImprover)

## Принципы

- **Read-модель над хранилищем заданий:** запись выполняют только Submit (создание job) и пайплайн (обновление статуса и узлов).
- **Доступ по идентификатору job и по семантическому ключу:** запрос по job id; фильтр списка заданий по одному из тегов job'а (semanticKey).
- **Дерево узлов по scopeId = job_id:** обновление при границах шагов (EnsureNode, StartNode, CompleteNode); прогресс по узлу (UpdateNodeProgress). Рассчитано на опрос 0,01–10 Hz.

## Терминология

### Tags (теги задания)

- **Tags** — массив строк, привязанных к **job'у** при создании (SubmitRefineJob). Передаётся в теле: `tags` (REST), `repeated string tags` (gRPC). Хранится в RefineJobStatus.
- Назначение: метки для фильтрации и семантического поиска (например, идентификатор сценария или пользователя).

### Semantic key (семантический ключ)

- **Семантический ключ** — это **один из тегов job'а**. Используется **только для фильтрации списка заданий**.
- REST: параметр запроса **semanticKey**: `GET /api/refine/jobs/query?semanticKey=...&status=...&from=...&to=...&limit=50&offset=0`.
- gRPC: поле **semantic_key** в QueryRefineJobsRequest.
- В фильтре хранилища: поле **SemanticKey** в RefineJobListFilter (сравнение без учёта регистра).

### Tag в контексте узлов (nodes)

- В эндпоинте **GET /api/refine/jobs/{id}/nodes** параметр **tag** означает **идентификатор узла (node id)** в рамках scope = job id.
- Если `tag` передан — возвращается один узел: GetNodeByScopeAndId(scopeId, tag). Иначе — плоский список (GetByScope) или дерево (GetTreeByScope).
- То есть «tag» в nodes — это не семантический ключ job'а, а id/имя узла (например, `tag=refine:batch-2`).

### X-Ray

- **X-Ray** в смысле RENTGEN — атрибуты на **методах** пайплайна, обновляющих виртуальную модель (EnsureNode, StartNode, CompleteNode). Не путать с `Activity.SetTag` (трассировка); теги Activity не являются XRay виртуальной модели.

## Модель узлов

- Контракт узла: **NodeInfo** — Id, ParentId, ScopeId, Kind, Status (RefineJobState), StartedAt, CompletedAt, UpdatedAt, ProgressPercent, Phase, ErrorMessage, Metadata, Children.
- Иерархия для refinement:
  - Корень: `id = jobId`, `parentId = null`, `scopeId = jobId`, `kind = "job"`.
  - Фаза: `jobId:refine`, parentId = jobId, kind = "phase".
  - Батчи: `jobId:refine:batch-0`, `jobId:refine:batch-1`, … parentId = `jobId:refine`, kind = "batch".

## API виртуальной модели

- `GET /api/refine/jobs/query?semanticKey=...&status=...&from=...&to=...&limit=50&offset=0` — список заданий с полным статусом (semanticKey = один из Tags job'а). OpenAPI tag: "Virtual model (RENTGEN)".
- `GET /api/refine/jobs/{id}/nodes?tree=true|false&tag=...` — узлы по job; при `tag` — один узел по node id. 404 если job или узел не найден.

## Реализация

- **INodeModel** / **INodeQuery** и **NodeInfo** в слайсе Refine (Application). Реализация: **InMemoryNodeStore** (один класс для записи и чтения).
- **IRefineJobQueryService**: GetById, Query(RefineJobListFilter), QueryBySemanticKey — слайс RefineJobQuery, зависимость только от IRefineJobStore.
- Запись в модель узлов — только в пайплайне на границах шагов (EnsureNode, StartNode, CompleteNode, UpdateNodeProgress).
