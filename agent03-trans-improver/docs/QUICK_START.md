# 🚀 Quick Start Guide

Быстрое начало работы с Agent03: Transcript Improver.

## За 3 минуты

### 1. Установка (30 сек)

```bash
cd agent03-trans-improver
pip install -r requirements.txt
```

### 2. API ключ (30 сек)

```bash
# Windows PowerShell
$env:OPENAI_API_KEY = "sk-your-key-here"

# Linux/Mac
export OPENAI_API_KEY="sk-your-key-here"
```

### 3. Копируйте файл (30 сек)

```bash
# Копируйте transcript.md из agent01
cp ../agent01/transcript.md .
```

### 4. Запуск! (2 минуты)

```bash
python -m cli.main
```

Готово! Результат будет в `transcript_fixed.md`.

## Что происходит

```
[INFO] Reading transcript from transcript.md
[INFO] Found 1100 content lines to process
[INFO] Total batches: 110

[BATCH 1/110] Processing lines 1-10...
[API] ✓ Fixed 10 lines
[BATCH 2/110] Processing lines 11-20...
[API] ✓ Fixed 10 lines
...
```

## Настройка

Отредактируйте `config/default.json`:

```json
{
  "batch_size": 10,        // Строк за раз
  "context_lines": 3,      // Контекст из предыдущего batch
  "model": "gpt-4o-mini"   // Модель для исправления
}
```

### Рекомендации по batch_size

- **10 строк**: баланс скорости и качества (рекомендуется)
- **5 строк**: медленнее, но дешевле
- **20 строк**: быстрее, но больше токенов

### Выбор модели

| Модель | Скорость | Качество | Цена |
|--------|----------|----------|------|
| **gpt-4o-mini** | ⚡⚡⚡ | ⭐⭐⭐ | 💰 |
| **gpt-4o** | ⚡⚡ | ⭐⭐⭐⭐⭐ | 💰💰💰 |
| **gpt-3.5-turbo** | ⚡⚡⚡⚡ | ⭐⭐ | 💸 |

## Примеры исправлений

Что исправляется:

```diff
- вале → vale
- пор фавор → por favor  
- эске → es que
- кохер → coger
- аста → hasta
- граcias → gracias (уже правильно)
```

## Стоимость

Для файла ~1100 строк:
- **gpt-4o-mini**: ~$0.05-0.10
- **gpt-4o**: ~$0.50-1.00

## Troubleshooting

### API ключ не найден
```bash
echo $OPENAI_API_KEY  # проверьте, что установлен
```

### Файл не найден
```bash
ls transcript.md  # проверьте наличие файла
```

### Ошибки API
- Проверьте баланс на account.openai.com
- Убедитесь что ключ действителен
- Попробуйте `gpt-3.5-turbo` если `gpt-4o-mini` не работает

## Дальше

- [Полная документация](../README.md)
- [Примеры конфигурации](EXAMPLES.md)
- [Архитектура](ARCHITECTURE.md)

