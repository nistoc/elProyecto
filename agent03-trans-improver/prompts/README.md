# 📝 Кастомные промпты

Эта папка содержит промпты для Agent03.

## Файлы

### `default.txt`
Стандартный промпт, используемый по умолчанию. Сбалансированный и проверенный.

### `custom_example.txt`
Пример кастомного промпта на русском языке с более подробными инструкциями.

## Как использовать свой промпт

### 1. Создайте файл с промптом

```bash
cp prompts/custom_example.txt prompts/my_prompt.txt
vim prompts/my_prompt.txt  # отредактируйте
```

### 2. Укажите в конфиге

`config/default.json`:
```json
{
  "prompt_file": "prompts/my_prompt.txt"
}
```

### 3. Запустите

```bash
python -m cli.main
```

Агент загрузит ваш промпт и использует его для всех batch.

## Плейсхолдеры

В промпте вы можете использовать:

- **`{context}`** - заменяется на последние строки из предыдущего batch
- **`{batch}`** - заменяется на текущие строки для обработки

### Пример:

```
Вот контекст:
{context}

Исправь эти строки:
{batch}
```

Будет заменено на:

```
Вот контекст:
```
- 72.12 speaker_0: "vale, por favor"
- 75.82 speaker_1: "es que no"
```

Исправь эти строки:
```
- 80.15 speaker_0: "аста луего"
- 82.20 speaker_1: "entonces"
```
```

## Советы по написанию промптов

### ✅ Хорошие практики

1. **Будьте конкретны**
   ```
   ❌ "Исправь испанские слова"
   ✅ "Замени испанские слова, записанные кириллицей, на латиницу"
   ```

2. **Приведите примеры**
   ```
   "вале" → "vale"
   "пор фавор" → "por favor"
   ```

3. **Укажите что НЕ делать**
   ```
   - НЕ переводи русские слова
   - НЕ меняй форматирование
   - НЕ добавляй объяснения
   ```

4. **Укажите формат вывода**
   ```
   "Верни ТОЛЬКО исправленные строки, без markdown"
   ```

5. **Разрешите гибкость форматирования**
   ```
   GPT может объединять строки одного спикера для улучшения читаемости.
   Это нормальное поведение, не требуйте точного количества строк.
   ```

### ❌ Что избегать

- Слишком длинные промпты (>1000 слов)
- Противоречивые инструкции
- Неясные критерии ("исправь плохие слова")
- Отсутствие плейсхолдеров {context} и {batch}
- Жесткие требования к количеству строк (GPT может объединять реплики)

## Тестирование промпта

### Быстрый тест

1. Создайте маленький файл:
   ```bash
   head -n 20 transcript.md > test_small.md
   ```

2. Используйте ваш промпт:
   ```json
   {
     "input_file": "test_small.md",
     "prompt_file": "prompts/my_prompt.txt"
   }
   ```

3. Запустите и проверьте результат:
   ```bash
   python -m cli.main
   cat transcript_fixed.md
   ```

### A/B тестирование

Сравните результаты разных промптов:

```bash
# Тест 1: default
python -m cli.main  # с prompt_file: "prompts/default.txt"
mv transcript_fixed.md result_default.md

# Тест 2: custom
# Измените prompt_file на "prompts/my_prompt.txt"
python -m cli.main
mv transcript_fixed.md result_custom.md

# Сравните
diff result_default.md result_custom.md
```

## Примеры промптов

### Минималистичный

```
Fix Cyrillic Spanish → Latin.
Keep format.

Context: {context}
Fix: {batch}

Return fixed lines only.
```

### Детальный

```
РОЛЬ: Эксперт по испанской транскрипции
ЗАДАЧА: Исправить кириллические испанские слова в латиницу

ПРАВИЛА:
1. Только испанские слова
2. Не трогать русский текст
3. Сохранить формат: - TIME speaker_N: "text"
4. Вернуть то же количество строк

ПРИМЕРЫ:
[список примеров]

КОНТЕКСТ: {context}
BATCH: {batch}

ВЫВОД: Только исправленные строки.
```

### С акцентом на качество

```
You are an expert Spanish linguist fixing transcription errors.

GOAL: Replace Cyrillic transcriptions with correct Spanish spelling.

QUALITY STANDARDS:
- 100% accuracy in Spanish spelling
- Preserve all Russian text unchanged
- Maintain exact line formatting
- Keep speaker labels and timestamps intact

CONTEXT (for understanding dialog flow):
{context}

LINES TO FIX:
{batch}

OUTPUT: Return only the corrected lines, same count as input.
```

## Версионность

Рекомендуем версионировать промпты:

```
prompts/
├── default.txt
├── v1_minimal.txt
├── v2_detailed.txt
├── v3_quality_focused.txt
└── my_production.txt
```

Это позволит:
- Откатываться к предыдущим версиям
- Тестировать изменения
- Делиться с командой

## Отладка

Если промпт не работает:

1. **Проверьте плейсхолдеры**
   ```bash
   grep "{context}" prompts/my_prompt.txt
   grep "{batch}" prompts/my_prompt.txt
   ```

2. **Посмотрите что отправляется в API**
   
   Добавьте в `services/fixer.py` временный print:
   ```python
   def _build_prompt_from_template(self, batch_info):
       # ...
       print("[DEBUG] Full prompt:")
       print(prompt)
       print("=" * 60)
       return prompt
   ```

3. **Проверьте промежуточные результаты**
   ```bash
   cat intermediate_fixes/batch_0001_of_0110.json | jq .
   ```

## Поддержка

Если ваш промпт не дает желаемых результатов:

1. Начните с `default.txt`
2. Делайте небольшие изменения
3. Тестируйте на маленьких файлах
4. Итеративно улучшайте

---

💡 **Совет**: Сохраняйте успешные промпты с описанием, когда они работают лучше всего!

