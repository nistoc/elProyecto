# elProyecto

Этот репозиторий содержит проекты для транскрибации аудио с использованием OpenAI API и диаризации спикеров.

## 📁 Структура

```
elProyecto/
├── agent01/                # 🎯 Agent01 - Оригинальный проект
│   ├── core/               # Domain Layer (модели, конфиг)
│   ├── services/           # Application Layer (бизнес-логика)
│   ├── infrastructure/     # Infrastructure Layer (аудио, кеш, I/O)
│   ├── cli/                # Interface Layer (CLI)
│   ├── tests/              # Тесты
│   ├── docs/               # Документация
│   └── README.md           # Главная документация Agent01
│
├── agent03/                # 🎯 Agent03 - Независимая копия проекта
│   ├── core/               # Domain Layer (модели, конфиг)
│   ├── services/           # Application Layer (бизнес-логика)
│   ├── infrastructure/     # Infrastructure Layer (аудио, кеш, I/O)
│   ├── cli/                # Interface Layer (CLI)
│   ├── tests/              # Тесты
│   ├── docs/               # Документация
│   └── README.md           # Главная документация Agent03
│
└── drafts/                 # Оригинальный монолитный код
```

## 🚀 Проекты

### Agent01 - Оригинальный проект
**Модульная система транскрипции аудио с Clean Architecture**

- 🎤 **Диаризация спикеров** через pyannote.audio
- 🌍 **Мультиязычность** - поддержка русского и испанского
- 🔄 **Пакетная обработка** - автоматическая обработка папки с файлами
- 💾 **Кеширование** - SHA256 fingerprinting для экономии API запросов
- 📊 **Два режима работы**:
  - v2.x (Chunking) - для монологов и подкастов
  - v3.0+ (Diarization) - для диалогов и интервью

**Версия:** 3.1.0  
**Статус:** Production-ready ✅

👉 **[agent01/README.md](agent01/README.md)** - Полная документация

### Agent03 - Независимая копия
**Точная копия Agent01 для экспериментов и модификаций**

Полностью независимый проект с такой же функциональностью, что и Agent01:
- ✅ Все модули и функции идентичны
- ✅ Отдельный namespace (`agent03` вместо `agent01`)
- ✅ Независимая установка и запуск
- ✅ Собственная workspace для обработки файлов

**Версия:** 3.1.0  
**Статус:** Ready for modifications 🔧

👉 **[agent03/README.md](agent03/README.md)** - Полная документация

## 🎯 Ключевые возможности

### ✅ Clean Architecture
- Разделение на слои (Domain, Application, Infrastructure, Interface)
- Четкие зависимости между компонентами
- Легко тестировать и расширять

### ✅ Интеллектуальная диаризация (v3.0+)
- Точное определение спикеров
- Автоопределение языка в каждом сегменте
- Детальные тайминги слов

### ✅ Мультиязычная транскрипция (v3.1+)
- Параллельная транскрипция на нескольких языках
- По умолчанию: испанский + русский + автоопределение
- Все результаты сохраняются в отдельных полях

### ✅ Production-ready
- Retry логика с fallback моделями
- Incremental output (не теряем прогресс)
- Кеширование результатов
- Обработка ошибок

## 🛠️ Технологии

- **Python 3.8+**
- **OpenAI API** (Whisper-1, GPT-4o-transcribe)
- **pyannote.audio** (speaker diarization)
- **ffmpeg/ffprobe** (конвертация аудио)
- **Clean Architecture** принципы

## 📚 Быстрый старт

### Установка Agent01
```bash
cd agent01
pip install -e .
```

### Установка Agent03
```bash
cd agent03
pip install -e .
```

### Настройка токенов
Создайте файл `.env` в корне нужного агента:
```bash
OPENAI_API_KEY=sk-ваш-ключ-openai
HUGGINGFACE_TOKEN=hf_ваш-токен-huggingface
```

### Запуск
```bash
# Agent01
agent01 --config agent01/config/default.json

# Agent03
agent03 --config agent03/config/default.json
```

## 📖 Документация

- **[agent01/README.md](agent01/README.md)** - Документация Agent01
- **[agent01/docs/QUICK_START.md](agent01/docs/QUICK_START.md)** - Быстрый старт
- **[agent01/docs/ARCHITECTURE.md](agent01/docs/ARCHITECTURE.md)** - Архитектура
- **[agent01/docs/BATCH_PROCESSING.md](agent01/docs/BATCH_PROCESSING.md)** - Пакетная обработка
- **[agent01/docs/MULTILINGUAL_SETUP.md](agent01/docs/MULTILINGUAL_SETUP.md)** - Мультиязычность

*(Agent03 имеет идентичную документацию)*

## 📄 Лицензия

MIT License - см. [LICENSE](agent01/LICENSE)

---

**Версия проектов:** 3.1.0  
**Последнее обновление:** Ноябрь 2025
