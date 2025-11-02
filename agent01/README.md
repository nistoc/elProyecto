# 🎯 Agent01 - Modular Transcription System

**Clean Architecture** implementation for audio transcription with OpenAI API.

[![Python 3.8+](https://img.shields.io/badge/python-3.8+-blue.svg)](https://www.python.org/downloads/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Version: 3.1.0](https://img.shields.io/badge/version-3.1.0-green.svg)](docs/CHANGELOG_v3.1.md)

## 🆕 Что нового в v3.1

**Организованная структура workspace:**
- 📁 Каждый файл обрабатывается в своей папке
- 🗂️ Все промежуточные и финальные файлы в одном месте
- 🧹 Простая очистка - удалите папку файла
- 🔧 Python 3.13 совместимость (замена pydub на soundfile)

**v3.0 - Интеллектуальная диаризация спикеров:**
- 🎯 Точное определение кто, когда и что говорил
- 🌍 Автоопределение языка в каждом сегменте
- 📊 Детальные тайминги слов
- 🔄 Обратная совместимость с v2.x

**Два режима работы:**
- **v2.x (Chunking):** разделение по размеру → для монологов, подкастов
- **v3.0+ (Diarization):** разделение по спикерам → для диалогов, интервью

## 📁 Структура проекта

**Исходный код:**
```
agent01/                        # Корневая папка проекта
├── core/                       # Domain Layer (модели, конфиг)
├── services/                   # Application Layer (бизнес-логика)
├── infrastructure/             # Infrastructure Layer (внешние зависимости)
│   ├── audio/                  # ffmpeg/ffprobe + diarization
│   ├── cache/                  # Кеширование
│   └── io/                     # Ввод-вывод
├── cli/                        # Interface Layer (CLI)
├── tests/                      # Тесты
├── docs/                       # Документация
├── examples/                   # Примеры
└── config/                     # Конфигурация
```

**Рабочие файлы (v3.1+):**
```
processing_workspaces/          # Workspace для каждого файла
└── my_audio/                   # Папка для my_audio.m4a
    ├── converted_wav/          # WAV конвертация
    ├── segments/               # Сегменты диаризации
    ├── intermediate/           # Промежуточные результаты
    ├── cache/                  # Кеш API запросов
    └── output/                 # 📤 Финальные файлы
        ├── my_audio_transcript.json
        ├── my_audio_transcript.md
        └── my_audio_diarization.json
```

**Подробнее**: см. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) и [docs/CHANGELOG_v3.1.md](docs/CHANGELOG_v3.1.md)

## 🚀 Быстрый старт

### Установка

**Базовая (v2.x режим):**
```bash
pip install -e .
```

**С диаризацией (v3.0 режим):**
```bash
pip install -e .
# или:
pip install pyannote.audio pydub
```

### Настройка токенов

Создайте файл `.env`:

```bash
# Обязательно для всех версий
OPENAI_API_KEY=sk-ваш-ключ-openai

# Дополнительно для v3.0 (диаризация)
HUGGINGFACE_TOKEN=hf_ваш-токен-huggingface
```

**Где получить:**
- OpenAI: https://platform.openai.com/api-keys
- HuggingFace: https://huggingface.co/settings/tokens

> ⚠️ **Важно для HuggingFace:** После создания токена получите доступ к **ТРЕМ** моделям:
> - https://huggingface.co/pyannote/speaker-diarization-3.1 → "Agree and access repository"
> - https://huggingface.co/pyannote/speaker-diarization-community-1 → "Agree and access repository"
> - https://huggingface.co/pyannote/segmentation-3.0 → "Agree and access repository"

> ⚠️ Файл `.env` уже в `.gitignore`

### Первый запуск

**Режим v2.x (Chunking):**
```python
from agent01 import Config, TranscriptionPipeline

config = Config({
    "file": "audio.m4a",
    "use_diarization": False  # v2.x режим
})

pipeline = TranscriptionPipeline(config)
md_path, json_path = pipeline.process_file("audio.m4a")
```

**Режим v3.0 (Diarization) 🆕:**
```python
from agent01 import Config, TranscriptionPipeline

config = Config({
    "file": "audio.m4a",
    "use_diarization": True,   # v3.0 режим
    "convert_to_wav": True
})

pipeline = TranscriptionPipeline(config)
md_path, json_path = pipeline.process_file("audio.m4a")
```

### CLI использование

```bash
# Отредактируйте config/default.json
# Установите use_diarization: true/false

agent01 --config config/default.json
```

## 💡 Примеры использования

### Полный pipeline (v3.0)

```python
from agent01 import Config, TranscriptionPipeline

config = Config({
    "file": "interview.m4a",
    "use_diarization": True,        # Включить диаризацию
    "convert_to_wav": True,          # Конвертация в WAV
    "save_intermediate_results": True # Промежуточные результаты
})

pipeline = TranscriptionPipeline(config)
md_path, json_path = pipeline.process_file("interview.m4a")

# Результат (v3.0):
# - interview_transcript.json (основной)
# - interview_transcript.md (для чтения)
# - interview_diarization.json (raw)
```

### Использование отдельных модулей

```python
# Диаризация (v3.0)
from agent01.infrastructure.audio import AudioDiarizer

diarizer = AudioDiarizer(huggingface_token="hf_...")
segments = diarizer.diarize("audio.wav")

for seg in segments:
    print(f"{seg.speaker}: {seg.start:.2f}s - {seg.end:.2f}s")

# Chunking (v2.x)
from agent01.infrastructure.audio import AudioChunker

chunker = AudioChunker("ffmpeg", "ffprobe")
chunks = chunker.process_chunks_for_file(
    "audio.m4a",
    target_mb=5.0,
    overlap_sec=2.0
)

# Транскрипция
from agent01.services import OpenAITranscriptionClient

client = OpenAITranscriptionClient(api_key="sk-...")
response = client.transcribe("audio.m4a")
segments = client.parse_segments(response)

# Кеш
from agent01.infrastructure.cache import CacheManager

cache = CacheManager("cache")
cached = cache.get_cached_response(manifest, "chunk.m4a", fingerprint)
```

## 🎨 Ключевые возможности

### ✅ Clean Architecture
- Разделение на слои (Domain, Application, Infrastructure, Interface)
- Четкие зависимости между компонентами
- Легко тестировать и расширять

### ✅ Модульность
- Каждый компонент можно использовать отдельно
- Простая замена реализаций
- Независимые модули

### ✅ Production-ready
- Кеширование результатов (SHA256 fingerprinting)
- Retry логика с fallback моделями
- Incremental output (не теряем прогресс)
- Обработка ошибок

### ✅ Гибкость
- Конфигурация через JSON или программно
- CLI или Python API
- Расширяемая архитектура

### ✅ v3.0 - Диаризация спикеров 🆕
- **Интеллектуальная диаризация** через pyannote.audio
  - Точное определение спикеров
  - Разделение по спикерам вместо chunking
  - State-of-the-art точность
  
- **Мультиязычность**
  - Автоопределение языка в каждом сегменте
  - Поддержка смешанных языков
  - Whisper-1 для транскрипции
  
- **Детальные результаты**
  - Тайминги слов (verbose_json)
  - Информация о языке
  - Промежуточные результаты

### ✅ v2.1 - Конвертация и сохранение
- **Автоматическая конвертация M4A → WAV**
  - Формат: 16kHz, mono, 16-bit PCM
  - Оптимизировано для transcription API
  
- **Промежуточное сохранение результатов**
  - Защита от потери данных
  - Анализ результатов по частям

## 🧪 Тестирование

```bash
# Запустить все тесты
pytest

# С coverage
pytest --cov=agent01

# Конкретный тест
pytest tests/test_core.py
```

## 📚 Документация

- **[QUICK_START.md](docs/QUICK_START.md)** - установка и использование
- **[MULTILINGUAL_SETUP.md](docs/MULTILINGUAL_SETUP.md)** - настройка мультиязычного распознавания (русский + испанский и др.)
- **[CHANGELOG_v3.1.md](docs/CHANGELOG_v3.1.md)** - что нового в v3.1
- **[CHANGELOG_v3.0.md](docs/CHANGELOG_v3.0.md)** - что нового в v3.0
- **[ARCHITECTURE.md](docs/ARCHITECTURE.md)** - детальная архитектура

## 🔧 Конфигурация

### v2.x режим (Chunking)

```json
{
  "file": "audio.m4a",
  "openai_api_key": "env:OPENAI_API_KEY",
  
  "use_diarization": false,
  "pre_split": true,
  "target_chunk_mb": 5,
  "chunk_overlap_sec": 2.0,
  
  "convert_to_wav": true,
  "save_intermediate_results": true
}
```

### v3.0 режим (Diarization) 🆕

```json
{
  "file": "audio.m4a",
  "openai_api_key": "env:OPENAI_API_KEY",
  "huggingface_token": "env:HUGGINGFACE_TOKEN",
  
  "use_diarization": true,
  "convert_to_wav": true,
  "save_intermediate_results": true,
  
  "diarization_model": "pyannote/speaker-diarization-3.1",
  "diarization_segments_dir": "diarization_segments",
  
  "language": null,
  "prompt": "Здравствуйте, как дела? Buenos días, ¿cómo estás?"
}
```

**Мультиязычное аудио (русский + испанский):**
- `language: null` - автоопределение языка (рекомендуется)
- `prompt` - примеры фраз на обоих языках для лучшего распознавания

**Подробнее:** см. [MULTILINGUAL_SETUP.md](docs/MULTILINGUAL_SETUP.md)

## 📊 Сравнение режимов

| Параметр | v2.x (Chunking) | v3.0 (Diarization) |
|----------|-----------------|-------------------|
| **Разделение** | По размеру файла | По спикерам |
| **Модель транскрипции** | gpt-4o-transcribe | whisper-1 |
| **Диаризация** | Через API | pyannote.audio |
| **Точность спикеров** | Средняя | Высокая |
| **Мультиязычность** | Один язык | Авто для каждого |
| **Скорость** | Быстрее | Медленнее |
| **Токены** | OpenAI | OpenAI + HuggingFace |
| **Подходит для** | Монологи, подкасты | Диалоги, интервью |

## 🛠️ Требования

### Python пакеты

**Базовые (v2.x):**
```
openai >= 1.0.0
python-dotenv >= 0.19.0
```

**Дополнительно для v3.0:**
```
pyannote.audio >= 3.0.0
pydub >= 0.25.0
```

### Системные
- Python 3.8+
- ffmpeg (для конвертации и chunking)
- ffprobe (для анализа аудио)

## 📦 Форматы результатов

### v2.x (Chunking)

**Markdown:** `transcript.md`
```markdown
- 0.00 speaker_0: "Hello"
- 2.50 speaker_1: "Hi there"
```

**JSON:** `openai_response.json`

### v3.0 (Diarization) 🆕

**JSON:** `audio_transcript.json` (основной)
```json
[
  {
    "speaker": "SPEAKER_00",
    "start": 0.5,
    "end": 5.2,
    "text": "Hello, how are you?",
    "words": [
      {"word": "Hello", "start": 0.5, "end": 1.0}
    ],
    "language": "en"
  }
]
```

**Markdown:** `audio_transcript.md` (для чтения)
```markdown
**[0.50s - 5.20s] SPEAKER_00:**
Hello, how are you?
```

## 📄 Лицензия

MIT License - см. [LICENSE](LICENSE)

---

**v3.0.0** - Revolutionary Speaker Diarization 🎯🚀
