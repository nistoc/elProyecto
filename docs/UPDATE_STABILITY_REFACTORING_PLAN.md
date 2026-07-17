# План рефакторинга: стабильная доставка апдейтов agent04 → agent05 → UI

Цель: фронт должен **всегда** сходиться к актуальному состоянию job — при обрывах сети, рестартах agent04, долгих чанках и конкурентных событиях. План разбит на независимые главы; каждая глава — самодостаточное ТЗ для исполнителя (субагента), выполняется отдельным коммитом и принимается ревьюером по чек-листу приёмки.

Диагноз (кратко): сегодня цепочка `agent04 —gRPC stream→ agent05 pipeline —Broadcaster→ SSE —EventSource→ React` рвётся в четырёх местах: (а) gRPC-стрим без keepalive и ретраев — падение убивает пайплайн навсегда; (б) SSE-эндпоинт пишет в Response конкурентно и без heartbeat, тихо закрывая стрим; (в) клиент после 5 обрывов синтезирует фейковый `done`; (г) общий мутируемый `JobSnapshot` даёт torn reads при сериализации.

---

## Глава 0. Общие правила для всех глав (читать исполнителю первой)

- Рабочая ОС — Windows; пути в конфигах абсолютные Windows-пути; шелл — PowerShell/Git Bash.
- **Скоуп**: менять только файлы, перечисленные в главе. Не переименовывать публичные типы/интерфейсы, если глава явно не велит. Не обновлять NuGet/npm-пакеты. Не менять `.proto`.
- Сборка/тесты перед сдачей главы:
  - agent05 API: `cd agent05-ui-control/API && dotnet build && dotnet test`
  - agent04 (если глава его трогает): `cd agent04 && dotnet test`
  - UI (если глава его трогает): `cd agent05-ui-control/UI && npm run build && npm run test && npm run lint`
- Один коммит на главу, conventional commits со скоупом: `refactor(agent05): …` / `fix(ui): …` / `fix(agent04): …`.
- Комментарии в коде — только там, где инвариант не видно из кода; стиль и плотность — как в соседнем коде (в проекте приняты XML-doc `<summary>` на публичных членах).
- Логи — через `ILogger`, таймстампы UTC уже настроены; не добавлять `Console.WriteLine`.
- Отчёт исполнителя: список изменённых файлов, что именно сделано по каждому пункту ТЗ, вывод `dotnet build`/`dotnet test`/`npm`-команд (полностью), отклонения от ТЗ с причиной.

Приёмка ревьюером (каждая глава): `git diff` построчно; сборка и тесты локально; сценарии из «Критериев приёмки»; несоответствие ТЗ → глава возвращается исполнителю с замечаниями.

---

## Глава 1. Долгоживущий gRPC-канал + HTTP/2 keepalive (agent05)

**Проблема.** `TranscriptionGrpcClient` и `RefinerGrpcClient` создают `GrpcChannel.ForAddress(...)` в каждом методе (`using var channel = ...`). Это TCP+HTTP/2 handshake на каждый вызов (а вызовы идут на каждый `GET /api/jobs/{id}` и каждый SSE-снапшот) и полное отсутствие keepalive: мёртвый сокет не детектится, `MoveNext` может виснуть бесконечно.

**Файлы.**
- `agent05-ui-control/API/Features/Jobs/Infrastructure/TranscriptionGrpcClient.cs`
- `agent05-ui-control/API/Features/Jobs/Infrastructure/RefinerGrpcClient.cs`

**Что сделать.**
1. В каждом клиенте создать **один** канал в конструкторе (поле `private readonly GrpcChannel _channel;`) и один сгенерированный gRPC-клиент на его основе. Убрать все `using var channel = GrpcChannel.ForAddress(...)` из методов.
2. Канал создавать с явным `SocketsHttpHandler`:
   ```csharp
   var handler = new SocketsHttpHandler
   {
       KeepAlivePingDelay = TimeSpan.FromSeconds(30),
       KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
       KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
       PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
       EnableMultipleHttp2Connections = true,
   };
   _channel = GrpcChannel.ForAddress(_address, new GrpcChannelOptions { HttpHandler = handler });
   ```
3. Реализовать `IDisposable` на обоих клиентах (`_channel.Dispose()`); они зарегистрированы как singleton в `Program.cs` — DI сам вызовет Dispose при остановке хоста. Регистрацию в `Program.cs` не менять.
4. Поведение методов (сигнатуры, маппинг, обработка ошибок) — без изменений.

**Чего не делать.** Не добавлять ретраи (это Глава 2). Не трогать пайплайн, контроллер, интерфейсы `ITranscriptionServiceClient`/`IRefinerServiceClient`.

**Критерии приёмки.**
- `dotnet build` и `dotnet test` в `agent05-ui-control/API` зелёные.
- В обоих файлах не осталось ни одного `GrpcChannel.ForAddress` внутри методов (только в конструкторе).
- grep по `using var channel` в этих файлах пуст.

---

## Глава 2. Устойчивое потребление стрима статусов в пайплайне (agent05)

**Проблема.** В `TranscriptionRefinerPipeline.RunAsync` оба `await foreach` (agent04 и agent06) не защищены: любой `RpcException`/`IOException` из стрима вылетает наружу, `RunAsync` умирает, job навсегда остаётся `running`, UI не получает ни `failed`, ни `done`. Плюс `MapState` маппит неизвестные состояния (включая `"NotFound"` от agent04) в `"running"`.

**Файлы.**
- `agent05-ui-control/API/Features/Jobs/Infrastructure/TranscriptionRefinerPipeline.cs`

**Что сделать.**
1. Вынести потребление стрима agent04 в приватный метод c retry-циклом. Логика:
   - Внешний цикл `while (true)`. Внутри — `try { await foreach (...) { обработка update как сейчас; if (терминальное) return; } } catch (Exception ex) when (ex is RpcException or IOException or HttpRequestException) { ... }`.
   - При обрыве: лог `LogWarning` с номером попытки; задержка backoff `1s, 2s, 5s, 15s, 15s, …` (cap 15s); затем **однократный** `GetJobStatusAsync`: если он вернул терминальное состояние (`Completed`/`Failed`/`Cancelled`/`NotFound`) — обработать его как обычный update и выйти; если нетерминальное или `null` — пересоздать стрим (следующая итерация внешнего цикла), передав актуальный `ChunkVirtualModel` из стора как seed (как это делается при первом вызове).
   - Ограничитель: после **10 подряд** неудачных циклов (обрыв + неудачный `GetJobStatusAsync`) — прекратить, пометить job `failed` (см. п.3). Счётчик сбрасывать при каждом успешно полученном update.
2. `MapState`: добавить `"NotFound" => "failed"`; при обработке update со `State == "NotFound"` писать `TranscriptionError = "Agent04 no longer knows this job (restart?)"`, `Phase = "idle"`. Убрать «оптимистичный» default → вместо `_ => "running"` сделать `_ => "running"` можно оставить только для действительно неизвестных строк, но `NotFound` обязан обрабатываться до default.
3. Обернуть всё тело `RunAsync` в `try/catch (Exception ex)`: в catch — `LogError`, перевести job в `Status="failed"`, `Phase="idle"`, `TranscriptionError=ex.Message`, `Broadcast(jobId, "snapshot", ...)` и `Broadcast(jobId, "done", null)`. Пайплайн не имеет права умирать молча. (Существующий `RunPipelineInBackground` в контроллере не трогать — его catch станет последним рубежом.)
4. Ту же retry-обёртку применить к `_refiner.StreamRefineStatusAsync` (fallback-опрос — `GetRefineStatusAsync`, если такого метода в `IRefinerServiceClient` нет — только реконнект стрима без опроса, метод в интерфейс не добавлять).
5. Починить попутный баг: в статус-событии рефайнера (строка ~274) используется устаревший `snap` — брать свежий снапшот из стора либо явно вычислять phase/status из `update.State`.

**Чего не делать.** Не менять SSE-эндпоинт, Broadcaster, сторы, интерфейсы клиентов, gRPC-клиенты (Глава 1 уже дала им keepalive).

**Критерии приёмки.**
- `dotnet build`/`dotnet test` зелёные.
- Ревьюер прогоняет сценарий: запустить agent04 и agent05, залить файл, **убить agent04 в середине транскрипции**, подождать 30 с, поднять agent04. Ожидание: пайплайн логирует reconnect-попытки; job либо продолжает получать статусы (agent04 хранит jobs в памяти — после рестарта будет `NotFound`), либо детерминированно завершается `failed` с внятным `TranscriptionError` и событием `done`. Job, «висящий в running навсегда», — провал приёмки.

---

## Глава 3. Переписать SSE-эндпоинт и Broadcaster (agent05)

**Проблема.** `JobsController.Stream`: снапшот отправляется **до** подписки (события в окне теряются, включая `done`); `Send` пишет в Response синхронно (`GetAwaiter().GetResult()`) и вызывается конкурентно несколькими издателями → порванные фреймы; любое исключение записи тихо завершает стрим; heartbeat нет; `done` детектится по `payload.Contains("\"type\":\"done\"")`. `Broadcaster`: гонка Subscribe/Unsubscribe (подписка в список, уже удалённый из словаря → «осиротевший» подписчик), возможный повторный вызов фабрики `AddOrUpdate` → дубль подписки.

**Файлы.**
- `agent05-ui-control/API/Features/Jobs/Application/IBroadcaster.cs`
- `agent05-ui-control/API/Features/Jobs/Infrastructure/Broadcaster.cs`
- `agent05-ui-control/API/Controllers/JobsController.cs` (метод `Stream` и `PublishEnrichedSnapshotAsync`)
- `agent05-ui-control/API/Features/Jobs/Infrastructure/TranscriptionRefinerPipeline.cs` (только точки вызова `Publish`)
- Тесты: `agent05-ui-control/API.Tests/` — добавить юнит-тесты Broadcaster.

**Что сделать.**
1. Типизировать событие: в `IBroadcaster.cs` завести `public sealed record BroadcastEvent(string Type, string Json);` где `Json` — уже сериализованная полная строка события (как сейчас). Интерфейс:
   ```csharp
   IDisposable Subscribe(string jobId, ChannelWriter<BroadcastEvent> writer);
   void Publish(string jobId, BroadcastEvent evt);
   ```
2. `Broadcaster`: `ConcurrentDictionary<string, ConcurrentDictionary<Guid, ChannelWriter<BroadcastEvent>>>`. `Subscribe` добавляет writer под новым `Guid` и возвращает `IDisposable`, удаляющий его; пустые внутренние словари можно не удалять (джобов немного) — это устраняет гонку удаления списка целиком. `Publish` → `TryWrite` каждому writer'у (не блокирует; переполненный канал теряет старое — см. п.3).
3. `JobsController.Stream` переписать:
   - `var channel = Channel.CreateBounded<BroadcastEvent>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });`
   - **Сначала** `using var sub = _broadcaster.Subscribe(id, channel.Writer);`, **потом** прочитать снапшот из стора и отправить его первым событием (порядок гарантирует отсутствие потерянного окна; дубль статуса после снапшота допустим — клиент идемпотентен).
   - Единственный цикл записи: `await foreach (var evt in channel.Reader.ReadAllAsync(ct))` → `WriteAsync($"data: {evt.Json}\n\n", ct)` + `FlushAsync`; после события с `evt.Type == "done"` — выход из цикла и завершение запроса.
   - Heartbeat: параллельная задача с `PeriodicTimer` раз в 15 с кладёт в **тот же канал** событие типа `"ping"` (`{"type":"ping"}`), цикл записи для `ping` пишет SSE-комментарий `: ping\n\n` (комментарий, не data-фрейм — клиентский `onmessage` его не увидит, но соединение живо для прокси). Таймер останавливать при завершении цикла.
   - Никаких `GetAwaiter().GetResult()`; исключения записи (клиент ушёл) — нормальное завершение метода, без глотания через TCS.
4. Обновить всех издателей: `TranscriptionRefinerPipeline.Broadcast` и `JobsController.PublishEnrichedSnapshotAsync` формируют `BroadcastEvent(type, json)` (json — та же строка, что сейчас). Строковый маркер `Contains("\"type\":\"done\"")` удалить.
5. Юнит-тесты Broadcaster: (а) publish до subscribe не падает; (б) два подписчика получают событие; (в) после `Dispose` подписки события не приходят; (г) конкурентные Subscribe/Unsubscribe/Publish из `Parallel.For` не кидают и не теряют живого подписчика (smoke на 10k итераций).

**Чего не делать.** Не менять формат JSON событий (`{"type":"snapshot","payload":{...}}` и т.д.) — UI парсит его как есть. Не менять пайплайн-логику, только вызовы `Publish`.

**Критерии приёмки.**
- `dotnet build`/`dotnet test` зелёные, новые тесты Broadcaster есть и проходят.
- В `JobsController` нет `GetAwaiter().GetResult()` и нет `Contains("\"type\"`.
- Ревьюер: запустить пайплайн с реальным файлом, открыть вкладку — снапшот приходит мгновенно, статусы идут, по завершении приходит `done` и стрим закрывается; в idle-паузе >1 мин соединение не рвётся (heartbeat виден в `curl -N`).

---

## Глава 4. Клиент UI: честный реконнект + polling-fallback

**Проблема.** `api.ts/subscribeToJob`: счётчик `attempt` не сбрасывается при успешном коннекте — 5 обрывов за всю жизнь вкладки, и клиент синтезирует **фейковый** `{type:'done'}`; `useJob` на нём отписывается и прыгает на Result, хотя job идёт. Реконнект — фикс 2 с без backoff. Polling-fallback отсутствует.

**Файлы.**
- `agent05-ui-control/UI/src/api.ts`
- `agent05-ui-control/UI/src/hooks/useJob.ts`
- `agent05-ui-control/UI/src/types.ts` (если нужно расширить `StreamEvent`)
- Тесты: `agent05-ui-control/UI/src/*.spec.ts` (vitest) — на чистые функции, которые появятся.

**Что сделать.**
1. `subscribeToJob`:
   - `eventSource.onopen = () => { attempt = 0; }` (сброс при успешном коннекте). Дополнительно сбрасывать при первом полученном сообщении.
   - Backoff: `delay = Math.min(30_000, 1000 * 2 ** attempt) + Math.random() * 500`.
   - **Удалить синтез `done`.** Вместо этого ввести колбэк-сигнал о деградации: `onEvent({ type: 'connection', payload: { state: 'reconnecting' | 'polling' | 'connected' } })` (расширить union `StreamEvent` в `types.ts`). После исчерпания `MAX_RECONNECT_ATTEMPTS` (оставить 5) — перейти в **polling-режим**: каждые 5 с `fetchJob(jobId)`; каждый 6-й тик (~30 с) — попытка снова поднять EventSource; при успехе — выйти из polling, `state:'connected'`.
   - В polling-режиме каждый полученный снапшот доставлять как `onEvent({ type: 'snapshot', payload })`; если снапшот терминальный (`status === 'done' || status === 'failed'`) — доставить `{ type: 'done' }` (настоящий терминал, узнанный из данных) и остановить всё.
   - `close()` обязан останавливать и таймеры polling/reconnect.
2. `useJob`:
   - Обрабатывать `type: 'connection'`: держать в стейте `connectionState: 'connected' | 'reconnecting' | 'polling'` и отдавать из хука (для индикатора в шапке; UI-компонент в этой главе не делать, только вернуть значение).
   - На `type: 'done'` поведение прежнее (unsubscribe + финальный fetch), но переход `setActiveStepState('result')` выполнять **только** если полученный снапшот терминальный (`status === 'done'`), а не «не failed».
   - Дополнительная защита: если пришёл `snapshot` с терминальным `status` (`done`/`failed`), а событие `done` ещё не приходило — вести себя как при `done` (терминальность определяется данными, не только событием).
3. Вынести чистую функцию `computeReconnectDelay(attempt): number` и `isTerminalSnapshot(snap): boolean` и покрыть их vitest-тестами (backoff растёт, cap 30 c; терминальность по status).

**Чего не делать.** Не трогать серверный код. Не менять существующие типы событий `snapshot`/`status`/`done` (только добавить `connection`). Не добавлять UI-компонентов.

**Критерии приёмки.**
- `npm run build`, `npm run test`, `npm run lint` зелёные; новые тесты есть.
- Ревьюер-сценарий: открыть вкладку с идущим job, **остановить agent05 API на ~1 мин**, поднять. Ожидание: UI не прыгает на Result, показывает reconnecting/polling, после поднятия API догоняет состояние и продолжает получать статусы. Фейкового `done` больше нет по коду (grep `type: 'done'` в api.ts допустим только в polling-ветке терминального снапшота).

---

## Глава 5. Потокобезопасные снапшоты job (agent05)

**Проблема.** `InMemoryJobStore.GetAsync` возвращает **тот же** мутируемый экземпляр `JobSnapshot`, который конкурентно мутируют пайплайн и HTTP-потоки (`MergeAgent04LiveIntoSnapshotAsync`) и конкурентно сериализует SSE/REST. Итерация `List<>` во время замены → torn reads / `InvalidOperationException`.

**Файлы.**
- `agent05-ui-control/API/Features/Jobs/Application/IJobStore.cs` (модель `JobSnapshot` — добавить метод клонирования; интерфейс стора не менять)
- `agent05-ui-control/API/Features/Jobs/Infrastructure/InMemoryJobStore.cs`
- `agent05-ui-control/API/Features/Jobs/Infrastructure/WorkspaceAwareJobStore.cs` (проверить, что делегирует и не кэширует ссылки)

**Что сделать.**
1. Добавить `JobSnapshot Clone()` — глубокая копия: списки (`Tags`, `Logs`, `Chunks.Completed/Active`, `ChunkVirtualModel` и все прочие коллекции) копировать (`ToList()`/новые объекты вложенных классов). Найти **все** коллекции по объявлению модели, не по памяти.
2. `InMemoryJobStore`: хранить эталон в словаре; `GetAsync` → `snap.Clone()`; `UpdateAsync` → под `lock` на per-job объекте (`ConcurrentDictionary<string, object> _locks`): применить `update(эталон)`. `ListAsync` — строится из эталонов; выполнять проекцию под тем же lock per job либо по клону.
3. Проверить `WorkspaceAwareJobStore`: если он где-то возвращает/хранит ссылку на снапшот из внутреннего стора — теперь это клоны; поправить места, которые рассчитывали на мутацию по ссылке (например, если после `GetAsync` кто-то мутирует снапшот и ожидает, что это видно всем, — такие места перевести на `UpdateAsync`). Известный кандидат: `JobsController.MergeAgent04LiveIntoSnapshotAsync` мутирует результат `GetAsync` — это **станет корректным** (локальная копия для ответа), менять не нужно, но убедиться, что нигде нет паттерна «GetAsync → мутация → ожидание общей видимости» (grep всех вызовов `GetAsync`).
4. Юнит-тест: параллельно 1000 `UpdateAsync` (замена `Chunks.Completed` новым списком) и 1000 `GetAsync` + `JsonSerializer.Serialize` клона — без исключений.

**Чего не делать.** Не переходить на immutable records (слишком широкий рефакторинг), не менять сигнатуры `IJobStore`.

**Критерии приёмки.** Build/tests зелёные, новый конкурентный тест проходит; grep подтверждает, что `GetAsync` больше нигде не используется для «общей» мутации.

---

## Глава 6. Фоновое исполнение пайплайна без fire-and-forget (agent05)

**Проблема.** `RunPipelineInBackground` — `async void` в контроллере; `ScheduleTranscribeSubFollowUpSnapshots` — 7 несвязанных `Task.Run` с фикс-задержками (600 мс…120 с), без отмены и без учёта удаления job; при остановке хоста всё это обрывается молча.

**Файлы.**
- `agent05-ui-control/API/Controllers/JobsController.cs`
- Новый: `agent05-ui-control/API/Features/Jobs/Infrastructure/PipelineBackgroundService.cs`
- `agent05-ui-control/API/Program.cs` (регистрация)

**Что сделать.**
1. `PipelineBackgroundService : BackgroundService` с `Channel<string>` (unbounded) очередью jobId; выделенный интерфейс `IPipelineQueue { void Enqueue(string jobId); }`, реализованный тем же классом; регистрация singleton + `AddHostedService`. `ExecuteAsync`: читает канал, для каждого jobId запускает `_pipeline.RunAsync(jobId, stoppingToken)` в отдельной задаче (конкурентность между джобами сохранить — не сериализовывать), учитывает `stoppingToken`.
2. Контроллер `Create`: вместо `RunPipelineInBackground(jobId)` → `_queue.Enqueue(jobId)`. Метод `RunPipelineInBackground` удалить.
3. `ScheduleTranscribeSubFollowUpSnapshots` заменить на один `Task.Run` с циклом по задержкам `[600, 1400, 4000, 9000, 15000, 30000, 60000]` мс (кумулятивно те же точки), с `CancellationToken` от `IHostApplicationLifetime.ApplicationStopping` и выходом из цикла, если job удалён из стора (`GetAsync == null`).
4. `RunAsync(jobId, ct)` в пайплайне уже принимает ct — прокинуть `stoppingToken`, чтобы при остановке хоста пайплайн корректно отменялся.

**Чего не делать.** Не делать персистентную очередь/восстановление после рестарта (вне скоупа). Не менять `IPipeline`.

**Критерии приёмки.** Build/tests зелёные; `async void` в контроллере отсутствует; загрузка файла запускает пайплайн (ручная проверка); `Ctrl+C` останавливает API без зависших тасок (лог чистый).

---

## Глава 7. agent04: корректное завершение StreamJobStatus

**Проблема.** В `TranscriptionGrpcService.StreamJobStatus` цикл делает `return` только при `Failed`/`Cancelled`; при `Completed` стрим остаётся открыт и опрашивает стор вечно (клиент agent05 выходит из `await foreach` сам, но любой другой потребитель повиснет, и на сервере крутится пустой poll-цикл).

**Файлы.**
- `agent04/Agent04/Services/TranscriptionGrpcService.cs` (метод `StreamJobStatus`)
- Тесты agent04, если есть покрывающие (`agent04/Agent04.Tests`).

**Что сделать.**
1. После отправки сообщения завершать стрим на **любом** терминальном состоянии: `if (job.State is JobState.Completed or JobState.Failed or JobState.Cancelled) return;`.
2. Убедиться, что сообщение с терминальным состоянием гарантированно отправляется до `return` (сейчас отправка происходит только при изменении сигнатуры — терминальное состояние всегда меняет `state`, этого достаточно; ничего не менять, только проверить).
3. `dotnet test` agent04 — зелёные.

**Чего не делать.** Не менять семантику `NotFound` (agent05 обрабатывает её в Главе 2). Не трогать `.proto`.

**Критерии приёмки.** Build/tests agent04 зелёные; ручная проверка `grpcurl`-ом или через agent05: после завершения job стрим со стороны agent04 закрывается сам.

---

## Глава 8. UI: дебаунс тяжёлых рефетчей по снапшотам

**Проблема.** Каждый SSE-снапшот (при активной транскрипции — каждые ~2 с) инкрементирует `jobSnapshotRevision`, на который подписаны `useJobProjectFiles` и `useJobChunkArtifactGroups` — оба дергают agent05, который дергает agent04 по gRPC. Шторм запросов на ровном месте.

**Файлы.**
- `agent05-ui-control/UI/src/hooks/useJob.ts` (или точечно в `useJobProjectFiles.ts` / `useJobChunkArtifactGroups.ts` — на выбор исполнителя, объяснить выбор в отчёте)

**Что сделать.**
1. Дебаунсить каскад рефетчей: ревизия (или эффекты на неё) должна приводить к запросу не чаще **1 раза в 3 с** (trailing edge: последний снапшот в окне всё равно приводит к рефетчу). Ручные обновления (кнопка Refresh, `refreshJobSnapshot` после chunk-action) — **без** дебаунса.
2. Чистую функцию/хук дебаунса вынести и покрыть vitest-тестом (fake timers): N быстрых инкрементов → 1 вызов + 1 trailing.

**Чего не делать.** Не менять серверные эндпоинты; не менять поведение при `done` (финальный рефетч должен пройти).

**Критерии приёмки.** `npm run build/test/lint` зелёные; в DevTools Network при активном job запросы `/files` и `/chunk-artifact-groups` идут ~раз в 3 с, а не раз в 2 с ×2.

---

## Глава 9 (опционально, требует решения владельца). Судьба логов в UI

`JobSnapshot.Logs` объявлен, но никогда не заполняется; SSE-событий `log` не существует; панель логов UI всегда пуста (в старом agent-browser логи были — это осознанная потеря паритета, зафиксированная в PARITY-доке лишь частично).

- **Вариант A (дёшево):** удалить мёртвое поле `Logs`, `useLogBuffer`, `LogsSection`-контент свести к подсказкам фаз. Честный UI без пустой панели.
- **Вариант B (дорого):** реальный стрим логов agent04 → нужен новый gRPC-стрим в `.proto` agent04, буфер в agent05, событие `log` в SSE, вирт-скролл в UI.

Решение за владельцем; до решения главу не исполнять.

---

## Порядок исполнения и зависимости

```
Гл.1 ──→ Гл.2          (retry строится на живом канале)
Гл.3 ──→ Гл.4          (клиент опирается на heartbeat и честный done)
Гл.5, Гл.6, Гл.7, Гл.8 — независимы, в любой момент после Гл.1–4
Гл.9 — после решения по варианту
```

Рекомендуемая последовательность: **1 → 2 → 3 → 4 → 5 → 7 → 6 → 8**. После Гл.4 провести совместный смоук: полный прогон файла с рестартами agent04 и agent05 в процессе — UI обязан сойтись к финальному состоянию без ручного F5.
