# 🎉 Changelog v2.1.0

**Дата выпуска:** 2025-11-02

## 🆕 Новые возможности

### 1. Автоматическая конвертация M4A → WAV

**Описание:** Автоматически конвертирует M4A файлы в формат WAV перед обработкой.

**Зачем это нужно:**
- WAV формат оптимизирован для речевых API
- Лучшее качество транскрипции
- Совместимость с различными транскрипционными моделями
- Стандартизация входных данных

**Параметры конфигурации:**
```json
{
  "convert_to_wav": true,           // Включить конвертацию (по умолчанию: false)
  "wav_output_dir": "converted_wav" // Директория для WAV файлов
}
```

**Технические детали:**
- Формат: 16-bit PCM WAV
- Частота: 16 kHz (оптимально для речи)
- Каналы: Mono (моно)
- Автоматический skip если файл уже в WAV
- Проверка существующих конвертированных файлов (не конвертирует повторно)

**Пример использования:**
```python
from agent03 import Config, TranscriptionPipeline

config = Config({
    "file": "audio.m4a",
    "convert_to_wav": True,
    "wav_output_dir": "converted_wav"
})

pipeline = TranscriptionPipeline(config)
md_path, json_path = pipeline.process_file("audio.m4a")
# Результат: converted_wav/audio.wav создан автоматически
```

**Интеграция в pipeline:**
- Выполняется на этапе 3 (после загрузки конфигурации, перед chunking)
- Прозрачна для остальной части pipeline
- Chunking работает с конвертированным файлом

---

### 2. Промежуточное сохранение результатов

**Описание:** Сохраняет результаты транскрипции каждого чанка в отдельный JSON файл сразу после обработки.

**Зачем это нужно:**
- **Защита от потери данных**: Если процесс прервется, уже обработанные чанки сохранены
- **Мониторинг прогресса**: Можно отслеживать результаты в реальном времени
- **Отладка**: Анализ результатов по отдельным чанкам
- **Восстановление**: Можно продолжить обработку с места остановки

**Параметры конфигурации:**
```json
{
  "save_intermediate_results": true,           // Включить (по умолчанию: true)
  "intermediate_results_dir": "intermediate_results"  // Директория
}
```

**Формат промежуточных файлов:**
```json
{
  "chunk_basename": "audio_part_001.wav",
  "chunk_index": 0,
  "offset": 0.0,
  "emit_guard": 0.0,
  "segments": [
    {
      "start": 0.0,
      "end": 2.5,
      "text": "Привет, как дела?",
      "speaker": "Speaker 1"
    }
  ],
  "raw_response": { /* полный ответ API */ }
}
```

**Имена файлов:**
- Паттерн: `{base_name}_chunk_{index:03d}_result.json`
- Пример: `audio_chunk_000_result.json`, `audio_chunk_001_result.json`

**Пример использования:**
```python
from agent03 import Config, TranscriptionPipeline

config = Config({
    "file": "audio.m4a",
    "save_intermediate_results": True,
    "intermediate_results_dir": "intermediate_results"
})

pipeline = TranscriptionPipeline(config)
md_path, json_path = pipeline.process_file("audio.m4a")

# Результаты сохраняются после каждого чанка:
# intermediate_results/audio_chunk_000_result.json
# intermediate_results/audio_chunk_001_result.json
# ...
```

**Интеграция в pipeline:**
- Выполняется на этапе 8 (после кеширования, перед парсингом сегментов)
- Не влияет на производительность (быстрая запись JSON)
- Работает параллельно с основным процессом

---

## 🔄 Обновленный поток данных

```
1. Входной файл (audio.m4a)
           ↓
2. Config загружает настройки
           ↓
3. 🆕 Конвертация m4a → WAV (если включено)
           ↓
4. AudioChunker разделяет на части
           ↓
5. CacheManager проверяет кеш
           ↓
6. APIClient транскрибирует
           ↓
7. CacheManager сохраняет в кеш
           ↓
8. 🆕 Промежуточное сохранение результатов (если включено)
           ↓
9. APIClient парсит сегменты
           ↓
10. OutputWriter форматирует
           ↓
11. Выходные файлы
```

---

## 📊 Обновленные компоненты

### AudioUtils (infrastructure/audio/utils.py)
- **Новый метод:** `convert_to_wav(ffmpeg_path, input_path, output_dir)`
  - Конвертирует аудио в WAV формат
  - Поддержка различных форматов входных файлов
  - Автоматическая проверка существующих файлов

### Config (core/config.py)
- **Новые параметры:**
  - `convert_to_wav` (bool, default: False)
  - `wav_output_dir` (str, default: "converted_wav")
  - `save_intermediate_results` (bool, default: True)
  - `intermediate_results_dir` (str, default: "intermediate_results")

### TranscriptionPipeline (services/pipeline.py)
- **Новые методы:**
  - `_convert_to_wav_if_needed(file_path)` - конвертация в WAV
  - `_save_intermediate_result(result, base_name, chunk_index)` - промежуточное сохранение
- **Обновлен метод:** `process_file()` - интегрированы новые этапы

---

## 🚀 Миграция с v2.0.0

### Для существующих пользователей:

**1. Обновите конфигурацию (опционально):**

```json
{
  // Существующие параметры остаются без изменений
  "file": "audio.m4a",
  "model": "gpt-4o-transcribe-diarize",
  
  // Новые параметры (добавить при необходимости)
  "convert_to_wav": false,  // По умолчанию выключено, чтобы не сломать существующие настройки
  "save_intermediate_results": true  // По умолчанию включено
}
```

**2. Обратная совместимость:**
- Все существующие конфигурации работают без изменений
- Новые возможности выключены по умолчанию (convert_to_wav)
- Промежуточное сохранение включено, но не влияет на результат

**3. Структура выходных файлов:**
```
До v2.1:
├── transcript.md
├── openai_response.json
├── cache/
└── chunks/

После v2.1 (с новыми возможностями):
├── transcript.md
├── openai_response.json
├── converted_wav/          # 🆕 Если convert_to_wav=true
├── intermediate_results/   # 🆕 Если save_intermediate_results=true
├── cache/
└── chunks/
```

---

## 📈 Преимущества

### Для разработчиков:
- ✅ Больше контроля над процессом обработки
- ✅ Легче отлаживать проблемы
- ✅ Можно анализировать промежуточные результаты
- ✅ Унифицированный формат входных данных (WAV)

### Для production:
- ✅ Защита от потери данных при сбоях
- ✅ Возможность восстановления после прерываний
- ✅ Мониторинг прогресса в реальном времени
- ✅ Улучшенное качество транскрипции (WAV)

---

## 🔗 Ссылки

- **README:** [README.md](../README.md)
- **Архитектура:** [ARCHITECTURE.md](ARCHITECTURE.md)
- **Быстрый старт:** [QUICK_START.md](QUICK_START.md)

---

## 👥 Авторы

Agent03 Team

## 📄 Лицензия

MIT License

---

**v2.1.0** - Enhanced with M4A→WAV Conversion & Intermediate Saves 🚀

