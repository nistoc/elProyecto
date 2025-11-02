# 🚀 Upgrade Guide: v2.x → v3.0

Краткое руководство по переходу на Agent01 v3.0 с поддержкой диаризации спикеров.

---

## 🎯 Что нового в v3.0?

**Главное изменение:** Вместо разделения на чанки по размеру, теперь используется **интеллектуальная диаризация спикеров** через pyannote.audio.

### До (v2.x):
```
Аудио → Chunking по размеру → Транскрипция → Склейка
```

### После (v3.0):
```
Аудио → Диаризация спикеров → Извлечение сегментов → Транскрипция whisper-1 → Результат
```

---

## 📦 Установка

### 1. Обновите зависимости

```bash
pip install --upgrade pyannote.audio pydub
```

Или:
```bash
pip install -r requirements.txt
```

### 2. Получите HuggingFace токен

**Новое требование для v3.0!**

1. Регистрация: https://huggingface.co/
2. Создайте токен: https://huggingface.co/settings/tokens (Read access)
3. Добавьте в `.env`:
   ```bash
   HUGGINGFACE_TOKEN=hf_ваш_токен
   ```

---

## ⚙️ Обновление конфигурации

### Минимальные изменения

Добавьте в ваш `config.json`:

```json
{
  "use_diarization": true,
  "huggingface_token": "env:HUGGINGFACE_TOKEN"
}
```

### Полная конфигурация v3.0

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

## 🔄 Обратная совместимость

**Старый режим (chunking) все еще работает!**

Чтобы использовать старый режим:
```json
{
  "use_diarization": false,
  "pre_split": true
}
```

По умолчанию `use_diarization: false` для обратной совместимости.

---

## 📊 Новый формат выходных данных

### При `use_diarization: true`:

**Файл:** `{base}_transcript.json`

```json
[
  {
    "speaker": "SPEAKER_00",
    "start": 0.5,
    "end": 5.2,
    "text": "Hello, how are you?",
    "words": [...],
    "language": "en"
  }
]
```

### При `use_diarization: false`:

**Старый формат** (как в v2.x):
- `transcript.md`
- `openai_response.json`

---

## 🎯 Быстрый старт

### Вариант 1: Python API

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

### Вариант 2: CLI

```bash
# Обновите config/default.json
agent01 --config config/default.json
```

---

## 📋 Checklist миграции

- [ ] Установлены: `pyannote.audio`, `pydub`
- [ ] Получен HuggingFace токен
- [ ] Токен добавлен в `.env`: `HUGGINGFACE_TOKEN=hf_...`
- [ ] Обновлена конфигурация: `"use_diarization": true`
- [ ] Протестирован на тестовом файле

---

## ⚠️ Breaking Changes

1. **Новый формат JSON** при `use_diarization: true`
2. **Требуется HuggingFace токен**
3. **Новые зависимости:** pyannote.audio, pydub

**Решение:** Используйте `"use_diarization": false` для старого поведения.

---

## 📚 Документация

- [CHANGELOG v3.0](docs/CHANGELOG_v3.0.md) - Полный список изменений
- [SETUP v3.0](docs/SETUP_v3.0.md) - Детальная настройка
- [ENV Template](docs/ENV_TEMPLATE.md) - Шаблон .env файла

---

## 🆘 Помощь

### Проблема: "HUGGINGFACE_TOKEN not found"
**Решение:** Проверьте `.env` файл, убедитесь что токен указан правильно.

### Проблема: "pyannote.audio not installed"
**Решение:** `pip install pyannote.audio`

### Проблема: Медленная обработка
**Решение:** Первый запуск загружает модель (~500MB). Последующие запуски быстрее.

---

## 🎉 Готово!

Теперь вы можете использовать v3.0 с интеллектуальной диаризацией спикеров!

```bash
agent01 --config config/default.json
```

---

**v3.0.0** - Revolutionary Speaker Diarization 🎯🚀

