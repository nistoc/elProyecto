# 🔧 Agent03: Transcript Improver

Агент для исправления транскрипций с кириллической транслитерацией испанских слов.

## Описание

Agent03 исправляет транскрипции, где испанские слова были записаны кириллицей (например, "вале" → "vale", "пор фавор" → "por favor"). Использует GPT для интеллектуального исправления с учетом контекста.

## Особенности

- ✅ **Контекстное исправление**: каждый batch получает контекст из предыдущего
- ✅ **Настраиваемый batch size**: контроль размера порций для обработки
- ✅ **Прогресс в реальном времени**: отображение процесса обработки
- ✅ **Сохранение промежуточных результатов**: можно восстановить работу
- ✅ **Поддержка разных моделей**: GPT-4o, GPT-4o-mini, GPT-3.5-turbo

## Установка

```bash
cd agent03-trans-improver
pip install -e .
```

Или установить зависимости напрямую:

```bash
pip install -r requirements.txt
```

## Настройка

### 1. API ключ

Установите ваш OpenAI API ключ:

```bash
# Windows PowerShell
$env:OPENAI_API_KEY = "sk-..."

# Linux/Mac
export OPENAI_API_KEY="sk-..."
```

### 2. Конфигурация

Отредактируйте `config/default.json`:

```json
{
  "input_file": "transcript.md",
  "output_file": "transcript_fixed.md",
  "model": "gpt-4o-mini",
  "batch_size": 10,
  "context_lines": 3,
  "temperature": 0.0,
  "save_intermediate": true,
  "intermediate_dir": "intermediate_fixes"
}
```

### Параметры конфигурации

| Параметр | Описание | По умолчанию |
|----------|----------|--------------|
| `input_file` | Путь к исходной транскрипции | `transcript.md` |
| `output_file` | Путь для сохранения исправленной версии | `transcript_fixed.md` |
| `model` | Модель GPT для исправления | `gpt-4o-mini` |
| `batch_size` | Количество строк в одном batch | `10` |
| `context_lines` | Количество строк контекста из предыдущего batch | `3` |
| `temperature` | Температура модели (0.0 = детерминированно) | `0.0` |
| `save_intermediate` | Сохранять промежуточные результаты | `true` |
| `intermediate_dir` | Папка для промежуточных результатов | `intermediate_fixes` |
| `openai_api_key` | API ключ (или `env:OPENAI_API_KEY`) | `env:OPENAI_API_KEY` |

## Использование

### Быстрый старт

1. Скопируйте ваш `transcript.md` в корень `agent03-trans-improver/`:
   ```bash
   cp ../agent01/transcript.md .
   ```

2. Запустите исправление:
   ```bash
   python -m cli.main
   ```

3. Результат будет в `transcript_fixed.md`

### Пример вывода

```
[INFO] Agent03: Transcript Improver
[INFO] Configuration loaded from config/default.json
[INFO] Input file: transcript.md
[INFO] Output file: transcript_fixed.md
[INFO] Model: gpt-4o-mini
[INFO] Batch size: 10 lines
[INFO] Context: 3 lines from previous batch

[INFO] Reading transcript from transcript.md
[INFO] Found 1100 content lines to process
[INFO] Total batches: 110

[BATCH 1/110] Processing lines 1-10...
[API] Sending to GPT-4o-mini... ✓
[BATCH 2/110] Processing lines 11-20...
[API] Sending to GPT-4o-mini... ✓
...
[BATCH 110/110] Processing lines 1091-1100...
[API] Sending to GPT-4o-mini... ✓

[INFO] Fixed transcript saved to transcript_fixed.md
[INFO] Processing complete! 🎉
```

## Как это работает

### Обработка с контекстом

```
Batch 1: lines 1-10
  Context: none
  → GPT fixes → result1

Batch 2: lines 11-20
  Context: last 3 lines from result1
  → GPT fixes → result2

Batch 3: lines 21-30
  Context: last 3 lines from result2
  → GPT fixes → result3
...
```

### Промпт для GPT

Агент отправляет каждому batch следующий промпт:

```
You are fixing a Russian-Spanish language learning transcript.

Context from previous batch (for continuity):
[last 3 lines from previous result]

Current batch to fix:
[10 lines]

Task:
1. Find Spanish words written in Cyrillic
2. Replace with correct Spanish spelling in Latin
3. Keep timestamps, speakers, and Russian text unchanged
4. Preserve line format exactly

Return ONLY the fixed lines.
```

## Примеры исправлений

| До | После |
|----|-------|
| `вале` | `vale` |
| `пор фавор` | `por favor` |
| `граcias` → уже правильно | `gracias` |
| `эске` | `es que` |
| `entonces` → уже правильно | `entonces` |
| `кохер` | `coger` |
| `аста` | `hasta` |

## Стоимость

Для файла ~1100 строк (batch_size=10):

| Модель | Примерная стоимость |
|--------|---------------------|
| **gpt-4o-mini** | $0.05 - $0.10 |
| **gpt-4o** | $0.50 - $1.00 |
| **gpt-3.5-turbo** | $0.02 - $0.05 |

## Troubleshooting

### Ошибка: "OPENAI_API_KEY not found"

Убедитесь, что установили ключ:
```bash
echo $OPENAI_API_KEY  # должен показать ваш ключ
```

### Ошибка: "Input file not found"

Проверьте путь в конфиге и что файл существует:
```bash
ls transcript.md
```

### Некорректные исправления

Попробуйте:
1. Увеличить `batch_size` для большего контекста
2. Использовать `gpt-4o` вместо `gpt-4o-mini`
3. Увеличить `context_lines` до 5

## Структура проекта

```
agent03-trans-improver/
├── cli/
│   └── main.py              # Точка входа
├── config/
│   └── default.json         # Конфигурация
├── core/
│   ├── config.py            # Загрузка конфига
│   └── models.py            # Модели данных
├── services/
│   └── fixer.py             # Логика исправления
├── intermediate_fixes/      # Промежуточные результаты
├── transcript.md            # Входной файл (копируете сами)
├── transcript_fixed.md      # Выходной файл
├── README.md
├── requirements.txt
└── setup.py
```

## Лицензия

MIT

