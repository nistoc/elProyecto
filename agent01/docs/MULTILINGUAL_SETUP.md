# Настройка мультиязычного распознавания

## Обзор

Agent01 поддерживает распознавание аудио на нескольких языках, включая случаи, когда спикеры переключаются между языками во время разговора.

## Как работает Whisper с языками

OpenAI Whisper API поддерживает два основных режима:

### 1. **Автоопределение языка** (рекомендуется для мультиязычного аудио)
- Модель сама определяет язык каждого сегмента
- Лучше всего подходит, когда люди переключаются между языками
- Использует параметр `language: null`

### 2. **Фиксированный язык**
- Модель ожидает только один конкретный язык
- Быстрее и точнее для моноязычного контента
- Использует параметр `language: "ru"` (или другой ISO 639-1 код)

## Настройка для русского + испанского

Для аудио, где люди переключаются между **русским и испанским**, используйте:

### config/default.json

```json
{
  "language": null,
  "prompt": "Здравствуйте, как дела? Buenos días, ¿cómo estás? Хорошо, спасибо. Muy bien, gracias."
}
```

### Параметры:

#### `language` (строка или null)
- **null** - автоопределение (рекомендуется для мультиязычного аудио)
- **"ru"** - только русский
- **"es"** - только испанский
- **"en"** - только английский

**Для переключения между языками всегда используйте `null`!**

#### `prompt` (строка или null)
Подсказка для модели, содержащая:
- Примеры фраз на обоих языках
- Специфичную терминологию
- Имена, которые нужно правильно распознать
- Контекст разговора

**Длина:** до 224 токенов (~150-200 слов)

## Примеры prompt для разных сценариев

### Русский + Испанский (общий разговор)
```json
{
  "prompt": "Здравствуйте, как дела? Buenos días, ¿cómo estás? Отлично, спасибо. Muy bien, gracias. Давайте поговорим. Vamos a hablar."
}
```

### Русский + Испанский (бизнес)
```json
{
  "prompt": "Добрый день, коллеги. Buenos días, colegas. Обсудим проект. Discutamos el proyecto. Контракт, договор. Contrato, acuerdo."
}
```

### Русский + Испанский (учеба)
```json
{
  "prompt": "Урок испанского языка. Lección de ruso. Грамматика, vocabulario. Произношение, pronunciación. Как сказать по-испански? ¿Cómo se dice en ruso?"
}
```

### Русский + Испанский + специальные термины
```json
{
  "prompt": "Здравствуйте, buenos días. Иванов, Петрова, García, Martínez. Москва, Madrid, Барселона, Barcelona. Технология blockchain, inteligencia artificial."
}
```

### Только русский (без переключения)
```json
{
  "language": "ru",
  "prompt": "Добрый день, коллеги. Обсудим проект и дальнейшие планы."
}
```

### Только испанский (без переключения)
```json
{
  "language": "es",
  "prompt": "Buenos días, colegas. Discutamos el proyecto y los planes futuros."
}
```

## Коды языков (ISO 639-1)

Наиболее часто используемые:

| Язык | Код | Пример |
|------|-----|--------|
| Русский | `ru` | `"language": "ru"` |
| Испанский | `es` | `"language": "es"` |
| Английский | `en` | `"language": "en"` |
| Китайский | `zh` | `"language": "zh"` |
| Французский | `fr` | `"language": "fr"` |
| Немецкий | `de` | `"language": "de"` |
| Португальский | `pt` | `"language": "pt"` |
| Итальянский | `it` | `"language": "it"` |
| Японский | `ja` | `"language": "ja"` |
| Корейский | `ko` | `"language": "ko"` |

**Полный список:** [ISO 639-1 codes](https://en.wikipedia.org/wiki/List_of_ISO_639-1_codes)

## Советы по оптимизации

### ✅ Делайте:

1. **Для мультиязычного аудио:**
   - Используйте `language: null`
   - Добавьте prompt с примерами на обоих языках
   - Включите специфичные термины и имена

2. **Для моноязычного аудио:**
   - Укажите конкретный язык
   - Добавьте prompt с терминологией
   - Это повысит точность и скорость

3. **Качественный prompt:**
   - Используйте естественные фразы
   - Включайте пунктуацию
   - Добавляйте имена и термины из вашего аудио

### ❌ Не делайте:

1. **Не указывайте конкретный язык** для мультиязычного аудио
   - ❌ `"language": "ru"` для русско-испанского разговора
   - ✅ `"language": null`

2. **Не делайте prompt слишком длинным**
   - Максимум ~200 слов
   - Whisper имеет лимит 224 токена

3. **Не используйте только ключевые слова**
   - ❌ `"prompt": "hello, привет, hola, здравствуйте"`
   - ✅ `"prompt": "Hello, how are you? Привет, как дела? Hola, ¿cómo estás?"`

## Примеры использования

### Пример 1: Интервью на русском и испанском

**config/default.json:**
```json
{
  "file": "interview.m4a",
  "use_diarization": true,
  "language": null,
  "prompt": "Здравствуйте, расскажите о себе. Buenos días, cuéntame sobre ti. Я работаю программистом. Trabajo como programador. Очень интересно! ¡Muy interesante!",
  "workspace_root": "processing_workspaces"
}
```

**Запуск:**
```bash
python -m cli.main --config config/default.json
```

### Пример 2: Урок испанского для русских

**config/default.json:**
```json
{
  "file": "lesson.m4a",
  "use_diarization": true,
  "language": null,
  "prompt": "Сегодня мы изучаем испанский. Hoy estudiamos español. Как сказать 'спасибо'? ¿Cómo se dice 'спасибо'? Gracias. Правильно! ¡Correcto!",
  "workspace_root": "processing_workspaces"
}
```

### Пример 3: Только русский (высокая точность)

**config/default.json:**
```json
{
  "file": "meeting.m4a",
  "use_diarization": true,
  "language": "ru",
  "prompt": "Добрый день, коллеги. Обсудим квартальный отчет и план на следующий период. Иванов, Петрова, Сидоров.",
  "workspace_root": "processing_workspaces"
}
```

## Результат

После обработки в JSON-файле будет указан определенный язык для каждого сегмента:

```json
[
  {
    "speaker": "SPEAKER_00",
    "start": 0.0,
    "end": 5.2,
    "text": "Здравствуйте, как дела?",
    "language": "ru"
  },
  {
    "speaker": "SPEAKER_01",
    "start": 5.5,
    "end": 8.3,
    "text": "Buenos días, muy bien, gracias.",
    "language": "es"
  }
]
```

## Troubleshooting

### Проблема: Whisper путает языки

**Решение:**
- Добавьте более подробный prompt с примерами
- Убедитесь, что `language: null`
- Проверьте качество аудио

### Проблема: Неправильно распознаются имена

**Решение:**
```json
{
  "prompt": "Иванов Сергей, García María, Петрова Елена, Martínez José..."
}
```

### Проблема: Специфичная терминология

**Решение:**
```json
{
  "prompt": "Blockchain, криптовалюта, smart contract, умный контракт, mining, майнинг..."
}
```

## Дополнительная информация

- [OpenAI Whisper API Documentation](https://platform.openai.com/docs/guides/speech-to-text)
- [Whisper Language Support](https://github.com/openai/whisper#available-models-and-languages)
- [Best Practices for Prompts](https://platform.openai.com/docs/guides/speech-to-text/prompting)

## Поддержка

Для вопросов и предложений создавайте issues в репозитории проекта.

