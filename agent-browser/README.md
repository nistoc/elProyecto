# Agent Browser UI & Node API

Одностраничное веб-приложение на React + Vite со встроенным Node API-роутером, который запускает Python-агентов:
- **Transcriber Agent** (`agent01`): распознаёт аудио, пишет `transcript.md` и `response.json`.
- **Refiner Agent** (`agent03-trans-improver`): улучшает транскрипт и сохраняет `transcript_fixed*.md`.
- API стримит логи через **SSE**; фронт показывает шаги, прогресс и выдаёт ссылки на артефакты.
- Интерфейс переключается между **EN/RU/ES**.

## Стек
- Frontend: React 18, TypeScript, Vite, стили на custom CSS; i18n — простой контекст; SSE для live-логов.
- Backend: Node 18+, Express, Multer (upload), `child_process.spawn` для запуска Python-агентов, SSE.
- Прочее: `concurrently` для одновременного запуска UI и API.

## Структура

```
agent-browser/
├─ src/                      # React UI
│  ├─ App.tsx                # Главный компонент (использует хуки)
│  ├─ api.ts                 # API клиент + SSE с reconnect
│  ├─ i18n.tsx               # Контекст перевода (EN/RU/ES)
│  ├─ types.ts               # TypeScript типы
│  ├─ style.css              # Стили
│  ├─ main.tsx               # Точка входа React
│  ├─ components/            # UI компоненты
│  │  ├─ index.ts            # Barrel export
│  │  ├─ StepCard.tsx        # Карточка шага
│  │  ├─ LogPanel.tsx        # Панель логов
│  │  ├─ UploadCard.tsx      # Загрузка файла
│  │  ├─ ChunkControlPanel.tsx # Управление чанками
│  │  ├─ LogsSection.tsx     # Секция логов с паузой
│  │  └─ ResultSection.tsx   # Результаты и ссылки
│  └─ hooks/                 # React хуки
│     ├─ index.ts            # Barrel export
│     ├─ useJob.ts           # Главный хук для работы с job
│     ├─ useChunkState.ts    # Управление состоянием чанков
│     └─ useLogBuffer.ts     # Буферизация логов при паузе
├─ server/                   # Express API
│  ├─ index.js               # Точка входа, middleware
│  ├─ config.js              # Конфигурация путей и переменных
│  ├─ routes/
│  │  └─ jobs.js             # API роуты для jobs
│  ├─ services/
│  │  ├─ jobStore.js         # In-memory хранилище jobs
│  │  ├─ broadcaster.js      # SSE broadcasting + логирование
│  │  ├─ chunkState.js       # Управление состоянием чанков
│  │  └─ pipeline.js         # Запуск Python агентов
│  └─ utils/
│     └─ spawn.js            # Helper для spawn процессов
├─ public/                   # Статические файлы
├─ runtime/                  # Артефакты (создаётся автоматически)
├─ index.html                # Точка входа Vite
└─ package.json              # Скрипты и зависимости
```

## Логика работы
1) Пользователь выбирает аудио → `POST /api/jobs` (multipart). Файл сохраняется в `runtime/{jobId}/`.
2) Node API создаёт конфиги под задачу и запускает:
   - `agent01` (`python -m cli.main --config <generated>`).
   - После завершения — `agent03` (одиночный run-файл, который импортирует `TranscriptFixer` и применяет конфиг).
3) stdout/stderr обоих агентов стримятся по **SSE** (`/api/jobs/:id/stream`) и помечаются алиасами.
4) Итоговые пути (`transcript.md`, `transcript_fixed.md`, `response.json`) возвращаются в статусе и доступны как статические файлы из `/runtime`.

## API (локально)
- `POST /api/jobs` — создать задачу, form-data `file`.
- `GET /api/jobs/:id` — снепшот статуса/артефактов.
- `GET /api/jobs/:id/stream` — SSE события: `snapshot | log | status | chunk | done`.
- `POST /api/jobs/:id/chunks/:idx/cancel` — отменить чанк.
- `GET /health` — health check.
- Статика: `/runtime/**` (артефакты).

Переменные: `PORT` (по умолчанию 3001), `PYTHON_BIN` (по умолчанию `python`), `OPENAI_API_KEY` должен быть в окружении (делегируется агентам).

## Запуск (dev)
```bash
cd agent-browser
npm install
# окно 1: API
npm run dev:server        # PORT=3001 по умолчанию
# окно 2: фронт
npm run dev               # Vite на 5173; можно VITE_API_BASE=http://localhost:3001
```
Минимальные требования: Node 18+, установленный `python` и `ffmpeg/ffprobe` в PATH (для `agent01`). Папки `agent01/` и `agent03-trans-improver/` должны лежать рядом с `agent-browser/`.

## Production-сборка (UI)
```bash
npm run build   # создает dist/
```
Для прод-сервинга потребуется отдельный сервер для статики `dist/` и поднятый `server/index.js` (или объединить в один Node runtime).

## Архитектурные заметки
- **Модульная структура:** Frontend разбит на компоненты и хуки; Backend разбит на routes, services, utils.
- **SSE с reconnect:** Клиент автоматически переподключается с exponential backoff (до 5 попыток).
- **Разделение ответственности:** UI логика в хуках (`useJob`), API взаимодействие в `api.ts`, состояние в services.
- **Конфиги per job:** генерируются в `runtime/{jobId}`; кэш/артефакты не пересекаются.
- **Без БД:** in-memory стор для статуса и подписчиков; можно заменить на Redis/DB при необходимости.
- **I18n:** лёгкий контекст; расширяется добавлением ключей в `i18n.tsx`.

## Известные требования/ограничения
- Нужен `OPENAI_API_KEY` доступный Python-агентам.
- На Windows убедитесь, что `python`, `ffmpeg`, `ffprobe` доступны в PATH; иначе укажите `PYTHON_BIN`.
- CORS открыт (`*`) для локальной разработки; для продакшена сузьте origin.
