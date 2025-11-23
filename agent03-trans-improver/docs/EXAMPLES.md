# 📚 Configuration Examples

Примеры конфигураций для различных сценариев.

## Базовая конфигурация (рекомендуется)

```json
{
  "input_file": "transcript.md",
  "output_file": "transcript_fixed.md",
  "model": "gpt-4o-mini",
  "temperature": 0.0,
  "batch_size": 10,
  "context_lines": 3,
  "save_intermediate": true,
  "intermediate_dir": "intermediate_fixes"
}
```

**Подходит для**: большинства случаев, баланс скорости/качества/цены.

---

## Быстрая обработка (большие файлы)

```json
{
  "input_file": "transcript.md",
  "output_file": "transcript_fixed.md",
  "model": "gpt-3.5-turbo",
  "temperature": 0.0,
  "batch_size": 20,
  "context_lines": 2,
  "save_intermediate": false
}
```

**Плюсы**: 
- Быстрее в 2-3 раза
- Дешевле в 10 раз

**Минусы**:
- Меньше точность
- Меньше контекста

---

## Максимальное качество

```json
{
  "input_file": "transcript.md",
  "output_file": "transcript_fixed.md",
  "model": "gpt-4o",
  "temperature": 0.0,
  "batch_size": 5,
  "context_lines": 5,
  "save_intermediate": true,
  "intermediate_dir": "intermediate_fixes"
}
```

**Плюсы**:
- Лучшее качество исправлений
- Больше контекста

**Минусы**:
- Дороже в 10 раз
- Медленнее в 2 раза

---

## Тестовая конфигурация (первый запуск)

```json
{
  "input_file": "transcript_small.md",
  "output_file": "transcript_fixed_test.md",
  "model": "gpt-4o-mini",
  "temperature": 0.0,
  "batch_size": 5,
  "context_lines": 2,
  "save_intermediate": true,
  "intermediate_dir": "test_fixes",
  "verbose": true
}
```

**Когда использовать**: первый раз, чтобы проверить качество на маленьком файле.

---

## Экономная конфигурация (минимум токенов)

```json
{
  "input_file": "transcript.md",
  "output_file": "transcript_fixed.md",
  "model": "gpt-3.5-turbo",
  "temperature": 0.0,
  "batch_size": 30,
  "context_lines": 1,
  "save_intermediate": false
}
```

**Плюсы**: минимальная стоимость
**Минусы**: меньше качество и контекста

---

## Пропуск существующих файлов

```json
{
  "input_file": "transcript.md",
  "output_file": "transcript_fixed.md",
  "model": "gpt-4o-mini",
  "batch_size": 10,
  "context_lines": 3,
  "skip_if_exists": true
}
```

**Когда использовать**: при повторных запусках скриптов, чтобы не перезаписывать.

---

## Кастомный API endpoint

```json
{
  "input_file": "transcript.md",
  "output_file": "transcript_fixed.md",
  "model": "gpt-4o-mini",
  "openai_api_key": "sk-...",
  "openai_base_url": "https://custom-api.example.com/v1",
  "openai_organization": "org-...",
  "batch_size": 10,
  "context_lines": 3
}
```

**Когда использовать**: при использовании прокси или альтернативных API.

---

## Сравнение конфигураций

| Параметр | Быстрая | Базовая | Качественная |
|----------|---------|---------|--------------|
| **Model** | gpt-3.5-turbo | gpt-4o-mini | gpt-4o |
| **Batch size** | 20 | 10 | 5 |
| **Context lines** | 2 | 3 | 5 |
| **Скорость** | ⚡⚡⚡⚡ | ⚡⚡⚡ | ⚡⚡ |
| **Качество** | ⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| **Стоимость** | 💰 | 💰💰 | 💰💰💰💰 |

---

## Оценка стоимости

Для файла **1100 строк**:

### Быстрая (gpt-3.5-turbo, batch=20)
- Батчей: ~55
- Токенов: ~40K
- **Стоимость: $0.02-0.03**

### Базовая (gpt-4o-mini, batch=10)
- Батчей: ~110
- Токенов: ~60K
- **Стоимость: $0.05-0.10**

### Качественная (gpt-4o, batch=5)
- Батчей: ~220
- Токенов: ~80K
- **Стоимость: $0.80-1.20**

---

## Подбор оптимальных параметров

### Определите размер файла

```bash
wc -l transcript.md
# Например: 1100 lines
```

### Выберите batch_size

| Строк в файле | Рекомендуемый batch_size |
|---------------|--------------------------|
| < 500 | 5-10 |
| 500-2000 | 10-15 |
| 2000-5000 | 15-20 |
| > 5000 | 20-30 |

### Выберите context_lines

| Важность контекста | context_lines |
|--------------------|---------------|
| Низкая (разные диалоги) | 1-2 |
| Средняя (обычный случай) | 3 |
| Высокая (связная речь) | 4-5 |

### Выберите модель

```
Тест на малом файле → смотрим качество
                ↓
Хорошо? → используем gpt-4o-mini
                ↓
Плохо? → пробуем gpt-4o
                ↓
Очень плохо? → проверяем промпт
```

---

## Переменные окружения

Вместо хардкода API ключа в конфиге:

```json
{
  "openai_api_key": "env:OPENAI_API_KEY"
}
```

Установите переменную:

```bash
# Windows PowerShell
$env:OPENAI_API_KEY = "sk-..."

# Linux/Mac
export OPENAI_API_KEY="sk-..."
```

---

## Работа с разными файлами

### Вариант 1: Меняйте конфиг

```bash
# Обработать file1
vim config/default.json  # input_file: "file1.md"
python -m cli.main

# Обработать file2
vim config/default.json  # input_file: "file2.md"
python -m cli.main
```

### Вариант 2: Несколько конфигов

```bash
# Создайте config/file1.json
cp config/default.json config/file1.json
vim config/file1.json  # input_file: "file1.md"

# Создайте config/file2.json
cp config/default.json config/file2.json
vim config/file2.json  # input_file: "file2.md"

# TODO: добавить поддержку --config в CLI
```

---

## Полезные комбинации

### После обработки agent01

```bash
# 1. Транскрибируем аудио (agent01)
cd agent01
python -m cli.main

# 2. Копируем результат
cp transcript.md ../agent03-trans-improver/

# 3. Исправляем (agent03)
cd ../agent03-trans-improver
python -m cli.main

# 4. Результат в transcript_fixed.md
```

### Пакетная обработка (bash)

```bash
for file in transcript*.md; do
    # Update config
    jq ".input_file = \"$file\" | .output_file = \"${file%.md}_fixed.md\"" \
        config/default.json > config/temp.json
    
    # Process
    python -m cli.main config/temp.json
done
```

---

## Debugging конфигурации

### Verbose mode

```json
{
  "verbose": true
}
```

Показывает больше деталей при обработке.

### Сохранение промежуточных результатов

```json
{
  "save_intermediate": true,
  "intermediate_dir": "debug_fixes"
}
```

Проверьте `debug_fixes/batch_0001.json` чтобы увидеть, что GPT возвращает.

### Тест на одном батче

```bash
# Создайте маленький файл (10 строк)
head -n 10 transcript.md > test_small.md

# Обработайте
python -m cli.main
```

Быстрая проверка качества исправлений.

