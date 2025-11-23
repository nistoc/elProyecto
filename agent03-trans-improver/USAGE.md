# 🎯 Использование Agent03

## Шаг 1: Подготовка

### Установите зависимости

```bash
cd agent03-trans-improver
pip install -r requirements.txt
```

Или установите как пакет:

```bash
pip install -e .
```

### Установите API ключ

```bash
# Windows PowerShell
$env:OPENAI_API_KEY = "sk-your-key-here"

# Linux/Mac
export OPENAI_API_KEY="sk-your-key-here"
```

## Шаг 2: Подготовка файла

Скопируйте ваш `transcript.md` в корень `agent03-trans-improver/`:

```bash
# Из agent01
cp ../agent01/transcript.md .

# Или откуда угодно
cp /path/to/your/transcript.md .
```

## Шаг 3: Настройка (опционально)

Отредактируйте `config/default.json` если нужно:

```json
{
  "input_file": "transcript.md",        // ваш файл
  "output_file": "transcript_fixed.md", // куда сохранить
  "model": "gpt-4o-mini",               // модель
  "batch_size": 10,                     // строк за раз
  "context_lines": 3                    // контекст из предыдущего batch
}
```

## Шаг 4: Запуск

```bash
python -m cli.main
```

Или если установили как пакет:

```bash
agent03-improver
```

## Шаг 5: Результат

Исправленный файл будет в `transcript_fixed.md`!

```bash
# Посмотреть результат
cat transcript_fixed.md

# Или открыть в редакторе
code transcript_fixed.md
```

## Примеры вывода

### Успешная обработка

```
============================================================
Agent03: Transcript Improver
============================================================
Execution plan:
- Config: config/default.json
- Input file: transcript.md
- Output file: transcript_fixed.md
- Model: gpt-4o-mini
- Batch size: 10 lines
- Context lines: 3
============================================================

[INFO] Reading transcript from transcript.md
[INFO] Found 1100 content lines to process
[INFO] Total batches: 110

[BATCH 1/110] Processing lines 1-10...
[API] ✓ Fixed 10 lines
[BATCH 2/110] Processing lines 11-20...
[API] ✓ Fixed 10 lines
...
[BATCH 110/110] Processing lines 1091-1100...
[API] ✓ Fixed 10 lines

[INFO] Writing fixed transcript to transcript_fixed.md
[INFO] ✓ Fixed transcript saved to transcript_fixed.md

============================================================
Processing complete! 🎉
============================================================
```

### Ошибка: API ключ не найден

```
[ERROR] OPENAI_API_KEY not set!
Please set it in environment or config file:
  export OPENAI_API_KEY='sk-...'
```

**Решение**: установите API ключ (см. Шаг 1)

### Ошибка: файл не найден

```
[ERROR] Input file not found: transcript.md

Please copy your transcript file to this location:
  cp /path/to/transcript.md transcript.md
```

**Решение**: скопируйте файл (см. Шаг 2)

## Проверка качества

### До исправления

```
- 75.82 speaker_0: "вале, пор фавор, эске но те ляман"
```

### После исправления

```
- 75.82 speaker_0: "vale, por favor, es que no te llaman"
```

### Сравнить файлы

```bash
# Показать различия
diff transcript.md transcript_fixed.md | head -50

# Подсветить изменения
git diff --no-index transcript.md transcript_fixed.md
```

## Промежуточные результаты

Если включено `"save_intermediate": true`, результаты сохраняются в `intermediate_fixes/`:

```bash
ls intermediate_fixes/
# batch_0001_of_0110.json
# batch_0002_of_0110.json
# ...
```

Посмотреть результат одного батча:

```bash
cat intermediate_fixes/batch_0001_of_0110.json
```

## Типичные проблемы

### 1. Rate limit error

```
[ERROR] ✗ Failed: Rate limit reached
```

**Решение**: подождите минуту и запустите снова.

### 2. Некорректные исправления

**Решение**: попробуйте другую модель:
```json
{"model": "gpt-4o"}
```

### 3. Слишком дорого

**Решение**: используйте более дешевую модель:
```json
{"model": "gpt-3.5-turbo"}
```

## Workflow с Agent01

```bash
# 1. Транскрибируем аудио
cd agent01
python -m cli.main
# Результат: transcript.md

# 2. Копируем в agent03
cp transcript.md ../agent03-trans-improver/

# 3. Исправляем
cd ../agent03-trans-improver
python -m cli.main
# Результат: transcript_fixed.md

# 4. Готово!
```

## Дальше

- [Полная документация](README.md)
- [Быстрый старт](docs/QUICK_START.md)
- [Примеры конфигурации](docs/EXAMPLES.md)
- [Архитектура](docs/ARCHITECTURE.md)

