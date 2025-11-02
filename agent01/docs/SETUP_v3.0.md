# 🚀 Быстрая настройка Agent01 v3.0

Полное руководство по настройке и использованию новой версии с диаризацией спикеров.

---

## 📋 Требования

### Системные требования:
- Python 3.8+
- ffmpeg (для конвертации аудио)
- ffprobe (для анализа аудио)

### Python пакеты:
```bash
openai >= 1.0.0
python-dotenv >= 0.19.0
pyannote.audio >= 3.0.0    # НОВОЕ в v3.0
pydub >= 0.25.0            # НОВОЕ в v3.0
```

---

## 🔧 Установка

### Шаг 1: Клонирование и установка зависимостей

```bash
cd agent01
pip install -e .
```

Или установите зависимости вручную:
```bash
pip install -r requirements.txt
```

---

## 🔑 Настройка API ключей

Для работы v3.0 нужны **ДВА токена**:

### 1. OpenAI API Key

**Где получить:**
https://platform.openai.com/api-keys

**Как настроить:**
1. Создайте файл `.env` в корне проекта:
```bash
touch .env
```

2. Добавьте ключ:
```bash
OPENAI_API_KEY=sk-ваш-ключ-здесь
```

### 2. HuggingFace Token (НОВОЕ в v3.0)

**Где получить:**
1. Зарегистрируйтесь на https://huggingface.co/
2. Перейдите в https://huggingface.co/settings/tokens
3. Нажмите "New token"
4. Выберите "Read" access
5. Скопируйте созданный токен

**Как настроить:**
Добавьте в тот же `.env` файл:
```bash
HUGGINGFACE_TOKEN=hf_ваш-токен-здесь
```

**Итоговый `.env` файл:**
```bash
# OpenAI API Key
OPENAI_API_KEY=sk-proj-...

# HuggingFace Token (для pyannote.audio)
HUGGINGFACE_TOKEN=hf_...
```

> ⚠️ **Важно:** Файл `.env` уже в `.gitignore` - ваши токены не попадут в git!

---

## ⚙️ Конфигурация

### Создайте конфигурационный файл

Пример `config/my_config.json`:

```json
{
  "file": "interview.m4a",
  
  "openai_api_key": "env:OPENAI_API_KEY",
  "huggingface_token": "env:HUGGINGFACE_TOKEN",
  
  "use_diarization": true,
  "diarization_model": "pyannote/speaker-diarization-3.1",
  
  "convert_to_wav": true,
  "wav_output_dir": "converted_wav",
  
  "save_intermediate_results": true,
  "intermediate_results_dir": "intermediate_results",
  "diarization_segments_dir": "diarization_segments",
  
  "ffmpeg_path": "ffmpeg",
  "ffprobe_path": "ffprobe",
  "cache_dir": "cache"
}
```

### Параметры конфигурации

#### Обязательные для v3.0:
- `use_diarization: true` - включить режим диаризации
- `huggingface_token` - токен для pyannote.audio

#### Рекомендуемые:
- `convert_to_wav: true` - конвертация в WAV (оптимально для диаризации)
- `save_intermediate_results: true` - сохранение промежуточных результатов

#### Опциональные:
- `diarization_model` - модель диаризации (по умолчанию: pyannote/speaker-diarization-3.1)
- `diarization_segments_dir` - директория для аудио-сегментов

---

## 🎯 Использование

### Вариант 1: CLI

```bash
agent01 --config config/my_config.json
```

### Вариант 2: Python API

```python
from agent01 import Config, TranscriptionPipeline

# Загрузка конфигурации
config = Config.from_file("config/my_config.json")

# Создание pipeline
pipeline = TranscriptionPipeline(config)

# Обработка файла
md_path, json_path = pipeline.process_file("interview.m4a")

print(f"✅ Готово!")
print(f"  JSON: {json_path}")
print(f"  Markdown: {md_path}")
```

### Вариант 3: Программная конфигурация

```python
from agent01 import Config, TranscriptionPipeline

config = Config({
    "file": "interview.m4a",
    "use_diarization": True,
    "convert_to_wav": True
})

pipeline = TranscriptionPipeline(config)
md_path, json_path = pipeline.process_file("interview.m4a")
```

---

## 📂 Структура выходных файлов

После обработки вы получите:

```
project/
├── interview_transcript.json        # ⭐ Основной результат
├── interview_transcript.md          # 📄 Для чтения
├── interview_diarization.json       # 🎯 Raw диаризация
├── converted_wav/
│   └── interview.wav                # 🔊 Конвертированный WAV
├── diarization_segments/
│   ├── interview_seg_0000_SPEAKER_00.wav
│   ├── interview_seg_0001_SPEAKER_01.wav
│   └── ...                          # 🗂️ Извлеченные сегменты
├── intermediate_results/
│   ├── interview_seg_0000_result.json
│   ├── interview_seg_0001_result.json
│   └── ...                          # 💾 Промежуточные результаты
└── cache/
    └── interview.manifest.json      # 📦 Кеш
```

---

## 📊 Формат результатов

### Основной JSON (`interview_transcript.json`):

```json
[
  {
    "speaker": "SPEAKER_00",
    "start": 0.5,
    "end": 5.2,
    "text": "Hello, how are you today?",
    "words": [
      {"word": "Hello", "start": 0.5, "end": 1.0},
      {"word": "how", "start": 1.2, "end": 1.4},
      ...
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

### Markdown (`interview_transcript.md`):

```markdown
# Transcription with Speaker Diarization

**[0.50s - 5.20s] SPEAKER_00:**
Hello, how are you today?

**[5.80s - 9.30s] SPEAKER_01:**
Отлично, спасибо!
```

---

## 🔍 Проверка установки

### Проверьте зависимости:

```python
# test_setup.py
import sys

def check_dependencies():
    errors = []
    
    try:
        import openai
        print("✅ openai installed")
    except ImportError:
        errors.append("openai")
    
    try:
        import dotenv
        print("✅ python-dotenv installed")
    except ImportError:
        errors.append("python-dotenv")
    
    try:
        import pyannote.audio
        print("✅ pyannote.audio installed")
    except ImportError:
        errors.append("pyannote.audio")
    
    try:
        import pydub
        print("✅ pydub installed")
    except ImportError:
        errors.append("pydub")
    
    if errors:
        print(f"\n❌ Missing: {', '.join(errors)}")
        print(f"Install: pip install {' '.join(errors)}")
        sys.exit(1)
    else:
        print("\n✅ All dependencies installed!")

if __name__ == "__main__":
    check_dependencies()
```

Запустите:
```bash
python test_setup.py
```

### Проверьте токены:

```python
# test_tokens.py
import os
from dotenv import load_dotenv

load_dotenv()

openai_key = os.getenv("OPENAI_API_KEY")
hf_token = os.getenv("HUGGINGFACE_TOKEN")

if openai_key:
    print(f"✅ OPENAI_API_KEY: {openai_key[:15]}...")
else:
    print("❌ OPENAI_API_KEY not found")

if hf_token:
    print(f"✅ HUGGINGFACE_TOKEN: {hf_token[:15]}...")
else:
    print("❌ HUGGINGFACE_TOKEN not found")
```

Запустите:
```bash
python test_tokens.py
```

---

## 🐛 Troubleshooting

### Проблема: "HUGGINGFACE_TOKEN not found"

**Решение:**
1. Проверьте файл `.env`:
   ```bash
   cat .env
   ```
2. Убедитесь что токен указан правильно:
   ```bash
   HUGGINGFACE_TOKEN=hf_...
   ```
3. Перезапустите скрипт

### Проблема: "pyannote.audio not installed"

**Решение:**
```bash
pip install --upgrade pyannote.audio
```

### Проблема: "ffmpeg not found"

**Решение:**

**Ubuntu/Debian:**
```bash
sudo apt install ffmpeg
```

**macOS:**
```bash
brew install ffmpeg
```

**Windows:**
Скачайте с https://ffmpeg.org/download.html и добавьте в PATH

### Проблема: Медленная обработка

**Причины и решения:**
1. **Много сегментов** - это нормально для диалогов с частой сменой спикеров
2. **Медленная диаризация** - первый запуск загружает модель (~500MB)
3. **API rate limits** - whisper-1 может иметь лимиты

**Оптимизация:**
- Используйте кеширование (включено по умолчанию)
- Для монологов рассмотрите старый режим: `"use_diarization": false`

---

## 📚 Дальнейшее чтение

- [CHANGELOG v3.0](CHANGELOG_v3.0.md) - Полный список изменений
- [ARCHITECTURE.md](ARCHITECTURE.md) - Архитектура проекта
- [README.md](../README.md) - Общая документация

---

## 💬 Поддержка

Если возникли проблемы:
1. Проверьте [CHANGELOG v3.0](CHANGELOG_v3.0.md) - известные проблемы
2. Убедитесь что все зависимости установлены
3. Проверьте токены в `.env`
4. Попробуйте старый режим: `"use_diarization": false`

---

**v3.0.0** - Готов к использованию! 🎯🚀

