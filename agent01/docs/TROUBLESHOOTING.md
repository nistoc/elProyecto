# 🔧 Troubleshooting Guide

## Common Issues and Solutions

### 1. Server 500 Errors from OpenAI

**Symptoms:**
```
[WARN] Server 500 on model gpt-4o-transcribe-diarize, variant 1. Retry 1/2 in 1s…
[API] Retry attempt 2/3 for model gpt-4o-transcribe-diarize
```

**Причина**: Временные проблемы на стороне OpenAI (перегрузка серверов)

**Решение**:
- ✅ Скрипт автоматически делает **2 retry** с паузами (1s, 2s)
- ✅ После 2 неудачных попыток переходит к следующему chunk
- ✅ Другие chunks продолжают обрабатываться
- ⚠️ Если проблема массовая - подождите 5-10 минут и запустите снова

**Настройка retry** (в `api_client.py`):
```python
# Максимум 2 retry для Server 500
if ... and attempt_no < 2:
    sleep_s = min(2 ** attempt_no, 4)  # Max 4 секунды между попытками
```

**Если нужно изменить**:
- Измените `attempt_no < 2` на `attempt_no < 3` для 3 попыток
- Измените `min(2 ** attempt_no, 4)` для других задержек

---

### 2. Timeout через 15 минут

**Symptoms:**
```
[ERROR] Timeout waiting for chunks to complete
[INFO] 6/8 chunks completed before timeout
[WARN] Some chunks are taking too long - they will be skipped
```

**Причина**: Некоторые chunks обрабатываются дольше 5 минут (обычно из-за Server 500)

**Решение**:
- ✅ Timeout = **5 минут × количество chunks**
- ✅ Завершенные chunks сохраняются
- ✅ Незавершенные chunks пропускаются
- ⚠️ Запустите скрипт снова - пропущенные chunks обработаются

**Настройка timeout**:
```json
{
  "parallel_transcription_workers": 3,  // Меньше workers = меньше нагрузка
  "max_duration_minutes": 10            // Обрабатывать только первые 10 минут аудио
}
```

---

### 3. Chunks обрабатываются очень долго

**Symptoms:**
```
[1:OK][2:OK][3:00:12:33.8][4:00:12:33.8][5:00:12:33.8]
```
Chunks 3, 4, 5 обрабатываются больше 12 минут

**Причина**: 
- Server 500 с retry
- Большой размер chunk
- Перегрузка OpenAI API

**Решение**:

**Вариант 1: Уменьшить размер chunks**
```json
{
  "target_chunk_mb": 1,       // Вместо 2 MB
  "chunk_overlap_sec": 3.0    // Вместо 5.0
}
```

**Вариант 2: Уменьшить параллелизм**
```json
{
  "parallel_transcription_workers": 1  // Последовательная обработка
}
```

**Вариант 3: Ограничить длительность**
```json
{
  "max_duration_minutes": 5  // Обрабатывать только первые 5 минут
}
```

---

### 4. После ошибки файлы неполные

**Это нормально!** При ошибке:

✅ **Сохраняется в cache**:
- Все **завершенные** chunks
- Манифест с fingerprints

✅ **Сохраняется на диск**:
- `intermediate_results/` - результаты каждого chunk
- Частичный `transcript.md` (может быть неполным)

❌ **НЕ сохраняется**:
- Незавершенные chunks
- Финальный `openai_response.json` (если не все chunks готовы)

**Как продолжить**:
```bash
# Просто запустите снова - продолжится с последнего chunk
python -m cli.main
```

---

## Best Practices

### Для длительных задач

1. **Используйте cache**:
```json
{
  "clean_before_run": false  // НЕ очищать cache
}
```

2. **Сохраняйте промежуточные результаты**:
```json
{
  "save_intermediate_results": true,
  "intermediate_results_dir": "intermediate_results"
}
```

3. **Начните с маленького теста**:
```json
{
  "max_duration_minutes": 1,  // Только 1 минута для теста
  "parallel_transcription_workers": 1
}
```

### Для быстрой обработки

```json
{
  "parallel_transcription_workers": 5,  // Больше параллелизма
  "target_chunk_mb": 2,                 // Больше размер chunk
  "clean_before_run": true              // Чистый старт
}
```

### Для надежности

```json
{
  "parallel_transcription_workers": 1,  // Последовательно
  "target_chunk_mb": 1,                 // Маленькие chunks
  "save_intermediate_results": true,    // Сохранять прогресс
  "clean_before_run": false             // Использовать cache
}
```

---

## Emergency Procedures

### Скрипт завис намертво

1. **Закройте терминал**
2. **Убейте процесс**:
   ```bash
   # Windows
   taskkill /F /IM python.exe
   
   # Linux/Mac
   pkill -9 python
   ```

### Очистить все и начать заново

```bash
cd agent01

# Удалить cache
rm -rf cache/

# Удалить промежуточные результаты
rm -rf intermediate_results/

# Удалить chunks
rm -rf chunks/

# Удалить output файлы
rm -f transcript.md openai_response.json
```

### Проверить что скрипт не запущен в фоне

```bash
# Windows
tasklist | findstr python

# Linux/Mac
ps aux | grep python
```

---

## Debugging

### Включить подробные логи

Добавьте print statements в `api_client.py`:

```python
print(f"[DEBUG] Sending request with params: {kwargs}")
print(f"[DEBUG] Response received: {len(str(resp))} bytes")
```

### Проверить API key

```bash
# Windows PowerShell
$env:OPENAI_API_KEY

# Linux/Mac
echo $OPENAI_API_KEY
```

### Тестовый запрос к API

```bash
cd agent01
python test_error_handling.py
```

---

## Getting Help

If problems persist:

1. Check logs in terminal output
2. Check `intermediate_results/` for partial results
3. Try with `"parallel_transcription_workers": 1`
4. Test with small audio file first
5. Verify OpenAI API is working: https://status.openai.com/

