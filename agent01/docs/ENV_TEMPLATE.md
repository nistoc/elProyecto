# 🔑 Настройка переменных окружения

## Создание .env файла

1. Создайте файл `.env` в корне проекта agent01:
```bash
cd agent01
touch .env
```

2. Скопируйте следующий шаблон в `.env`:

```bash
# ========================================
# OpenAI API Key (ОБЯЗАТЕЛЬНО)
# ========================================
# Получить: https://platform.openai.com/api-keys
OPENAI_API_KEY=sk-your-openai-key-here

# ========================================
# HuggingFace Token (ОБЯЗАТЕЛЬНО для v3.0+)
# ========================================
# Требуется для pyannote.audio (диаризация спикеров)
# Получить: https://huggingface.co/settings/tokens
# Требуемый доступ: Read
HUGGINGFACE_TOKEN=hf_your-huggingface-token-here
```

3. Замените `sk-your-openai-key-here` на ваш настоящий OpenAI API ключ
4. Замените `hf_your-huggingface-token-here` на ваш настоящий HuggingFace токен

## Где получить токены

### OpenAI API Key
1. Зайдите на https://platform.openai.com/api-keys
2. Нажмите "Create new secret key"
3. Скопируйте ключ (начинается с `sk-`)

### HuggingFace Token
1. Зарегистрируйтесь на https://huggingface.co/
2. Перейдите в https://huggingface.co/settings/tokens
3. Нажмите "New token"
4. Выберите "Read" access
5. Скопируйте токен (начинается с `hf_`)

## Проверка

Запустите для проверки:
```python
import os
from dotenv import load_dotenv

load_dotenv()

print("OpenAI:", "✅" if os.getenv("OPENAI_API_KEY") else "❌")
print("HuggingFace:", "✅" if os.getenv("HUGGINGFACE_TOKEN") else "❌")
```

> ⚠️ **Важно:** Файл `.env` не попадает в git (уже в `.gitignore`)

