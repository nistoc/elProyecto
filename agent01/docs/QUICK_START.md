# ⚡ Быстрый старт

## 🎯 Выбор режима работы

**Agent01 v3.0** поддерживает два режима:

| Режим | Описание | Подходит для |
|-------|----------|--------------|
| **v2.x (Chunking)** | Разделение по размеру файла | Монологи, подкасты |
| **v3.0 (Diarization)** | Диаризация спикеров | Диалоги, интервью |

---

## 1️⃣ Установка

### Базовая установка (v2.x режим)

```bash
cd agent01
pip install -e .

# Настройте API ключ OpenAI
touch .env
echo "OPENAI_API_KEY=sk-ваш-ключ" >> .env
```

### Установка с диаризацией (v3.0 режим)

```bash
cd agent01
pip install -e .

# Или установите зависимости вручную:
pip install pyannote.audio pydub

# Настройте оба токена в .env
cat > .env << EOF
OPENAI_API_KEY=sk-ваш-ключ-openai
HUGGINGFACE_TOKEN=hf_ваш-токен-huggingface
EOF
```

**Где получить токены:**
- OpenAI: https://platform.openai.com/api-keys
- HuggingFace: https://huggingface.co/settings/tokens (Read access)

---

## 2️⃣ Первый запуск

### Режим v2.x (Chunking)

```python
from agent01 import Config, TranscriptionPipeline

config = Config({
    "file": "audio.m4a",
    "use_diarization": False  # Режим v2.x
})

pipeline = TranscriptionPipeline(config)
md_path, json_path = pipeline.process_file("audio.m4a")
print(f"Готово! {md_path}")
```

### Режим v3.0 (Diarization) 🆕

```python
from agent01 import Config, TranscriptionPipeline

config = Config({
    "file": "audio.m4a",
    "use_diarization": True,  # Режим v3.0
    "convert_to_wav": True
})

pipeline = TranscriptionPipeline(config)
md_path, json_path = pipeline.process_file("audio.m4a")
print(f"Готово! {json_path}")  # Основной результат теперь в JSON
```

### CLI

```bash
# Отредактируйте config/default.json
# Установите use_diarization: true/false

agent01 --config config/default.json
```

---

## 3️⃣ Конфигурация

### Конфиг для v2.x (Chunking)

```json
{
  "file": "audio.m4a",
  "openai_api_key": "env:OPENAI_API_KEY",
  "use_diarization": false,
  "pre_split": true,
  "target_chunk_mb": 5,
  "chunk_overlap_sec": 2.0
}
```

### Конфиг для v3.0 (Diarization) 🆕

```json
{
  "file": "audio.m4a",
  "openai_api_key": "env:OPENAI_API_KEY",
  "huggingface_token": "env:HUGGINGFACE_TOKEN",
  "use_diarization": true,
  "convert_to_wav": true,
  "save_intermediate_results": true
}
```

---

## 4️⃣ Формат результатов

### v2.x (Chunking)

**Файлы:**
- `transcript.md` - транскрипция с таймингами
- `openai_response.json` - сырой ответ API

**Markdown:**
```markdown
- 0.00 speaker_0: "Hello"
- 2.50 speaker_1: "Hi there"
```

### v3.0 (Diarization) 🆕

**Файлы:**
- `audio_transcript.json` - основной результат
- `audio_transcript.md` - для чтения
- `audio_diarization.json` - raw диаризация

**JSON:**
```json
[
  {
    "speaker": "SPEAKER_00",
    "start": 0.5,
    "end": 5.2,
    "text": "Hello, how are you?",
    "words": [
      {"word": "Hello", "start": 0.5, "end": 1.0},
      {"word": "how", "start": 1.2, "end": 1.4}
    ],
    "language": "en"
  }
]
```

---

## 5️⃣ Ключевые параметры

### Общие параметры

| Параметр | Описание | По умолчанию |
|----------|----------|--------------|
| `file` / `files` | Входной файл(ы) | - |
| `openai_api_key` | API ключ OpenAI | `env:OPENAI_API_KEY` |
| `cache_dir` | Папка кеша | `cache` |

### Параметры v2.x (Chunking)

| Параметр | Описание | По умолчанию |
|----------|----------|--------------|
| `use_diarization` | Режим диаризации | `false` |
| `pre_split` | Разделять на чанки | `true` |
| `target_chunk_mb` | Размер чанка (MB) | 24.5 |
| `chunk_overlap_sec` | Перекрытие (сек) | 2.0 |

### Параметры v3.0 (Diarization) 🆕

| Параметр | Описание | По умолчанию |
|----------|----------|--------------|
| `use_diarization` | Режим диаризации | `true` |
| `huggingface_token` | Токен HuggingFace | `env:HUGGINGFACE_TOKEN` |
| `diarization_model` | Модель диаризации | `pyannote/speaker-diarization-3.1` |
| `convert_to_wav` | Конвертация в WAV | `true` |

---

## 6️⃣ Примеры использования

### Только chunking (без API)

```python
from agent01.infrastructure.audio import AudioChunker

chunker = AudioChunker("ffmpeg", "ffprobe")
chunks = chunker.process_chunks_for_file(
    "audio.m4a", 
    target_mb=5.0,
    overlap_sec=2.0
)

for chunk in chunks:
    print(f"Chunk: {chunk.path}, offset: {chunk.offset}s")
```

### Только диаризация 🆕

```python
from agent01.infrastructure.audio import AudioDiarizer

diarizer = AudioDiarizer(
    huggingface_token="hf_...",
    model_name="pyannote/speaker-diarization-3.1"
)

segments = diarizer.diarize("audio.wav")

for seg in segments:
    print(f"{seg.speaker}: {seg.start:.2f}s - {seg.end:.2f}s")
```

### Только транскрипция

```python
from agent01.services import OpenAITranscriptionClient

client = OpenAITranscriptionClient(api_key="sk-...")
response = client.transcribe("audio.m4a")
segments = client.parse_segments(response)

for seg in segments:
    print(f"{seg.speaker}: {seg.text}")
```

---

## 7️⃣ Troubleshooting

### ❌ "OPENAI_API_KEY not provided"

**Решение:** Создайте файл `.env`:
```bash
OPENAI_API_KEY=sk-ваш-ключ
```

### ❌ "HUGGINGFACE_TOKEN not found" (v3.0)

**Решение:** Добавьте в `.env`:
```bash
HUGGINGFACE_TOKEN=hf_ваш-токен
```

Получите токен: https://huggingface.co/settings/tokens

### ❌ "pyannote.audio not installed" (v3.0)

**Решение:**
```bash
pip install pyannote.audio pydub
```

### ❌ "ffmpeg/ffprobe not found"

**Решение:**
```bash
# Ubuntu/Debian
sudo apt install ffmpeg

# macOS
brew install ffmpeg

# Windows
# Скачайте с https://ffmpeg.org/download.html
```

### ❌ Медленная обработка (v3.0)

**Причина:** Первый запуск загружает модель диаризации (~500MB).

**Решение:** Последующие запуски будут быстрее. Или используйте v2.x режим для монологов.

---

## 8️⃣ Best Practices

### ✅ Используйте .env для токенов

```bash
# Файл: agent01/.env
OPENAI_API_KEY=sk-ваш-ключ
HUGGINGFACE_TOKEN=hf_ваш-токен
```

Файл `.env` автоматически загружается и защищен `.gitignore`.

### ✅ Выбирайте правильный режим

- **v2.x (Chunking):** для монологов, подкастов, длинных аудио
- **v3.0 (Diarization):** для диалогов, интервью, встреч

### ✅ Используйте кеширование

```python
config = Config({
    "cache_dir": "cache"  # Включено по умолчанию
})
```

### ✅ Сохраняйте промежуточные результаты

```python
config = Config({
    "save_intermediate_results": True  # Защита от потери данных
})
```

---

## 9️⃣ Сравнение режимов

| Характеристика | v2.x | v3.0 |
|----------------|------|------|
| **Разделение** | По размеру | По спикерам |
| **Модель** | gpt-4o-transcribe | whisper-1 |
| **Точность спикеров** | Средняя | Высокая |
| **Мультиязычность** | Один язык | Авто для каждого |
| **Скорость** | Быстрее | Медленнее |
| **Токены** | OpenAI | OpenAI + HuggingFace |

---

## 🔟 Дополнительная документация

- [CHANGELOG v3.0](CHANGELOG_v3.0.md) - что нового в v3.0
- [ARCHITECTURE.md](ARCHITECTURE.md) - архитектура проекта
- [Примеры](../examples/basic_examples.py) - готовые примеры кода

---

**Начните за 30 секунд! 🚀**

**v3.0.0** - Revolutionary Speaker Diarization
