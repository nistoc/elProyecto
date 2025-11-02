# ⚡ Быстрый старт

> **Agent01 v3.0** использует интеллектуальную диаризацию спикеров для точной транскрипции диалогов и интервью.

---

## 1️⃣ Установка

```bash
cd agent01
pip install -e .
```

**Настройте токены** в файле `.env`:

```bash
# Создайте .env файл
cat > .env << EOF
OPENAI_API_KEY=sk-ваш-ключ-openai
HUGGINGFACE_TOKEN=hf_ваш-токен-huggingface
EOF
```

**Где получить токены:**
- OpenAI: https://platform.openai.com/api-keys
- HuggingFace: https://huggingface.co/settings/tokens (Read access)

> ⚠️ **Важно:** После создания HuggingFace токена нужно получить доступ к **ТРЕМ** моделям:
> 1. https://huggingface.co/pyannote/speaker-diarization-3.1 → "Agree and access repository"
> 2. https://huggingface.co/pyannote/speaker-diarization-community-1 → "Agree and access repository"
> 3. https://huggingface.co/pyannote/segmentation-3.0 → "Agree and access repository"
> 4. Подождите несколько секунд для одобрения каждой
>
> 📖 **Подробная инструкция:** [HUGGINGFACE_SETUP.md](HUGGINGFACE_SETUP.md)

---

## 2️⃣ Первый запуск

### Python API

```python
from agent01 import Config, TranscriptionPipeline

config = Config({
    "file": "audio.m4a",
    "use_diarization": True,
    "convert_to_wav": True
})

pipeline = TranscriptionPipeline(config)
md_path, json_path = pipeline.process_file("audio.m4a")
print(f"Готово! {json_path}")
```

### CLI

**После установки:**
```bash
agent01 --config config/default.json
```

**Без установки (прямой запуск):**
```bash
# Из корня проекта agent01/
python -m cli.main --config config/default.json
```

> 💡 **Совет:** Прямой запуск полезен для разработки и тестирования без установки пакета.

---

## 3️⃣ Конфигурация

### Минимальная конфигурация

```json
{
  "file": "audio.m4a",
  "openai_api_key": "env:OPENAI_API_KEY",
  "huggingface_token": "env:HUGGINGFACE_TOKEN",
  "use_diarization": true,
  "convert_to_wav": true
}
```

### Полная конфигурация

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
  "cache_dir": "cache"
}
```

### Пакетная обработка (v3.1+)

Автоматическая обработка всех файлов из папки:

```json
{
  "input_dir": "taskstoparse",
  "openai_api_key": "env:OPENAI_API_KEY",
  "huggingface_token": "env:HUGGINGFACE_TOKEN",
  "use_diarization": true,
  "convert_to_wav": true,
  "languages": ["es", "ru"]
}
```

**Три способа указания файлов:**

1. **Папка** (v3.1+): `"input_dir": "taskstoparse"` - обрабатывает все файлы
2. **Список**: `"files": ["file1.m4a", "file2.mp3"]` - конкретные файлы
3. **Один файл**: `"file": "audio.m4a"` - один файл

**Поддерживаемые форматы:** `.m4a`, `.mp3`, `.wav`, `.flac`, `.ogg`, `.aac`, `.wma`, `.opus`

**Приоритет:** `input_dir` > `files` > `file`

**Мультиязычность (v3.1+):** Параметр `languages` позволяет указать список языков для транскрипции. Система транскрибирует с каждым языком плюс автоопределение (null), сохраняя все результаты в отдельных полях (`text-es`, `text-ru`, `text-null`). По умолчанию: `["es", "ru"]` → 3 API вызова на сегмент.

Каждый файл получает свою workspace в `processing_workspaces/{filename}/output/`

📖 **Подробнее:** [BATCH_PROCESSING.md](BATCH_PROCESSING.md)

---

## 4️⃣ Формат результатов

**Файлы:**
- `audio_transcript.json` - основной результат
- `audio_transcript.md` - для чтения
- `audio_diarization.json` - raw диаризация

**JSON формат:**
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
  },
  {
    "speaker": "SPEAKER_01",
    "start": 5.8,
    "end": 9.3,
    "text": "Отлично, спасибо!",
    "words": [...],
    "language": "ru"
  }
]
```

**Markdown формат:**
```markdown
**[0.50s - 5.20s] SPEAKER_00:**
Hello, how are you?

**[5.80s - 9.30s] SPEAKER_01:**
Отлично, спасибо!
```

---

## 5️⃣ Ключевые параметры

| Параметр | Описание | По умолчанию |
|----------|----------|--------------|
| `file` / `files` | Входной файл(ы) | - |
| `openai_api_key` | API ключ OpenAI | `env:OPENAI_API_KEY` |
| `huggingface_token` | Токен HuggingFace | `env:HUGGINGFACE_TOKEN` |
| `use_diarization` | Режим диаризации | `true` |
| `convert_to_wav` | Конвертация в WAV | `true` |
| `diarization_model` | Модель диаризации | `pyannote/speaker-diarization-3.1` |
| `save_intermediate_results` | Промежуточное сохранение | `true` |
| `cache_dir` | Папка кеша | `cache` |

---

## 6️⃣ Модульное использование

### Только диаризация

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

### Кеширование

```python
from agent01.infrastructure.cache import CacheManager

cache = CacheManager("cache")
fingerprint = cache.get_file_fingerprint("audio.m4a")
manifest = cache.load_manifest(cache.get_manifest_path("audio"))
cached = cache.get_cached_response(manifest, "chunk.m4a", fingerprint)
```

---

## 7️⃣ Troubleshooting

### ❌ "OPENAI_API_KEY not provided"

**Решение:** Создайте файл `.env`:
```bash
OPENAI_API_KEY=sk-ваш-ключ
```

### ❌ "HUGGINGFACE_TOKEN not found"

**Решение:** Добавьте в `.env`:
```bash
HUGGINGFACE_TOKEN=hf_ваш-токен
```

**Пошаговая инструкция:**
1. Создайте токен: https://huggingface.co/settings/tokens (Read access)
2. Получите доступ к **трем** моделям:
   - https://huggingface.co/pyannote/speaker-diarization-3.1 → "Agree and access repository"
   - https://huggingface.co/pyannote/speaker-diarization-community-1 → "Agree and access repository"
   - https://huggingface.co/pyannote/segmentation-3.0 → "Agree and access repository"
3. Добавьте токен в `.env` файл

### ❌ "403 Client Error" или "gated repo"

**Проблема:** Нет доступа к одной или нескольким моделям диаризации.

**Решение:** Получите доступ к **ТРЕМ** моделям:

**Модель 1: speaker-diarization-3.1**
1. Откройте: https://huggingface.co/pyannote/speaker-diarization-3.1
2. Войдите в аккаунт HuggingFace
3. Нажмите **"Agree and access repository"**
4. Подождите 5-10 секунд

**Модель 2: speaker-diarization-community-1**
1. Откройте: https://huggingface.co/pyannote/speaker-diarization-community-1
2. Нажмите **"Agree and access repository"**
3. Подождите 5-10 секунд

**Модель 3: segmentation-3.0**
1. Откройте: https://huggingface.co/pyannote/segmentation-3.0
2. Нажмите **"Agree and access repository"**
3. Подождите 5-10 секунд

Затем запустите скрипт снова.

> 💡 Это нужно сделать **один раз** для каждой модели. После одобрения ваш токен получит постоянный доступ ко всем трем.

### ❌ "pyannote.audio not installed"

**Решение:**
```bash
pip install pyannote.audio pydub torchaudio
```

### ⚠️ "torchcodec" или "AudioDecoder" warnings/errors

**Что это:** Warnings о `libtorchcodec` или ошибки `AudioDecoder is not defined`.

**Решение:** agent01 автоматически обходит эту проблему используя `torchaudio`.

Вы увидите сообщение:
```
[INFO] Loading audio with torchaudio (bypassing torchcodec)...
```

Это **нормально** и ожидаемо на Windows. Никаких дополнительных действий не требуется.

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

### ❌ Медленная обработка

**Причина:** Первый запуск загружает модель диаризации (~500MB).

**Решение:** Последующие запуски будут быстрее (модель кешируется).

### ❌ TypeError: Pipeline.from_pretrained() unexpected keyword

**Проблема:** Несовместимость версий pyannote.audio API.

**Решение:** Код уже поддерживает обе версии API. Убедитесь что используете последнюю версию:
```bash
pip install --upgrade pyannote.audio torchaudio
```

---

## 8️⃣ Best Practices

### ✅ Используйте .env для токенов

```bash
# Файл: agent01/.env
OPENAI_API_KEY=sk-ваш-ключ
HUGGINGFACE_TOKEN=hf_ваш-токен
```

Файл `.env` автоматически загружается и защищен `.gitignore`.

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

## 9️⃣ CLI - Детальное использование

### Способы запуска

**1. После установки пакета:**
```bash
# Установите пакет
pip install -e .

# Запустите команду
agent01 --config config/default.json
```

**2. Прямой запуск без установки:**
```bash
# Из корня проекта agent01/
python -m cli.main --config config/default.json
```

**3. Прямой запуск с python:**
```bash
# Если cli.main не работает, используйте полный путь
python cli/main.py --config config/default.json
```

### Параметры командной строки

```bash
# Основной параметр - путь к конфигу
agent01 --config config/my_config.json

# Или
python -m cli.main --config config/my_config.json
```

### Пример использования

```bash
# Создайте config/interview.json:
{
  "file": "interview.m4a",
  "use_diarization": true,
  "convert_to_wav": true,
  "save_intermediate_results": true
}

# Запустите
python -m cli.main --config config/interview.json
```

### Структура вывода CLI

```
Execution plan:
- Load config from: config/default.json
- Model: gpt-4o-transcribe-diarize
- Files: ['audio.m4a']
- Use diarization (v3.0+): True
...

[FILE] audio.m4a
[STAGE 3] Converting to WAV if needed...
[STAGE 4] Running speaker diarization...
[INFO] Diarization complete: 15 segments found
[STAGE 5-6] Processing 15 diarized segments...
[DONE] Processing complete!
  - JSON: audio_transcript.json
  - Markdown: audio_transcript.md
```

---

## 🔟 Альтернативный режим (без диаризации)

Для монологов и подкастов можно использовать упрощенный режим:

```json
{
  "file": "podcast.m4a",
  "use_diarization": false,
  "pre_split": true,
  "target_chunk_mb": 5
}
```

**Отличия:**
- ✅ Быстрее (нет диаризации)
- ✅ Не требует HuggingFace токен
- ❌ Меньше точность определения спикеров
- ❌ Один язык для всего файла

---

## 1️⃣1️⃣ Дополнительная документация

- [HUGGINGFACE_SETUP.md](HUGGINGFACE_SETUP.md) - настройка HuggingFace (пошагово)
- [CHANGELOG v3.0](CHANGELOG_v3.0.md) - что нового в v3.0
- [ARCHITECTURE.md](ARCHITECTURE.md) - архитектура проекта
- [README.md](../README.md) - полная документация

---

**Начните за 30 секунд! 🚀**

**v3.0.0** - Revolutionary Speaker Diarization

