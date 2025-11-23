# Пакетная обработка аудиофайлов (Batch Processing)

## Обзор

Агент 01 теперь поддерживает автоматическую обработку всех аудиофайлов из указанной папки. Вместо указания одного конкретного файла в конфигурации, вы можете указать директорию, и все поддерживаемые аудиофайлы будут обработаны последовательно.

## Поддерживаемые форматы

Агент автоматически находит файлы со следующими расширениями:
- `.m4a`
- `.mp3`
- `.wav`
- `.flac`
- `.ogg`
- `.aac`
- `.wma`
- `.opus`

## Конфигурация

### Вариант 1: Обработка всех файлов из папки

В файле конфигурации (например, `config/default.json`) используйте параметр `input_dir`:

```json
{
  "input_dir": "taskstoparse",
  "model": "gpt-4o-transcribe-diarize",
  ...
}
```

Это будет сканировать папку `taskstoparse` и обрабатывать все найденные аудиофайлы.

### Вариант 2: Обработка одного файла (старый способ)

Для обработки одного файла используйте параметр `file`:

```json
{
  "file": "path/to/your/audio.m4a",
  "model": "gpt-4o-transcribe-diarize",
  ...
}
```

### Вариант 3: Обработка конкретного списка файлов

Для обработки конкретного списка файлов используйте параметр `files`:

```json
{
  "files": [
    "taskstoparse/file1.m4a",
    "taskstoparse/file2.m4a",
    "other_folder/file3.mp3"
  ],
  "model": "gpt-4o-transcribe-diarize",
  ...
}
```

## Приоритет параметров

Агент проверяет параметры в следующем порядке:

1. `input_dir` - если указан, сканирует директорию и игнорирует `files` и `file`
2. `files` - если `input_dir` не указан, использует список файлов
3. `file` - если не указаны `input_dir` и `files`, обрабатывает один файл

## Структура результатов

Для каждого обработанного файла создаётся отдельная рабочая область (workspace) в папке `processing_workspaces`:

```
processing_workspaces/
├── Voice_251022_191028-1-1-1/
│   ├── output/
│   │   ├── Voice_251022_191028-1-1-1_transcript.md
│   │   ├── Voice_251022_191028-1-1-1_transcript.json
│   │   └── Voice_251022_191028-1-1-1_diarization.json (если используется диаризация)
│   ├── converted_wav/
│   ├── segments/
│   ├── intermediate/
│   └── cache/
├── Voice_251022_191028-1-1-2/
│   └── ...
```

## Пример использования

### 1. Подготовьте файлы

Поместите все аудиофайлы, которые нужно обработать, в папку `taskstoparse`:

```
agent03/
├── taskstoparse/
│   ├── meeting_01.m4a
│   ├── meeting_02.m4a
│   └── interview.mp3
```

### 2. Настройте конфигурацию

Отредактируйте `config/default.json`:

```json
{
  "input_dir": "taskstoparse",
  "model": "gpt-4o-transcribe-diarize",
  "use_diarization": true,
  "convert_to_wav": true,
  ...
}
```

### 3. Запустите обработку

```bash
python -m cli.main --config config/default.json
```

### 4. Проверьте результаты

После завершения проверьте папки:

```
processing_workspaces/meeting_01/output/meeting_01_transcript.md
processing_workspaces/meeting_02/output/meeting_02_transcript.md
processing_workspaces/interview/output/interview_transcript.md
```

## Логи и прогресс

При запуске вы увидите:

```
Execution plan:
- Input directory: taskstoparse (3 file(s) found)
- Files to process: ['taskstoparse/meeting_01.m4a', 'taskstoparse/meeting_02.m4a', 'taskstoparse/interview.mp3']
...

[FILE] taskstoparse/meeting_01.m4a
[STAGE 1] Creating workspace...
[INFO] Created workspace for 'meeting_01': processing_workspaces/meeting_01
...

[FILE] taskstoparse/meeting_02.m4a
[STAGE 1] Creating workspace...
...
```

## Преимущества

1. **Автоматизация**: не нужно вручную запускать обработку для каждого файла
2. **Организация**: каждый файл получает свою рабочую область с результатами
3. **Кэширование**: каждый файл имеет свой кэш, можно переобрабатывать отдельные файлы
4. **Удобство**: просто положите файлы в папку и запустите

## Советы

- Используйте понятные имена файлов - они будут использованы для создания имён папок
- Проверяйте логи на предмет ошибок при обработке конкретных файлов
- При больших объёмах файлов убедитесь, что у вас достаточно места на диске
- Можно остановить обработку (Ctrl+C) и запустить снова - обработанные файлы будут использовать кэш

## Python API

### Обработка из директории

```python
from core import Config
from services import TranscriptionPipeline

# Конфигурация с директорией
config = Config({
    "input_dir": "taskstoparse",
    "openai_api_key": "env:OPENAI_API_KEY",
    "huggingface_token": "env:HUGGINGFACE_TOKEN",
    "use_diarization": True,
    "convert_to_wav": True,
    "save_intermediate_results": True,
    "workspace_root": "processing_workspaces"
})

# Создание pipeline
pipeline = TranscriptionPipeline(config)

# Обработка всех файлов
results = pipeline.process_all_files()

# Вывод результатов
print(f"\nProcessed {len(results)} files:")
for md_path, json_path in results:
    print(f"  ✓ {md_path}")
    print(f"  ✓ {json_path}")
```

### Обработка списка конкретных файлов

```python
from core import Config
from services import TranscriptionPipeline

# Список файлов для обработки
files_to_process = [
    "taskstoparse/meeting_01.m4a",
    "taskstoparse/meeting_02.m4a",
]

config = Config({
    "files": files_to_process,
    "openai_api_key": "env:OPENAI_API_KEY",
    "huggingface_token": "env:HUGGINGFACE_TOKEN",
    "use_diarization": True,
    "convert_to_wav": True
})

pipeline = TranscriptionPipeline(config)
results = pipeline.process_all_files()
```

### Проверка найденных файлов

```python
from core import Config

config = Config.from_file("config/default.json")

# Получить список файлов для обработки
files = config.get_files()

print(f"Found {len(files)} files:")
for i, file_path in enumerate(files, 1):
    print(f"  {i}. {file_path}")
```

## Технические детали

### Алгоритм сканирования

1. **Проверка параметра `input_dir`**: Если указан, переходим к сканированию
2. **Чтение директории**: `os.listdir(input_dir)` с сортировкой
3. **Фильтрация**: Проверка расширения файла (case-insensitive)
4. **Проверка типа**: Только файлы, не директории
5. **Возврат списка**: Полные пути к найденным файлам

### Поддерживаемые расширения

Расширения проверяются без учёта регистра:
- `.m4a`, `.M4A`, `.M4a` - все эквивалентны
- `.mp3`, `.MP3`, `.Mp3` - все эквивалентны

### Последовательная обработка

Файлы обрабатываются **последовательно** (не параллельно):
- Один файл за раз
- Каждый файл получает полный цикл обработки
- Переход к следующему файлу после завершения предыдущего

### Workspace для каждого файла

Каждый файл получает свою изолированную workspace:

```
processing_workspaces/
├── {filename}/
│   ├── converted_wav/          # Конвертированные файлы
│   ├── segments/                # Сегменты диаризации
│   ├── intermediate/            # Промежуточные результаты
│   ├── cache/                   # Кэш API запросов
│   └── output/                  # Финальные результаты
│       ├── {filename}_transcript.json
│       ├── {filename}_transcript.md
│       └── {filename}_diarization.json
```

### Обратная совместимость

Все старые конфигурации продолжают работать:

**v3.0 и ранее:**
```json
{
  "file": "audio.m4a"
}
```
✅ Работает без изменений

**v3.1+ (новое):**
```json
{
  "input_dir": "taskstoparse"
}
```
✅ Новая функция

## Примеры конфигурации

### Минимальная конфигурация для batch processing

```json
{
  "input_dir": "taskstoparse",
  "openai_api_key": "env:OPENAI_API_KEY",
  "use_diarization": true,
  "languages": ["es", "ru"]
}
```

**Multilingual Support:** The `languages` parameter specifies which languages to try when transcribing. For each audio segment, the system will:
1. Transcribe with each specified language (e.g., `es`, `ru`)
2. Transcribe with auto-detection (`language=null`)
3. Store all results in separate fields: `text-es`, `text-ru`, `text-null`
4. No selection - all transcriptions are preserved for analysis

**Note:** This makes **(N + 1) API calls per segment** where N is the number of languages. Default `["es", "ru"]` = **3 calls per segment**.

### Полная конфигурация для batch processing

```json
{
  "input_dir": "taskstoparse",
  
  "model": "gpt-4o-transcribe-diarize",
  "openai_api_key": "env:OPENAI_API_KEY",
  "huggingface_token": "env:HUGGINGFACE_TOKEN",
  
  "use_diarization": true,
  "diarization_model": "pyannote/speaker-diarization-3.1",
  
  "convert_to_wav": true,
  "save_intermediate_results": true,
  "save_per_chunk_json": true,
  
  "workspace_root": "processing_workspaces",
  "languages": ["es", "ru"],
  "prompt": null
}
```

**Note:** The `languages` parameter (v3.1+) allows specifying multiple languages to try for transcription. The system will:
- Transcribe with each language + auto-detection
- Store all results in separate fields (`text-es`, `text-ru`, `text-null`)  
- Makes **(N + 1) API calls per segment**
- Default: `["es", "ru"]` → 3 API calls per segment

## Часто задаваемые вопросы

**Q: Обрабатываются ли файлы параллельно?**  
A: Нет, файлы обрабатываются последовательно, один за другим.

**Q: Можно ли обработать файлы из поддиректорий?**  
A: Нет, сканируется только указанная директория (не рекурсивно).

**Q: Что происходит при ошибке обработки одного файла?**  
A: Обработка останавливается. Используйте Ctrl+C и перезапустите - обработанные файлы будут использовать кэш.

**Q: Как изменить порядок обработки файлов?**  
A: Файлы обрабатываются в алфавитном порядке. Используйте параметр `files` для явного указания порядка.

**Q: Можно ли комбинировать `input_dir` и `files`?**  
A: Нет, `input_dir` имеет приоритет и игнорирует другие параметры.

## См. также

- [CHANGELOG_v3.1.md](CHANGELOG_v3.1.md) - Подробности о версии 3.1
- [QUICK_START.md](QUICK_START.md) - Быстрый старт
- [README.md](../README.md) - Основная документация

