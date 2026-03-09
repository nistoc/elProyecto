# ⚡ Быстрый старт

## 1️⃣ Установка (30 секунд)

```bash
# Перейти в папку проекта
cd agent01

# Установить зависимости
pip install -e .

# Настроить API ключ через .env файл (рекомендуется)
cp .env.example .env
# Откройте .env и замените your-api-key-here на ваш настоящий ключ

# Альтернативно: использовать переменную окружения
export OPENAI_API_KEY="your-key-here"  # Linux/Mac
set OPENAI_API_KEY=your-key-here       # Windows
```

> 💡 **Рекомендация:** Используйте `.env` файл - он автоматически загружается и уже добавлен в `.gitignore`

## 2️⃣ Первый запуск (1 минута)

### Вариант A: Python

```python
from agent01 import Config, TranscriptionPipeline

# Минимальная конфигурация
# API ключ будет автоматически загружен из .env файла
config = Config({
    "file": "audio.m4a"
})

# Запустить
pipeline = TranscriptionPipeline(config)
md_path, json_path = pipeline.process_file("audio.m4a")

print(f"Готово! {md_path}")
```

### Вариант B: CLI

```bash
# Отредактировать config/default.json
# Указать ваш audio файл и API ключ

# Запустить
python -m agent01.cli.main --config config/default.json

# Или после установки
agent01 --config config/default.json
```

## 3️⃣ Примеры использования

### 🎯 Только chunking (без API)

```python
from agent01.infrastructure.audio import AudioChunker

chunker = AudioChunker("ffmpeg", "ffprobe")
chunks = chunker.process_chunks_for_file(
    "audio.m4a", 
    target_mb=5.0,
    workdir="chunks",
    naming_pattern="{base}_part_%03d.m4a",
    overlap_sec=2.0
)

for chunk in chunks:
    print(f"Chunk: {chunk.path}, offset: {chunk.offset}s")
```

### 🎯 Только транскрибация (без chunking)

```python
from agent01.services import OpenAITranscriptionClient

client = OpenAITranscriptionClient(api_key="sk-...")
response = client.transcribe("audio.m4a")
segments = client.parse_segments(response)

for seg in segments:
    print(f"{seg.speaker}: {seg.text}")
```

### 🎯 С кешированием

```python
from agent01.infrastructure.cache import CacheManager
from agent01.services import OpenAITranscriptionClient

cache = CacheManager("cache")
client = OpenAITranscriptionClient(api_key="sk-...")

# Проверить кеш
fp = cache.get_file_fingerprint("audio.m4a")
manifest = cache.load_manifest(cache.get_manifest_path("audio"))
cached = cache.get_cached_response(manifest, "audio.m4a", fp)

if not cached:
    # Транскрибировать
    response = client.transcribe("audio.m4a")
    # Сохранить в кеш
    cache.cache_response(manifest, manifest_path, "audio.m4a", fp, response)
else:
    response = cached
```

## 4️⃣ Настройка конфигурации

### Из файла

```json
{
  "file": "audio.m4a",
  "model": "gpt-4o-transcribe-diarize",
  "openai_api_key": "env:OPENAI_API_KEY",
  
  "pre_split": true,
  "target_chunk_mb": 5,
  "chunk_overlap_sec": 2.0,
  
  "md_output_path": "transcript.md",
  "cache_dir": "cache"
}
```

### Программно

```python
from agent01 import Config, TranscriptionPipeline

config = Config({
    "file": "audio.m4a",
    "model": "gpt-4o-transcribe-diarize",
    "openai_api_key": "sk-...",
    "pre_split": True,
    "target_chunk_mb": 5,
    "chunk_overlap_sec": 2.0,
    
    # Новые возможности (v2.1)
    "convert_to_wav": True,  # Конвертировать m4a → wav
    "save_intermediate_results": True  # Сохранять промежуточные результаты
})

pipeline = TranscriptionPipeline(config)
pipeline.process_file("audio.m4a")
```

## 5️⃣ Ключевые параметры

| Параметр | Описание | По умолчанию |
|----------|----------|--------------|
| `file` / `files` | Входной файл(ы) | - |
| `model` | Модель OpenAI | `gpt-4o-transcribe-diarize` |
| `convert_to_wav` | Конвертировать m4a → wav | `false` |
| `wav_output_dir` | Папка для wav файлов | `converted_wav` |
| `pre_split` | Разделять на чанки? | `true` |
| `target_chunk_mb` | Размер чанка (MB) | 24.5 |
| `chunk_overlap_sec` | Перекрытие (секунды) | 2.0 |
| `cache_dir` | Папка кеша | `cache` |
| `save_intermediate_results` | Сохранять промежуточные результаты | `true` |
| `intermediate_results_dir` | Папка для промежуточных результатов | `intermediate_results` |

## 6️⃣ Структура выходных файлов

```
transcript.md           # Markdown с метками времени
openai_response.json    # Сырой ответ API

converted_wav/          # Конвертированные WAV файлы (если convert_to_wav=true)
  └── audio.wav

cache/                  # Кеш результатов
  └── audio.manifest.json

chunks/                 # Чанки (если pre_split=true)
  ├── audio_part_001.wav
  ├── audio_part_002.wav
  └── ...

chunks_json/           # JSON для каждого чанка (опционально)
  ├── audio_part_001.json
  └── ...

intermediate_results/  # Промежуточные результаты (если save_intermediate_results=true)
  ├── audio_chunk_000_result.json
  ├── audio_chunk_001_result.json
  └── ...
```

## 7️⃣ Интеграция с внешним агентом

### Вариант 1: Как библиотека

```python
from agent01 import Config, TranscriptionPipeline

def my_agent_function(audio_file):
    config = Config({
        "file": audio_file,
        "openai_api_key": "sk-..."
    })
    
    pipeline = TranscriptionPipeline(config)
    md_path, json_path = pipeline.process_file(audio_file)
    
    # Ваша дополнительная обработка
    with open(md_path) as f:
        transcript = f.read()
    
    return transcript
```

### Вариант 2: Модульно

```python
from agent01.infrastructure.audio import AudioChunker
from agent01.services import OpenAITranscriptionClient
from agent01.infrastructure.io import OutputWriter

class MyAgent:
    def __init__(self):
        self.chunker = AudioChunker("ffmpeg", "ffprobe")
        # API ключ загружается автоматически из .env
        self.transcriber = OpenAITranscriptionClient()
        self.writer = OutputWriter()
    
    def process(self, audio_file):
        # Шаг 1: Разделить
        chunks = self.chunker.process_chunks_for_file(audio_file, target_mb=5.0, ...)
        
        # Шаг 2: Транскрибировать каждый
        for chunk in chunks:
            response = self.transcriber.transcribe(chunk.path)
            segments = self.transcriber.parse_segments(response)
            
            # Шаг 3: Записать результаты
            self.writer.append_segments_to_markdown(
                "output.md", segments, chunk.offset, chunk.emit_guard
            )
```

## 8️⃣ Troubleshooting

### ❌ "OPENAI_API_KEY not provided"

**Причина:** API ключ не найден в переменных окружения.

**Решение:**

1. **Проверьте файл `.env`** (рекомендуется):
```bash
# Убедитесь что файл agent01/.env существует и содержит:
OPENAI_API_KEY=sk-ваш-настоящий-ключ
```

2. **Или установите переменную окружения системы**:
```bash
export OPENAI_API_KEY="sk-..."  # Linux/Mac
set OPENAI_API_KEY=sk-...       # Windows CMD
$env:OPENAI_API_KEY="sk-..."    # Windows PowerShell
```

3. **Или укажите в config.json**:
```json
{
  "openai_api_key": "sk-..."
}
```

> 🔑 Получите API ключ на: https://platform.openai.com/api-keys

### ❌ "ffmpeg/ffprobe not found"

```bash
# Установить ffmpeg
# Ubuntu/Debian
sudo apt install ffmpeg

# Mac
brew install ffmpeg

# Windows
# Скачать с https://ffmpeg.org/download.html
```

### ❌ "Missing 'openai' package"

```bash
pip install openai
```

### ❌ Chunk слишком большой

```json
{
  "target_chunk_mb": 5,    // Уменьшить размер
  "reencode_if_needed": true,
  "reencode_bitrate_kbps": 32  // Уменьшить bitrate
}
```

## 9️⃣ Best Practices

### ✅ Используйте кеширование

```python
# Кеш сохраняет результаты, избегает повторных API вызовов
config = Config({
    "cache_dir": "cache",  # Включено по умолчанию
    ...
})
```

### ✅ Настройте overlap для больших файлов

```python
# 2-3 секунды overlap обеспечивает continuity
config = Config({
    "chunk_overlap_sec": 2.0,  # Рекомендуется
    ...
})
```

### ✅ Сохраняйте per-chunk JSON для отладки

```python
config = Config({
    "save_per_chunk_json": true,
    "per_chunk_json_dir": "chunks_json",
    ...
})
```

### ✅ Используйте .env файл для секретов

```bash
# Файл: agent01/.env
OPENAI_API_KEY=sk-ваш-ключ

# Автоматически загружается при импорте agent01
# Уже добавлен в .gitignore
```

**Или используйте env: префикс в конфиге:**
```json
{
  "openai_api_key": "env:OPENAI_API_KEY"
}
```

## 🔟 Примеры

Смотрите готовые примеры в `examples/basic_examples.py`:

```bash
python examples/basic_examples.py
```

---

**Начните за 30 секунд! 🚀**

**v2.1.0** - Enhanced with M4A→WAV Conversion & Intermediate Saves
