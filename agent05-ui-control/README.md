# XtractManager (agent05-ui-control)

Аналог agent-browser: веб-UI и API для пайплайна **транскрипция (agent04) + refiner (agent06)**. Интеграция с agent04 и agent06 — только по **gRPC**.

## Структура

- **API/** — бекенд (.NET 9): решение `Api.slnx` (проекты Api, API.Tests). Сборка и запуск из `API/`.
- **UI/** — фронтенд (React 18 + Vite, TypeScript). Сборка и запуск из `UI/`.
- **docs/** — стандарты (API_STANDARDS.md, AUTH.md, FRONTEND_STANDARDS.md).

## Запуск

### Требования

- .NET 9 (API)
- Node.js 18+ (UI)
- Запущенные **agent04** (gRPC) и **agent06** (gRPC) на нужных портах

### API

```bash
cd agent05-ui-control/API
dotnet build
dotnet run
```

По умолчанию API слушает **http://localhost:5010**.

Для использования своих адресов agent04/agent06 задайте переменную окружения и при необходимости скопируйте/отредактируйте `appsettings.Development.json`:

```bash
# Windows (PowerShell)
$env:ASPNETCORE_ENVIRONMENT="Development"; dotnet run

# Windows (CMD) / Linux / macOS
set ASPNETCORE_ENVIRONMENT=Development   # CMD
dotnet run
```

В `appsettings.Development.json` по умолчанию: Agent04 gRPC — http://localhost:5032, Agent06 — http://localhost:5033. Адреса можно изменить там же; для Agent04 REST/OpenAPI настройте порты в launchSettings самого agent04.

### UI

```bash
cd agent05-ui-control/UI
npm install
npm run dev
```

UI откроется на **http://localhost:5173** и по прокси обращается к API на http://localhost:5010. Убедитесь, что API запущен.

Сборка UI для продакшена:

```bash
npm run build
```

Артефакты — в `UI/dist/`.

## Конфигурация API

Файл: `API/appsettings.json` (и при необходимости `appsettings.Development.json`).

| Секция       | Ключ           | Описание                                              |
|-------------|----------------|--------------------------------------------------------|
| Kestrel     | Endpoints:Http | URL API (по умолчанию http://localhost:5010)          |
| Agent04     | GrpcAddress   | Адрес gRPC agent04 (транскрипция). Должен указывать на порт h2c, напр. http://localhost:5032 (не REST 5034). |
| Agent04     | ConfigPath    | Путь к конфигу agent04 (например config/default.json) |
| Agent04     | WorkspaceRoot | Рабочий каталог agent04 (пустой — подставляется Jobs:WorkspacePath) |
| Agent06     | GrpcAddress   | Адрес gRPC agent06 (refiner)                          |
| Jobs        | WorkspacePath | Каталог для рабочих данных заданий (аудио, артефакты). Абсолютный путь или относительный от Content Root. По умолчанию `./runtime`. Логи приложения — в корень запущенного проекта. |

При старте в лог выводится фактический путь: `Job workspace base path (Jobs:WorkspacePath): ...`. Оба внешних сервиса вызываются только по gRPC; REST для agent06 не используется.

### gRPC по HTTP (без TLS)

При работе по `http://` (без HTTPS) gRPC использует HTTP/2 «prior knowledge» (h2c). Чтобы избежать ошибки `HTTP_1_1_REQUIRED` (0xd):

- **agent05**: в `Program.cs` включён `AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true)` — клиент подключается по h2c к agent04 и agent06.
- **agent04**: на Windows без TLS Kestrel по умолчанию не включает HTTP/2. В Agent04 в `Program.cs` настроен отдельный порт **5032** только для gRPC (h2c): `ConfigureKestrel` с `AllowAlternateSchemes = true` и `ListenLocalhost(5032, HttpProtocols.Http2)`. REST и OpenAPI слушают на порту **5034** (applicationUrl в launchSettings). Адрес `Agent04:GrpcAddress` в agent05 должен указывать на **http://localhost:5032**.

## Endpoints

| Метод  | Путь                    | Описание |
|--------|-------------------------|----------|
| GET    | /health                 | Проверка состояния (JSON: status, service) |
| GET    | /api/jobs               | Список заданий. Query: semanticKey, status, from, to, limit, offset. Ответ: `{ "jobs": JobListItem[] }`. |
| GET    | /api/jobs/{id}          | Снепшот задания (JobSnapshot). 404 если нет. |
| POST   | /api/jobs               | Создать задание. Form: file (обязательно), tags (опционально, строка через запятую). Ответ: 202, `{ "jobId": string }`. Пайплайн запускается в фоне (agent04 → agent06). Лимит тела запроса 512 MB. |
| DELETE | /api/jobs/{id}          | Удалить задание. 204 при успехе, 404 если нет. |
| GET    | /api/jobs/{id}/stream   | SSE: первый ответ — snapshot (type: "snapshot", payload: JobSnapshot), далее события type: "status" | "done". При "done" стрим завершается. |

В Development при включённом OpenAPI: **GET /openapi/v1.json** — схема API.

## Тесты API

```bash
cd agent05-ui-control/API
dotnet test
```

## Документация и план

- **docs/API_STANDARDS.md** — стандарты бекенда.
- **docs/FRONTEND_STANDARDS.md** — стандарты фронтенда.
- **docs/AUTH.md** — закладка под авторизацию.
- План разработки и спецификация UI — в общем плане сервиса (agent05, этапы 1–9 и п. 4a).
