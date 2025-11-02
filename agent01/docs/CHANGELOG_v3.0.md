# 🎉 Changelog v3.0.0

**Дата выпуска:** 2025-11-02

## 🚀 MAJOR UPDATE: Speaker Diarization with pyannote.audio

Версия 3.0.0 представляет **революционное изменение** в архитектуре обработки: вместо разделения файла на чанки по размеру, теперь система использует **интеллектуальную диаризацию спикеров** с помощью pyannote.audio.

---

## 🆕 Новые возможности

### 1. Интеграция pyannote.audio для диаризации спикеров

**Описание:** Автоматическое определение и разделение речи по спикерам с помощью state-of-the-art модели pyannote.audio.

**Зачем это нужно:**
- 🎯 **Точная диаризация**: Определяет кто, когда и что говорил
- 🔍 **Интеллектуальное разделение**: Вместо тупого chunking по размеру - умное разделение по спикерам
- 🌍 **Мультиязычность**: Автоопределение языка в каждом сегменте
- 📊 **Лучшие результаты**: Контекст сохраняется внутри реплики каждого спикера

**Новые зависимости:**
```bash
pip install pyannote.audio pydub
```

**Конфигурация:**
```json
{
  "use_diarization": true,
  "huggingface_token": "env:HUGGINGFACE_TOKEN",
  "diarization_model": "pyannote/speaker-diarization-3.1",
  "diarization_segments_dir": "diarization_segments"
}
```

**Как получить HuggingFace токен:**
1. Зарегистрируйтесь на https://huggingface.co/
2. Перейдите в https://huggingface.co/settings/tokens
3. Создайте новый токен (Read access достаточно)
4. Добавьте в `.env` файл: `HUGGINGFACE_TOKEN=hf_...`

---

### 2. Новый алгоритм обработки

**Старый алгоритм (v2.x):**
```
1. Конвертация в WAV
2. Разделение на чанки по размеру (target_mb)
3. Транскрипция каждого чанка
4. Склейка результатов с учетом overlap
```

**Новый алгоритм (v3.0):**
```
1. Конвертация в WAV
2. 🆕 Диаризация (определение спикеров)
3. 🆕 Извлечение сегмента для каждого спикера
4. 🆕 Транскрипция через whisper-1 (verbose_json)
5. 🆕 Автоопределение языка
6. Формирование итогового результата
```

---

### 3. Использование whisper-1 вместо gpt-4o-transcribe

**Изменения в транскрипции:**
- Модель: `whisper-1` (более стабильная для коротких сегментов)
- Формат ответа: `verbose_json` (детальная информация)
- Язык: **не указывается** (автоопределение для каждого сегмента)

**Преимущества:**
- Поддержка мультиязычных диалогов
- Детальная информация о словах и таймингах
- Лучшая обработка коротких фрагментов

---

### 4. Новый формат выходных данных

**Формат JSON (основной результат):**
```json
[
  {
    "speaker": "SPEAKER_00",
    "start": 12.3,
    "end": 17.8,
    "text": "Hola, как дела?",
    "words": [
      {"word": "Hola", "start": 12.3, "end": 12.8},
      {"word": "как", "start": 13.0, "end": 13.3},
      {"word": "дела", "start": 13.4, "end": 13.9}
    ],
    "language": "es"
  },
  {
    "speaker": "SPEAKER_01",
    "start": 18.2,
    "end": 22.5,
    "text": "Отлично, спасибо!",
    "words": [...],
    "language": "ru"
  }
]
```

**Поля:**
- `speaker`: Идентификатор спикера (SPEAKER_00, SPEAKER_01, ...)
- `start`: Время начала реплики (секунды)
- `end`: Время окончания реплики (секунды)
- `text`: Текст реплики
- `words`: Массив слов с точными таймингами (из verbose_json)
- `language`: Определенный язык сегмента

**Формат Markdown (для удобства чтения):**
```markdown
# Transcription with Speaker Diarization

**[12.30s - 17.80s] SPEAKER_00:**
Hola, как дела?

**[18.20s - 22.50s] SPEAKER_01:**
Отлично, спасибо!
```

---

### 5. Новые модули

#### `infrastructure/audio/diarizer.py`
Новый модуль для работы с pyannote.audio:

**Класс `AudioDiarizer`:**
- `diarize(audio_path)` - запуск диаризации
- `extract_audio_segment(source, output, start, end)` - извлечение сегмента
- `save_segments_to_json(segments, path)` - сохранение результатов

**Класс `DiarizationSegment`:**
- Dataclass для хранения информации о сегменте
- Поля: `speaker`, `start`, `end`

---

## 🔄 Обновленный поток данных

```
v3.0 Pipeline (с диаризацией):

1. Входной файл (audio.m4a)
   ↓
2. Загрузка конфигурации
   ↓
3. Конвертация M4A → WAV (16kHz, mono, PCM)
   ↓
4. 🆕 ДIARIZАЦИЯ через pyannote.audio
   → Определение спикеров и их сегментов
   → Сохранение diarization.json
   ↓
5. 🆕 Извлечение аудио-сегментов
   → Для каждого сегмента создается отдельный .wav файл
   → Naming: {base}_seg_{idx:04d}_{speaker}.wav
   ↓
6. 🆕 Транскрипция через whisper-1
   → Модель: whisper-1
   → Формат: verbose_json
   → Язык: автоопределение
   → Получение текста + words + language
   ↓
7. Промежуточное сохранение (опционально)
   → {base}_seg_{idx:04d}_result.json
   ↓
8. Сортировка по start time
   ↓
9. Сохранение итоговых файлов
   → {base}_transcript.json (основной)
   → {base}_transcript.md (для чтения)
   → {base}_diarization.json (raw diarization)
```

---

## ⚙️ Конфигурация

### Минимальная конфигурация для v3.0:

```json
{
  "file": "audio.m4a",
  "openai_api_key": "env:OPENAI_API_KEY",
  
  "use_diarization": true,
  "huggingface_token": "env:HUGGINGFACE_TOKEN",
  
  "convert_to_wav": true,
  "wav_output_dir": "converted_wav",
  
  "save_intermediate_results": true,
  "intermediate_results_dir": "intermediate_results",
  
  "diarization_segments_dir": "diarization_segments"
}
```

### Полная конфигурация:

```json
{
  "file": "audio.m4a",
  "openai_api_key": "env:OPENAI_API_KEY",
  
  "use_diarization": true,
  "huggingface_token": "env:HUGGINGFACE_TOKEN",
  "diarization_model": "pyannote/speaker-diarization-3.1",
  "diarization_segments_dir": "diarization_segments",
  
  "convert_to_wav": true,
  "wav_output_dir": "converted_wav",
  
  "save_intermediate_results": true,
  "intermediate_results_dir": "intermediate_results",
  
  "ffmpeg_path": "ffmpeg",
  "ffprobe_path": "ffprobe",
  
  "cache_dir": "cache"
}
```

---

## 📋 Переменные окружения

Создайте файл `.env` в корне проекта:

```bash
# OpenAI API Key (обязательно)
OPENAI_API_KEY=sk-ваш-ключ-openai

# HuggingFace Token (обязательно для v3.0+)
HUGGINGFACE_TOKEN=hf_ваш-токен-huggingface
```

---

## 🚀 Миграция с v2.x

### Для существующих пользователей:

**1. Обновите зависимости:**
```bash
pip install --upgrade pyannote.audio pydub
```

**2. Получите HuggingFace токен:**
- Зарегистрируйтесь на https://huggingface.co/
- Создайте токен: https://huggingface.co/settings/tokens
- Добавьте в `.env`: `HUGGINGFACE_TOKEN=hf_...`

**3. Обновите конфигурацию:**
```json
{
  // Добавьте эти параметры
  "use_diarization": true,
  "huggingface_token": "env:HUGGINGFACE_TOKEN"
}
```

**4. Обратная совместимость:**
- Старый режим (chunking) все еще работает: `"use_diarization": false`
- Новый режим (diarization): `"use_diarization": true`
- По умолчанию: `false` (для обратной совместимости)

---

## 📊 Сравнение режимов

| Параметр | v2.x (Chunking) | v3.0 (Diarization) |
|----------|-----------------|-------------------|
| **Разделение** | По размеру файла | По спикерам |
| **Модель транскрипции** | gpt-4o-transcribe-diarize | whisper-1 |
| **Формат ответа** | json/verbose_json | verbose_json |
| **Определение языка** | Один для всего файла | Для каждого сегмента |
| **Overlap** | Фиксированный (2с) | Нет (точные границы) |
| **Точность спикеров** | Средняя | Высокая |
| **Скорость** | Быстрее | Медленнее |
| **Подходит для** | Монологи, подкасты | Диалоги, интервью |

---

## 💡 Примеры использования

### Python API (v3.0):

```python
from agent01 import Config, TranscriptionPipeline

config = Config({
    "file": "interview.m4a",
    "use_diarization": True,
    "huggingface_token": "hf_...",
    "convert_to_wav": True
})

pipeline = TranscriptionPipeline(config)
md_path, json_path = pipeline.process_file("interview.m4a")

# Результат:
# - interview_transcript.json (основной)
# - interview_transcript.md (для чтения)
# - interview_diarization.json (raw)
# - diarization_segments/ (audio segments)
```

### CLI:

```bash
# Обновите config/default.json
{
  "use_diarization": true,
  "huggingface_token": "env:HUGGINGFACE_TOKEN"
}

# Запустите
agent01 --config config/default.json
```

---

## ⚠️ Breaking Changes

1. **Новый формат выходных данных** при `use_diarization: true`
   - JSON теперь массив объектов, а не combined format
   - Добавлены поля: `words`, `language`

2. **Новые обязательные зависимости** для режима diarization:
   - `pyannote.audio>=3.0.0`
   - `pydub>=0.25.0`

3. **Требуется HuggingFace токен** для режима diarization

4. **Изменилась логика обработки**:
   - Без overlap (точные границы сегментов)
   - Множество маленьких сегментов вместо больших чанков

---

## 🎯 Преимущества v3.0

### Для пользователей:
- ✅ Точная идентификация спикеров
- ✅ Поддержка мультиязычных диалогов
- ✅ Детальные тайминги слов
- ✅ Лучшее качество для интервью и диалогов

### Для разработчиков:
- ✅ Модульная архитектура сохранена
- ✅ Обратная совместимость с v2.x
- ✅ Легко расширять (новые дiarizация models)
- ✅ Промежуточные результаты для отладки

### Для production:
- ✅ State-of-the-art дiarizация (pyannote.audio)
- ✅ Стабильная транскрипция (whisper-1)
- ✅ Защита от потери данных
- ✅ Кеширование результатов

---

## 🔗 Полезные ссылки

- **pyannote.audio**: https://github.com/pyannote/pyannote-audio
- **HuggingFace**: https://huggingface.co/
- **OpenAI Whisper**: https://platform.openai.com/docs/guides/speech-to-text
- **README**: [README.md](../README.md)
- **Архитектура**: [ARCHITECTURE.md](ARCHITECTURE.md)

---

## 👥 Авторы

Agent01 Team

## 📄 Лицензия

MIT License

---

**v3.0.0** - Revolutionary Speaker Diarization with pyannote.audio 🎯🚀

