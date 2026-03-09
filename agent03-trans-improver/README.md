# 🔧 Agent03: Transcript Improver

Исправление кириллической транслитерации испанских слов в транскриптах с помощью GPT.

## Что делает

Преобразует: `"вале, пор фавор, эске"` → `"vale, por favor, es que"`

Agent03 исправляет транскрипции, где испанские слова были записаны кириллицей. Использует GPT с контекстом из предыдущих строк для точного исправления.

## Быстрый старт

```bash
# 1. Установка
cd agent03-trans-improver
pip install -r requirements.txt

# 2. API ключ (выберите способ)

# Способ A: .env файл (рекомендуется)
cp env.example .env
# Отредактируйте .env и впишите свой ключ

# Способ B: Переменная окружения
export OPENAI_API_KEY="sk-..."  # Linux/Mac
$env:OPENAI_API_KEY = "sk-..."  # Windows

# 3. Копируйте файл
cp ../agent01/transcript.md .

# 4. Запуск
python -m cli.main

# 5. Результат в transcript_fixed_YYYY-MM-DD_HH-MM-SS.md ✓
```

## Ключевые особенности

- ✅ **Контекстное исправление** - каждый batch получает контекст из предыдущего
- ✅ **Умное форматирование** - GPT может объединять реплики одного спикера для читаемости
- ✅ **Настраиваемая обработка** - размер batch, количество контекста, модель GPT
- ✅ **Промежуточные результаты** - сохранение прогресса для восстановления (автоочистка при запуске)
- ✅ **Прогресс в реальном времени** - отображение статуса обработки

## Конфигурация

Отредактируйте `config/default.json`:

```json
{
  "input_file": "transcript.md",
  "output_file": "transcript_fixed.md",
  "model": "gpt-4o-mini",
  "batch_size": 10,
  "context_lines": 3,
  "prompt_file": "prompts/default.txt",
  "add_timestamp_to_output": true
}
```

**Основные параметры:**
- `batch_size` - строк за один запрос (5-30)
- `context_lines` - строк контекста из предыдущего batch (1-5)
- `model` - `gpt-4o-mini` / `gpt-4o` / `gpt-3.5-turbo`
- `prompt_file` - путь к файлу с кастомным промптом (опционально)
- `add_timestamp_to_output` - добавить дату-время к имени файла (`true` / `false`)

### Кастомные промпты

Вы можете написать свой промпт! См. [`prompts/README.md`](prompts/README.md) для деталей.

```bash
# Создайте свой промпт
cp prompts/custom_example.txt prompts/my_prompt.txt
vim prompts/my_prompt.txt

# Укажите в конфиге
# "prompt_file": "prompts/my_prompt.txt"
```

## Стоимость

Для файла ~1100 строк:
- **gpt-4o-mini**: $0.05-0.10 ✅ (рекомендуется)
- **gpt-4o**: $0.50-1.00 (максимальное качество)
- **gpt-3.5-turbo**: $0.02-0.05 (быстро и дешево)

## Документация

- 📖 [Подробная инструкция](docs/USAGE.md) - пошаговое использование
- 📚 [Примеры конфигураций](docs/EXAMPLES.md) - готовые настройки для разных сценариев
- 🏗️ [Архитектура](docs/ARCHITECTURE.md) - устройство системы

## Примеры исправлений

| До | После |
|----|-------|
| `вале` | `vale` |
| `пор фавор` | `por favor` |
| `эске` | `es que` |
| `кохер` | `coger` |
| `аста луего` | `hasta luego` |

## Структура проекта

```
agent03-trans-improver/
├── cli/main.py          # Точка входа
├── config/default.json  # Конфигурация
├── core/                # Config, модели
├── services/fixer.py    # Логика исправления
└── docs/                # Документация
```

## Troubleshooting

**API ключ не найден:**
```bash
echo $OPENAI_API_KEY  # проверьте, что установлен
```

**Файл не найден:**
```bash
ls transcript.md  # убедитесь, что файл скопирован
```

**Плохое качество:** попробуйте `"model": "gpt-4o"` в конфиге

## Лицензия

MIT License - см. [LICENSE](LICENSE)
