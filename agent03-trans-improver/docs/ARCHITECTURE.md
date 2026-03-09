# 🏗️ Architecture

Архитектура Agent03: Transcript Improver.

## Обзор

Agent03 построен по модульной архитектуре, аналогичной Agent01:

```
┌─────────────────────────────────────────────────┐
│              CLI Entry Point                     │
│              (cli/main.py)                       │
└────────────────┬────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────┐
│          Configuration Layer                     │
│            (core/config.py)                      │
│  • Load JSON config                              │
│  • Resolve env vars                              │
│  • Apply defaults                                │
└────────────────┬────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────┐
│         Service Layer                            │
│       (services/fixer.py)                        │
│  • Parse transcript structure                    │
│  • Create batches                                │
│  • Fix batches with GPT                          │
│  • Save intermediate results                     │
└────────────────┬────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────┐
│          OpenAI API                              │
│  • GPT-4o / GPT-4o-mini / GPT-3.5-turbo          │
└─────────────────────────────────────────────────┘
```

## Модули

### 1. CLI (`cli/`)

**Назначение**: Точка входа, обработка аргументов, управление процессом.

**Файлы**:
- `main.py`: основная логика CLI

**Функции**:
- Загрузка конфигурации
- Валидация входных данных
- Инициализация сервисов
- Обработка ошибок и прерываний

### 2. Core (`core/`)

**Назначение**: Базовая функциональность, модели данных, конфигурация.

**Файлы**:
- `config.py`: управление конфигурацией
- `models.py`: модели данных

**Классы**:

#### Config
```python
class Config:
    def from_file(path: str) -> Config
    def get(key: str, default=None)
    def print_plan(config_path: str)
```

#### BatchInfo
```python
@dataclass
class BatchInfo:
    index: int
    start_line: int
    end_line: int
    lines: List[str]
    context: Optional[List[str]]
```

#### BatchResult
```python
@dataclass
class BatchResult:
    batch_index: int
    fixed_lines: List[str]
    success: bool
    error: Optional[str]
```

### 3. Services (`services/`)

**Назначение**: Бизнес-логика исправления транскриптов.

**Файлы**:
- `fixer.py`: основной сервис исправления

**Класс TranscriptFixer**:

```python
class TranscriptFixer:
    def __init__(api_key, model, temperature, ...)
    
    # Основной метод
    def fix_transcript_file(
        input_path,
        output_path,
        batch_size,
        context_lines,
        save_intermediate,
        intermediate_dir
    )
    
    # Внутренние методы
    def _parse_transcript_structure(lines)
    def _create_batches(lines, batch_size)
    def _fix_batch(batch_info)
    def _build_fix_prompt(batch_info)
    def _save_intermediate_batch(result, ...)
```

## Поток данных

### 1. Инициализация

```
config/default.json
       ↓
Config.from_file()
       ↓
Config object
       ↓
TranscriptFixer(config)
```

### 2. Чтение файла

```
transcript.md
       ↓
Read all lines
       ↓
Parse structure:
  - header (before >>>>>>>)
  - content (between >>>>>>> and <<<<<)
  - footer (after <<<<<)
```

### 3. Создание батчей

```
content lines (N lines)
       ↓
Split into batches of batch_size
       ↓
BatchInfo objects:
  - Batch 0: lines 0-9
  - Batch 1: lines 10-19
  - ...
  - Batch K: lines N-batch_size to N
```

### 4. Обработка батчей

```
For each batch:
  ┌──────────────────────────────────────┐
  │ 1. Get context from previous batch   │
  │    (last context_lines lines)        │
  └──────────────┬───────────────────────┘
                 ↓
  ┌──────────────────────────────────────┐
  │ 2. Build prompt:                     │
  │    - Context (if available)          │
  │    - Current batch lines             │
  └──────────────┬───────────────────────┘
                 ↓
  ┌──────────────────────────────────────┐
  │ 3. Send to GPT API                   │
  │    model: gpt-4o-mini                │
  │    temperature: 0.0                  │
  └──────────────┬───────────────────────┘
                 ↓
  ┌──────────────────────────────────────┐
  │ 4. Parse response                    │
  │    - Split into lines                │
  │    - Validate line count             │
  └──────────────┬───────────────────────┘
                 ↓
  ┌──────────────────────────────────────┐
  │ 5. Save intermediate result          │
  │    intermediate_fixes/batch_NNNN.json│
  └──────────────┬───────────────────────┘
                 ↓
  ┌──────────────────────────────────────┐
  │ 6. Update context for next batch     │
  │    context = fixed_lines[-3:]        │
  └──────────────────────────────────────┘
```

### 5. Сохранение результата

```
Fixed lines
       ↓
Combine with header and footer
       ↓
Write to output file
       ↓
transcript_fixed.md
```

## Контекст между батчами

Ключевая особенность Agent03 - передача контекста:

```
Batch 1: lines 1-10
  Context: none
  → GPT fixes → result1 (10 fixed lines)

Batch 2: lines 11-20
  Context: result1[-3:] (last 3 lines from result1)
  → GPT fixes → result2 (10 fixed lines)

Batch 3: lines 21-30
  Context: result2[-3:] (last 3 lines from result2)
  → GPT fixes → result3 (10 fixed lines)
```

### Зачем контекст?

1. **Непрерывность**: GPT видит, как закончился предыдущий батч
2. **Консистентность**: одинаковые слова исправляются одинаково
3. **Диалог**: понимание разговорного контекста

## Промпт-инжиниринг

### Структура промпта

```
You are fixing a Russian-Spanish language learning transcript.

[System instructions]

Context from previous batch (for continuity):
```
[last 3 lines from previous result]
```

Current batch to fix:
```
[10 lines from current batch]
```

[Task description and constraints]

Return ONLY the fixed lines (same count as input).
```

### Ключевые аспекты

1. **System message**: четкая роль (transcript editor)
2. **Context**: для непрерывности
3. **Constraints**: 
   - Same line count
   - Preserve format
   - Only fix Spanish
   - No translation
4. **Output format**: только исправленные строки

## Промежуточные результаты

### Формат

```json
{
  "batch_index": 0,
  "success": true,
  "error": null,
  "fixed_lines": [
    "- 72.12 speaker_0: \"Ah, muy bien, estoy conectándome, todo bien.\"",
    "- 75.82 speaker_0: \"¿Por qué no quieres trabajar en Teams?\"",
    ...
  ]
}
```

### Назначение

1. **Восстановление**: если процесс прервался
2. **Отладка**: проверка качества исправлений
3. **Мониторинг**: отслеживание прогресса

## Обработка ошибок

### Уровни ошибок

1. **Configuration errors** (exit code 2):
   - Config file not found
   - Invalid JSON

2. **Validation errors** (exit code 1):
   - API key not set
   - Input file not found
   
3. **Processing errors**:
   - API call failed → use original lines for batch
   - Line count mismatch → use original lines
   
4. **User interruption** (exit code 130):
   - Ctrl+C → graceful shutdown
   - Partial results saved

### Fallback strategy

```
Try to fix batch
  ↓
[API error]
  ↓
Log error
  ↓
Use original lines (no changes)
  ↓
Continue to next batch
```

## Масштабирование

### Для больших файлов (10000+ строк)

1. **Увеличить batch_size**:
   ```json
   {"batch_size": 20}
   ```
   Меньше батчей → быстрее

2. **Использовать промежуточные результаты**:
   ```json
   {"save_intermediate": true}
   ```
   Можно возобновить при ошибке

3. **Уменьшить context_lines**:
   ```json
   {"context_lines": 2}
   ```
   Меньше токенов → дешевле

### Параллелизация

**Текущая версия**: последовательная обработка (нужен контекст).

**Будущее**: можно параллелить с общим контекстом:
```
Batch 1 → result1
         ↓
Batch 2, 3, 4 (parallel) with context from result1
         ↓
Batch 5, 6, 7 (parallel) with context from result4
```

## Сравнение с Agent01

| Аспект | Agent01 | Agent03 |
|--------|---------|---------|
| **Назначение** | Транскрипция аудио | Исправление транскриптов |
| **API** | Whisper / GPT-4o-transcribe | GPT-4o / GPT-4o-mini |
| **Обработка** | Параллельная (chunks) | Последовательная (batches) |
| **Контекст** | Overlap между chunks | Context из предыдущего batch |
| **Стоимость** | Высокая (транскрипция) | Низкая (текст) |
| **Скорость** | Медленная (аудио) | Быстрая (текст) |

## Зависимости

### Основные
- **openai** >= 1.12.0: клиент OpenAI API

### Python
- Python >= 3.8
- Стандартная библиотека: json, os, sys, dataclasses, typing

## Тестирование

### Unit tests (TODO)
```bash
pytest tests/
```

### Integration test
```bash
# Копируем тестовый файл
cp test_data/transcript_small.md transcript.md

# Запускаем
python -m cli.main

# Проверяем результат
diff transcript_fixed.md test_data/expected_output.md
```

## Будущие улучшения

1. **Resume capability**: продолжение с последнего батча
2. **Parallel processing**: с shared context
3. **Custom dictionaries**: для специфичных слов
4. **Quality metrics**: оценка качества исправлений
5. **Interactive mode**: предпросмотр и подтверждение

