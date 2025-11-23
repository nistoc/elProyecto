# 🎯 Подробная инструкция по использованию

Полное руководство по использованию Agent03: Transcript Improver.

## Подготовка

### Системные требования

- Python 3.8+
- OpenAI API ключ
- ~50 MB свободного места

### Установка зависимостей

```bash
cd agent03-trans-improver
pip install -r requirements.txt
```

Или установка как пакет:

```bash
pip install -e .
```

После установки доступна команда:

```bash
agent03-improver
```

### Настройка API ключа

**Вариант 1: Переменная окружения (рекомендуется)**

```bash
# Windows PowerShell
$env:OPENAI_API_KEY = "sk-your-key-here"

# Linux/Mac/Git Bash
export OPENAI_API_KEY="sk-your-key-here"
```

**Вариант 2: В конфиге**

```json
{
  "openai_api_key": "sk-your-actual-key"
}
```

⚠️ Не коммитьте ключ в git!

## Подготовка файла для обработки

### Копирование transcript.md

**Из agent01:**

```bash
cp ../agent01/transcript.md .
```

**Из произвольного места:**

```bash
cp /path/to/your/transcript.md .
```

**Или другое имя файла:**

Измените `input_file` в `config/default.json`:

```json
{
  "input_file": "my_custom_file.md"
}
```

### Формат файла

Agent03 ожидает файл в формате:

```
>>>>>>>
- 72.12 speaker_0: "текст с кириллическими испанскими словами"
- 75.82 speaker_1: "más texto"
...
<<<<<
```

- `>>>>>>>` - начало контента
- `<<<<<` - конец контента
- Строки между ними обрабатываются

Если маркеров нет, обрабатывается весь файл.

## Конфигурация

### Основные параметры

Отредактируйте `config/default.json`:

```json
{
  "input_file": "transcript.md",
  "output_file": "transcript_fixed.md",
  "model": "gpt-4o-mini",
  "temperature": 0.2,
  "batch_size": 10,
  "context_lines": 5,
  "prompt_file": "prompts/default.txt",
  "add_timestamp_to_output": true,
  "save_intermediate": true,
  "intermediate_dir": "intermediate_fixes",
  "skip_if_exists": false,
  "verbose": true
}
```

### Описание параметров

#### input_file
Путь к исходному файлу для обработки.
- **По умолчанию:** `transcript.md`
- **Может быть:** относительный или абсолютный путь

#### output_file
Куда сохранить исправленный файл.
- **По умолчанию:** `transcript_fixed.md`
- **Совет:** используйте суффикс `_fixed` для ясности

#### add_timestamp_to_output
Добавлять ли дату и время к имени выходного файла.
- **По умолчанию:** `false`
- **true:** создаст файл типа `transcript_fixed_2024-11-23_15-30-45.md`
- **false:** использовать имя из `output_file` как есть
- **Полезно:** для версионирования и истории обработки

#### model
Какую модель GPT использовать.
- **Варианты:** `gpt-4o-mini`, `gpt-4o`, `gpt-3.5-turbo`
- **Рекомендуется:** `gpt-4o-mini` (баланс качества/цены)
- **Для лучшего качества:** `gpt-4o`
- **Для экономии:** `gpt-3.5-turbo`

#### temperature
Вариативность ответов модели.
- **Диапазон:** 0.0 - 2.0
- **По умолчанию:** 0.2
- **0.0** - детерминированные ответы (одинаковые при повторе)
- **0.2** - небольшая вариативность (рекомендуется)
- **>0.5** - креативные ответы (может ошибаться)

#### batch_size
Количество строк в одном batch для отправки в GPT.
- **Диапазон:** 5-30
- **По умолчанию:** 10
- **Меньше (5):** дешевле, но медленнее
- **Больше (20-30):** быстрее, но дороже
- **Оптимально:** 10-15

#### context_lines
Сколько строк из предыдущего batch передавать как контекст.
- **Диапазон:** 1-5
- **По умолчанию:** 3
- **Больше:** лучше понимание диалога, но дороже
- **Меньше:** дешевле, но может терять контекст

#### prompt_file
Путь к файлу с кастомным промптом.
- **По умолчанию:** `null` (используется встроенный промпт)
- **Пример:** `"prompts/default.txt"` или `"prompts/my_custom.txt"`
- **См.:** [prompts/README.md](../prompts/README.md) для деталей

#### save_intermediate
Сохранять ли промежуточные результаты каждого batch.
- **По умолчанию:** `true`
- **true:** можно отладить, если что-то пошло не так
- **false:** не создавать дополнительные файлы

#### intermediate_dir
Папка для промежуточных результатов.
- **По умолчанию:** `intermediate_fixes`

#### skip_if_exists
Пропускать обработку, если выходной файл уже существует.
- **По умолчанию:** `false`
- **true:** полезно в автоматических скриптах

#### verbose
Выводить ли подробные логи.
- **По умолчанию:** `true`
- **false:** только основная информация

## Запуск

### Базовый запуск

```bash
python -m cli.main
```

### Если установлен как пакет

```bash
agent03-improver
```

### Вывод при запуске

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
- Context lines: 5
============================================================

[INFO] Reading transcript from transcript.md
[INFO] Found 1100 content lines to process
[INFO] Total batches: 110

[BATCH 1/110] Processing lines 1-10...
[API] ✓ Fixed 10 lines
[BATCH 2/110] Processing lines 11-20...
[API] ✓ Fixed 10 lines
...
```

### Прерывание процесса

Нажмите `Ctrl+C` для остановки:

```
^C
[WARN] Interrupted by user!
[INFO] Partial results saved in intermediate_fixes/
```

Промежуточные результаты сохраняются в `intermediate_fixes/`.

## Результаты

### Выходной файл

Исправленная транскрипция сохраняется в `output_file`:

```bash
cat transcript_fixed.md
```

### Промежуточные файлы

Если `save_intermediate: true`, в `intermediate_fixes/` создаются:

```
batch_0001_of_0110.json
batch_0002_of_0110.json
...
```

Формат файла:

```json
{
  "batch_index": 0,
  "success": true,
  "error": null,
  "fixed_lines": [
    "- 72.12 speaker_0: \"vale, por favor\"",
    ...
  ]
}
```

### Проверка качества

**Сравнение файлов:**

```bash
diff transcript.md transcript_fixed.md | head -50
```

**С подсветкой (если установлен git):**

```bash
git diff --no-index transcript.md transcript_fixed.md
```

**Подсчет изменений:**

```bash
diff transcript.md transcript_fixed.md | grep "^<" | wc -l
```

## Типичные сценарии

### Сценарий 1: Быстрая обработка

```json
{
  "model": "gpt-3.5-turbo",
  "batch_size": 20,
  "context_lines": 2,
  "save_intermediate": false
}
```

```bash
python -m cli.main
```

**Результат:** ~2-3 минуты, $0.02-0.05

### Сценарий 2: Максимальное качество

```json
{
  "model": "gpt-4o",
  "batch_size": 5,
  "context_lines": 5,
  "temperature": 0.0
}
```

```bash
python -m cli.main
```

**Результат:** ~10-15 минут, $0.50-1.00

### Сценарий 3: Тестирование на маленьком файле

```bash
# Создайте тестовый файл (первые 50 строк)
head -n 50 transcript.md > test_small.md
```

```json
{
  "input_file": "test_small.md",
  "output_file": "test_small_fixed.md"
}
```

```bash
python -m cli.main
```

### Сценарий 4: Пакетная обработка

```bash
# Bash скрипт для обработки нескольких файлов
for file in transcript*.md; do
    python -m cli.main --config custom_config.json \
        --input "$file" \
        --output "${file%.md}_fixed.md"
done
```

### Сценарий 5: Версионирование с timestamp

Полезно для отслеживания истории обработки:

```json
{
  "output_file": "transcript_fixed.md",
  "add_timestamp_to_output": true
}
```

```bash
python -m cli.main
```

**Результат:** 
- Первый запуск: `transcript_fixed_2024-11-23_15-30-45.md`
- Второй запуск: `transcript_fixed_2024-11-23_16-12-08.md`
- Третий запуск: `transcript_fixed_2024-11-24_10-05-33.md`

**Преимущества:**
- Сохраняется история всех обработок
- Можно сравнивать результаты разных промптов/настроек
- Не перезаписывается предыдущий результат

**Пример сравнения версий:**

```bash
# Сравнить две версии
diff transcript_fixed_2024-11-23_15-30-45.md \
     transcript_fixed_2024-11-23_16-12-08.md

# Найти самый новый файл
ls -t transcript_fixed_*.md | head -1
```

## Workflow с Agent01

Типичный workflow обработки аудио:

```bash
# 1. Транскрибируем аудио (Agent01)
cd agent01
python -m cli.main
# → Результат: transcript.md

# 2. Копируем в Agent03
cp transcript.md ../agent03-trans-improver/

# 3. Исправляем испанские слова
cd ../agent03-trans-improver
python -m cli.main
# → Результат: transcript_fixed.md

# 4. Готово!
cat transcript_fixed.md
```

## Устранение проблем

### Ошибка: "OPENAI_API_KEY not found"

**Причина:** API ключ не установлен.

**Решение:**

```bash
# Проверьте, что ключ установлен
echo $OPENAI_API_KEY  # должен показать "sk-..."

# Если пусто, установите
export OPENAI_API_KEY="sk-..."
```

### Ошибка: "Input file not found"

**Причина:** Файл `transcript.md` не найден.

**Решение:**

```bash
# Проверьте наличие
ls transcript.md

# Скопируйте, если отсутствует
cp ../agent01/transcript.md .

# Или измените путь в конфиге
vim config/default.json  # "input_file": "другой_путь.md"
```

### Ошибка: "Rate limit exceeded"

**Причина:** Превышен лимит запросов к OpenAI API.

**Решение:**

```bash
# Подождите 1 минуту и повторите
sleep 60
python -m cli.main
```

### Некорректные исправления

**Проблема:** GPT неправильно исправляет слова.

**Решения:**

1. **Использовать более мощную модель:**
   ```json
   {"model": "gpt-4o"}
   ```

2. **Увеличить контекст:**
   ```json
   {"context_lines": 5}
   ```

3. **Уменьшить temperature:**
   ```json
   {"temperature": 0.0}
   ```

4. **Уменьшить batch_size** (больше внимания каждой строке):
   ```json
   {"batch_size": 5}
   ```

### Слишком дорого

**Проблема:** Обработка стоит слишком много.

**Решения:**

1. **Дешевая модель:**
   ```json
   {"model": "gpt-3.5-turbo"}
   ```

2. **Больший batch_size:**
   ```json
   {"batch_size": 20}
   ```

3. **Меньше контекста:**
   ```json
   {"context_lines": 1}
   ```

### Медленная обработка

**Проблема:** Обработка занимает слишком много времени.

**Решения:**

1. **Больший batch_size:**
   ```json
   {"batch_size": 20}
   ```

2. **Быстрая модель:**
   ```json
   {"model": "gpt-3.5-turbo"}
   ```

## Оптимизация производительности

### Выбор batch_size

| Строк в файле | Рекомендуемый batch_size |
|---------------|--------------------------|
| < 500 | 5-10 |
| 500-2000 | 10-15 |
| 2000-5000 | 15-20 |
| > 5000 | 20-30 |

### Выбор context_lines

| Тип контента | context_lines |
|--------------|---------------|
| Разрозненные фразы | 1-2 |
| Обычный диалог | 3 |
| Связная речь | 4-5 |

### Оценка времени и стоимости

**Формулы:**

- Батчей = `строк / batch_size`
- Время ≈ `батчей × 2-5 секунд`
- Токены ≈ `батчей × (batch_size × 50 + context_lines × 50)`

**Пример:** 1100 строк, batch_size=10, context=3

- Батчей: 110
- Время: 3-9 минут
- Токены: ~60,000
- Стоимость (gpt-4o-mini): $0.05-0.10

## Дополнительно

### Интеграция в CI/CD

```yaml
# .github/workflows/transcript-fix.yml
name: Fix Transcript
on: [push]
jobs:
  fix:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - name: Setup Python
        uses: actions/setup-python@v2
      - name: Install deps
        run: pip install -r requirements.txt
      - name: Fix transcript
        env:
          OPENAI_API_KEY: ${{ secrets.OPENAI_API_KEY }}
        run: python -m cli.main
```

### Логирование

Все логи выводятся в stdout. Сохранение в файл:

```bash
python -m cli.main 2>&1 | tee processing.log
```

### Debugging

Для отладки включите verbose:

```json
{"verbose": true}
```

И проверяйте промежуточные результаты:

```bash
cat intermediate_fixes/batch_0001_of_0110.json | jq .
```

---

📚 **См. также:**
- [Примеры конфигураций](EXAMPLES.md)
- [Архитектура системы](ARCHITECTURE.md)

