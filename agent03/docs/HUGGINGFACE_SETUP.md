# 🔑 Настройка HuggingFace для диаризации

## Шаг 1: Создайте токен

1. Откройте: https://huggingface.co/settings/tokens
2. Нажмите **"New token"**
3. Введите название (например, "agent03")
4. Выберите тип: **"Read"**
5. Нажмите **"Generate"**
6. Скопируйте токен (начинается с `hf_`)

## Шаг 2: Получите доступ к моделям (ОБЯЗАТЕЛЬНО!)

Нужно получить доступ к **ТРЕМ** моделям:

### Модель 1: speaker-diarization-3.1
1. Откройте: https://huggingface.co/pyannote/speaker-diarization-3.1
2. Войдите в аккаунт (если не вошли)
3. Найдите кнопку **"Agree and access repository"**
4. Нажмите на нее
5. Подождите 5-10 секунд

### Модель 2: speaker-diarization-community-1
1. Откройте: https://huggingface.co/pyannote/speaker-diarization-community-1
2. Найдите кнопку **"Agree and access repository"**
3. Нажмите на нее
4. Подождите 5-10 секунд

### Модель 3: segmentation-3.0
1. Откройте: https://huggingface.co/pyannote/segmentation-3.0
2. Найдите кнопку **"Agree and access repository"**
3. Нажмите на нее
4. Подождите 5-10 секунд

> ⚠️ **Все три модели обязательны!** Модель 3.1 использует community-1, которая использует segmentation-3.0.

## Шаг 3: Добавьте токен в .env

```bash
# Создайте файл .env в корне проекта agent03/
HUGGINGFACE_TOKEN=hf_ваш_токен_здесь
```

## Шаг 4: Проверьте

```bash
python -m cli.main --config config/default.json
```

Должно начаться скачивание модели (~500MB при первом запуске).

---

## ⚠️ Важные замечания

1. **Токен должен иметь права "Read"** (достаточно базового доступа)
2. **Доступ к моделям** - это разовая процедура, после одобрения доступ остается
3. **Файл `.env` уже в `.gitignore`** - он не будет закоммичен в git
4. **Windows users:** Warnings о `torchcodec` - это нормально, скрипт использует `torchaudio`

---

## ❌ Ошибки

### "403 Client Error" или "gated repo"

**Причина:** Вы не получили доступ к одной или обеим моделям (Шаг 2).

**Решение:** Получите доступ к **ТРЕМ** моделям:
- https://huggingface.co/pyannote/speaker-diarization-3.1
- https://huggingface.co/pyannote/speaker-diarization-community-1
- https://huggingface.co/pyannote/segmentation-3.0

**Проверьте какая модель в ошибке:**
- Если в ошибке упоминается `speaker-diarization-3.1` → получите доступ к ней
- Если в ошибке упоминается `speaker-diarization-community-1` → получите доступ к ней
- Если в ошибке упоминается `segmentation-3.0` → получите доступ к ней
- **Для полной работы нужны все три!**

### "HUGGINGFACE_TOKEN not found"

**Причина:** Токен не добавлен в `.env` или файл не в корне проекта.

**Решение:** 
```bash
# Проверьте что файл .env существует:
cat .env

# Должно быть:
OPENAI_API_KEY=sk-...
HUGGINGFACE_TOKEN=hf_...
```

---

## ✅ Успех!

После успешной настройки вы увидите:

```
[INFO] Loading diarization model: pyannote/speaker-diarization-3.1
Downloading...
[INFO] Diarization model loaded successfully
[INFO] Starting diarization: converted_wav/audio.wav
```

Модель скачается один раз и будет кешироваться для следующих запусков.

---

**v3.0.0** 🎯🚀

