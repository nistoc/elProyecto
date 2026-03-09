# 🎯 Agent01 - Modular Transcription System

**Clean Architecture** implementation for audio transcription with OpenAI API.

[![Python 3.8+](https://img.shields.io/badge/python-3.8+-blue.svg)](https://www.python.org/downloads/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## 📁 Структура проекта

```
agent01/                        # Корневая папка проекта
├── core/                       # Domain Layer (модели, конфиг)
├── services/                   # Application Layer (бизнес-логика)
├── infrastructure/             # Infrastructure Layer (внешние зависимости)
│   ├── audio/                  # ffmpeg/ffprobe
│   ├── cache/                  # Кеширование
│   └── io/                     # Ввод-вывод
├── cli/                        # Interface Layer (CLI)
├── tests/                      # Тесты
├── docs/                       # Документация
├── examples/                   # Примеры
└── config/                     # Конфигурация
```

**Подробнее**: см. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)

## 🚀 Быстрый старт

### Установка

```bash
pip install -e .
```

### Настройка API ключа

**Рекомендуемый способ:** использование `.env` файла

1. Скопируйте файл `.env.example` в `.env`:
```bash
cp .env.example .env
```

2. Откройте `.env` и замените `your-api-key-here` на ваш настоящий API ключ OpenAI:
```bash
OPENAI_API_KEY=sk-ваш-настоящий-ключ
```

3. Файл `.env` автоматически загружается при импорте модуля `agent01`

**Альтернативные способы:**
- Установить переменную окружения: `export OPENAI_API_KEY="sk-..."`
- Передать напрямую в конфиге: `{"openai_api_key": "sk-..."}`

> ⚠️ **Важно:** Файл `.env` уже добавлен в `.gitignore` и не будет закоммичен в git

### Первый запуск

```python
from agent01 import Config, TranscriptionPipeline

# API ключ автоматически загружается из .env файла
config = Config({
    "file": "audio.m4a"
})

# Запустить pipeline
pipeline = TranscriptionPipeline(config)
md_path, json_path = pipeline.process_file("audio.m4a")

print(f"Done! {md_path}")
```

### CLI использование

**После установки пакета:**

```bash
agent01 --config config/default.json
```

**Запуск БЕЗ установки пакета:**

**Способ 1: Через Python модуль (рекомендуется)**
```bash
cd agent01
pip install -r requirements.txt
python -m cli.main --config config/default.json
```

**Способ 2: Запуск главного скрипта напрямую**
```bash
cd agent01
pip install -r requirements.txt
python cli/main.py --config config/default.json
```

## 💡 Примеры использования

### Полный pipeline

```python
from agent01 import Config, TranscriptionPipeline

config = Config.from_file("config/default.json")
pipeline = TranscriptionPipeline(config)
md_path, json_path = pipeline.process_file("audio.m4a")
```

### Использование отдельных модулей

```python
# Только chunking
from agent01.infrastructure.audio import AudioChunker

chunker = AudioChunker("ffmpeg", "ffprobe")
chunks = chunker.process_chunks_for_file(
    "audio.m4a",
    target_mb=5.0,
    workdir="chunks",
    naming_pattern="{base}_part_%03d.m4a",
    overlap_sec=2.0
)

# Только транскрибация
from agent01.services import OpenAITranscriptionClient

client = OpenAITranscriptionClient(api_key="sk-...")
response = client.transcribe("audio.m4a")
segments = client.parse_segments(response)

# Только кеш
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

### ✅ Новые возможности (v2.1)
- **Автоматическая конвертация M4A → WAV**
  - Конвертирует m4a файлы в WAV перед обработкой
  - Формат: 16kHz, mono, 16-bit PCM
  - Оптимизировано для transcription API
  - Включается параметром `convert_to_wav: true`
  
- **Промежуточное сохранение результатов**
  - Сохраняет результат каждого чанка отдельно
  - Защита от потери данных при сбоях
  - Позволяет анализировать результаты по частям
  - JSON с полными данными сегментов + raw response
  - Включается параметром `save_intermediate_results: true`

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

- **[ARCHITECTURE.md](docs/ARCHITECTURE.md)** - Детальная архитектура
- **[QUICK_START.md](docs/QUICK_START.md)** - Быстрый старт за 30 секунд

## 🔧 Конфигурация

### Основные параметры

```json
{
  "file": "audio.m4a",
  "model": "gpt-4o-transcribe-diarize",
  "openai_api_key": "env:OPENAI_API_KEY",
  
  "max_duration_minutes": 1,
  "parallel_transcription_workers": 3,
  
  "convert_to_wav": true,
  "wav_output_dir": "converted_wav",
  
  "pre_split": true,
  "target_chunk_mb": 5,
  "chunk_overlap_sec": 2.0,
  
  "md_output_path": "transcript.md",
  "raw_json_output_path": "response.json",
  
  "cache_dir": "cache",
  "save_per_chunk_json": true,
  "save_intermediate_results": true,
  "intermediate_results_dir": "intermediate_results"
}
```

#### Ключевые параметры

- **`max_duration_minutes`** - количество минут аудио для обработки (0 = весь файл, по умолчанию 1)
- **`parallel_transcription_workers`** - количество параллельных API вызовов (по умолчанию 3)
- **`progress_time_format`** - формат отображения времени в прогрессе (по умолчанию "MMM:SSS.M")
- **`clean_before_run`** - очищать кэш и промежуточные папки перед запуском (по умолчанию true)

#### Формат прогресса

Прогресс отображается в виде компактной строки, где каждый чанк имеет свою ячейку:

```
[1:✓][2:✓][3:001:45.3][4:002:28.5][5:000:05.2][6:---:--.-][7:---:--.-]
```

- `[N:✓]` - чанк N завершен
- `[N:001:45.3]` - чанк N обрабатывается (формат: минуты:секунды.десятые)
- `[N:---:--.-]` - чанк N в ожидании

#### Очистка перед запуском

При `clean_before_run: true` (по умолчанию) автоматически очищается содержимое папок:
- `cache/` - кэш транскрибаций
- `chunks/` - разделенные аудио файлы
- `chunks_json/` - JSON результаты по чанкам
- `intermediate_results/` - промежуточные результаты
- `converted_wav/` - конвертированные WAV файлы

Сами папки остаются, удаляется только их содержимое. Установите `clean_before_run: false` для сохранения кэша между запусками.

### Переменные окружения и .env файл

**Способ 1: `.env` файл (рекомендуется)**

Проект автоматически загружает переменные из файла `.env`:

```bash
# Файл: agent01/.env
OPENAI_API_KEY=sk-ваш-настоящий-ключ
```

> 🔑 **Где взять API ключ?** https://platform.openai.com/api-keys

**Способ 2: Переменная окружения системы**

```bash
# Linux/Mac
export OPENAI_API_KEY="sk-..."

# Windows CMD
set OPENAI_API_KEY=sk-...

# Windows PowerShell
$env:OPENAI_API_KEY="sk-..."
```

**Способ 3: В конфигурационном файле**

```json
{
  "openai_api_key": "env:OPENAI_API_KEY"
}
```

> ⚠️ **Важно:** Файл `.env` уже добавлен в `.gitignore` - ваш API ключ не будет закоммичен в git.

## 🛠️ Требования

### Python пакеты
```
openai >= 1.0.0
python-dotenv >= 0.19.0
```

### Системные
- Python 3.8+
- ffmpeg (для chunking)
- ffprobe (для chunking)

## 📦 API Reference

### Config

```python
from agent01.core import Config

# Из файла
config = Config.from_file("config/default.json")

# Программно
config = Config({
    "file": "audio.m4a",
    "openai_api_key": "sk-...",
    "pre_split": True,
    "target_chunk_mb": 5
})
```

### TranscriptionPipeline

```python
from agent01.services import TranscriptionPipeline

pipeline = TranscriptionPipeline(config)

# Обработать один файл
md_path, json_path = pipeline.process_file("audio.m4a")

# Обработать все файлы из конфига
results = pipeline.process_all_files()
```

### AudioChunker

```python
from agent01.infrastructure.audio import AudioChunker

chunker = AudioChunker("ffmpeg", "ffprobe")
chunks = chunker.process_chunks_for_file(
    source_path="audio.m4a",
    target_mb=5.0,
    workdir="chunks",
    naming_pattern="{base}_part_%03d.m4a",
    overlap_sec=2.0
)
```

### OpenAITranscriptionClient

```python
from agent01.services import OpenAITranscriptionClient

client = OpenAITranscriptionClient(
    api_key="sk-...",
    model="gpt-4o-transcribe-diarize"
)

response = client.transcribe("audio.m4a")
segments = client.parse_segments(response)
```

### CacheManager

```python
from agent01.infrastructure.cache import CacheManager

cache = CacheManager("cache")
fingerprint = cache.get_file_fingerprint("audio.m4a")
manifest = cache.load_manifest(cache.get_manifest_path("audio"))
cached = cache.get_cached_response(manifest, "chunk.m4a", fingerprint)
```

### OutputWriter

```python
from agent01.infrastructure.io import OutputWriter

writer = OutputWriter()
writer.initialize_markdown("output.md")
writer.append_segments_to_markdown("output.md", segments, offset=0.0, emit_guard=0.0)
writer.finalize_markdown("output.md")
```

### Конвертация M4A → WAV

```python
from agent01.infrastructure.audio import AudioUtils

# Конвертировать m4a в wav
ffmpeg = "ffmpeg"  # или полный путь
wav_path = AudioUtils.convert_to_wav(
    ffmpeg_path=ffmpeg,
    input_path="audio.m4a",
    output_dir="converted_wav"  # опционально
)
print(f"Converted to: {wav_path}")
```

### Использование с конвертацией в pipeline

```python
from agent01 import Config, TranscriptionPipeline

config = Config({
    "file": "audio.m4a",
    "convert_to_wav": True,  # Включить конвертацию
    "wav_output_dir": "converted_wav",
    "save_intermediate_results": True,  # Промежуточное сохранение
    "intermediate_results_dir": "intermediate_results"
})

pipeline = TranscriptionPipeline(config)
md_path, json_path = pipeline.process_file("audio.m4a")

# Результаты:
# - converted_wav/audio.wav (конвертированный файл)
# - intermediate_results/audio_chunk_000_result.json (промежуточные результаты)
# - intermediate_results/audio_chunk_001_result.json
# - transcript.md (финальная транскрипция)
# - openai_response.json (полный ответ API)
```

## 📄 Лицензия

MIT License - см. [LICENSE](LICENSE)

---

**v2.1.0** - Enhanced with M4A→WAV Conversion & Intermediate Saves 🚀
