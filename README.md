# elProyecto

Пайплайн транскрибации записей уроков испанского: аудио → транскрипт с диаризацией → рефайнинг (исправление испанских слов, записанных кириллицей) → веб-UI для просмотра и редактирования.

## 📁 Структура

```
elProyecto/
├── agent04/                  # Транскрайбер (.NET 9, gRPC :5032) — OpenAI diarize, чанки, кэш
├── agent06-improver-dot-net/ # Рефайнер (.NET 9, gRPC :5033, REST :5034) — TranslationImprover
├── agent05-ui-control/       # Оркестратор + UI (XtractManager): API .NET :5010, React UI :5173
├── lesson-package/           # Пакет урока: требования, контракт данных, prompt каждого прохода сборщика
└── voice-speakers/           # Кто говорит когда — по голосу, контейнер на локальном GPU (без OpenAI)
```

Схема взаимодействия:

```
UI (React, :5173) → agent05 API (REST+SSE, :5010)
                        ├─ gRPC → agent04 (:5032)  транскрипция
                        └─ gRPC → agent06 (:5033)  рефайнинг
```

## 📚 Документация

- **[agent04/README.md](agent04/README.md)** — сервис транскрипции
- **[agent05-ui-control/README.md](agent05-ui-control/README.md)** — API + UI, контракт workspace
- **[agent06-improver-dot-net/README.md](agent06-improver-dot-net/README.md)** — рефайнер
- **[lesson-package/CLAUDE.md](lesson-package/CLAUDE.md)** — пакет урока (темы, грамматика, примеры, слова): что это, правила, файлы и API, как собираются списки
- **[voice-speakers/README.md](voice-speakers/README.md)** — говорящие общей записи по голосу: как работает, как запустить, замеры

## 🗄️ История

Первое поколение (Python `agent01` — транскрайбер, `agent03-trans-improver` — рефайнер, Node/React `agent-browser` — UI, `drafts` — исходный монолит) удалено из рабочего дерева; при необходимости доступно в git-истории (до коммита с удалением legacy-агентов).
