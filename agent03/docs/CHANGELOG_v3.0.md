# 🎉 Changelog v3.0.0

**Дата выпуска:** 2025-11-02

---

## 🚀 MAJOR UPDATE: Speaker Diarization

Версия 3.0.0 добавляет **интеллектуальную диаризацию спикеров** через pyannote.audio вместо разделения на чанки по размеру.

---

## 🆕 Новые возможности

### 1. Диаризация через pyannote.audio

**Преимущества:**
- 🎯 Точное определение спикеров
- 🔍 Разделение по спикерам вместо chunking по размеру
- 🌍 Автоопределение языка в каждом сегменте
- 📊 Детальные тайминги слов (verbose_json)

**Новые зависимости:**
```bash
pip install pyannote.audio pydub
```

**Конфигурация:**
```json
{
  "use_diarization": true,
  "huggingface_token": "env:HUGGINGFACE_TOKEN",
  "convert_to_wav": true
}
```

**Получение HuggingFace токена:**
1. https://huggingface.co/settings/tokens
2. Создайте токен (Read access)
3. Добавьте в `.env`: `HUGGINGFACE_TOKEN=hf_...`

---

### 2. Новый алгоритм обработки

**v2.x:** Разделение по размеру → Транскрипция → Склейка  
**v3.0:** Диаризация → Извлечение сегментов → Транскрипция whisper-1

---

### 3. Транскрипция через whisper-1

- Модель: `whisper-1` (вместо gpt-4o-transcribe)
- Формат: `verbose_json`
- Язык: автоопределение для каждого сегмента

---

### 4. Новый формат JSON

```json
[
  {
    "speaker": "SPEAKER_00",
    "start": 12.3,
    "end": 17.8,
    "text": "Hola, как дела?",
    "words": [...],
    "language": "es"
  }
]
```

**Поля:**
- `speaker` - идентификатор спикера
- `start`/`end` - время в секундах
- `text` - текст реплики
- `words` - массив слов с таймингами
- `language` - определенный язык

---

## 🔄 Новый поток данных

```
1. Аудио (m4a/wav)
2. Конвертация → WAV (16kHz, mono)
3. Диаризация → определение спикеров
4. Извлечение сегментов → отдельные wav файлы
5. Транскрипция → whisper-1 для каждого сегмента
6. Сортировка по времени
7. Сохранение → JSON + Markdown
```

---

## ⚙️ Конфигурация

### Минимальная для v3.0:
```json
{
  "file": "audio.m4a",
  "openai_api_key": "env:OPENAI_API_KEY",
  "huggingface_token": "env:HUGGINGFACE_TOKEN",
  "use_diarization": true,
  "convert_to_wav": true
}
```

---

## 🔄 Обратная совместимость

Старый режим (chunking) работает при `"use_diarization": false` (по умолчанию).

---

## 📊 Сравнение

| Параметр | v2.x | v3.0 |
|----------|------|------|
| Разделение | По размеру | По спикерам |
| Модель | gpt-4o-transcribe | whisper-1 |
| Диаризация | Через API | pyannote.audio |
| Точность | Средняя | Высокая |
| Мультиязычность | Один язык | Авто для каждого |
| Подходит для | Монологи | Диалоги, интервью |

---

## ⚠️ Breaking Changes

1. Новый формат JSON при `use_diarization: true`
2. Требуется HuggingFace токен
3. Новые зависимости: pyannote.audio, pydub

---

## 📚 Документация

- [QUICK_START.md](QUICK_START.md) - установка и использование
- [ARCHITECTURE.md](ARCHITECTURE.md) - архитектура проекта

---

**v3.0.0** - Revolutionary Speaker Diarization 🎯🚀
