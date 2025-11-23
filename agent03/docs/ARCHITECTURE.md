# 🏗️ Agent03 - Архитектура

## Clean Architecture

Проект построен на принципах **Clean Architecture** с четким разделением на слои.

## 📁 Структура проекта

```
agent03/
├── core/                           # 🎯 DOMAIN LAYER
│   ├── __init__.py
│   ├── models.py                   # Модели данных
│   └── config.py                   # Конфигурация
│
├── services/                       # 🔧 APPLICATION LAYER
│   ├── __init__.py
│   ├── api_client.py               # OpenAI API клиент
│   └── pipeline.py                 # Главный оркестратор
│
├── infrastructure/                 # 🔨 INFRASTRUCTURE LAYER
│   ├── __init__.py
│   ├── audio/                      # Аудио обработка
│   │   ├── __init__.py
│   │   ├── utils.py                # ffmpeg/ffprobe утилиты
│   │   └── chunker.py              # Разделение на чанки
│   ├── cache/                      # Кеширование
│   │   ├── __init__.py
│   │   └── manager.py              # Cache manager
│   └── io/                         # Ввод-вывод
│       ├── __init__.py
│       └── writers.py              # Форматирование вывода
│
├── cli/                            # 🖥️ INTERFACE LAYER
│   ├── __init__.py
│   └── main.py                     # CLI entry point
│
├── tests/                          # 🧪 Тесты
│   ├── __init__.py
│   ├── conftest.py
│   ├── test_core.py
│   └── test_infrastructure.py
│
├── docs/                           # 📚 Документация
│   ├── ARCHITECTURE.md             # Этот файл
│   └── QUICK_START.md
│
├── examples/                       # 📖 Примеры
│   └── basic_examples.py
│
├── config/                         # ⚙️ Конфигурация
│   └── default.json
│
├── setup.py                        # Setup script
├── pyproject.toml                  # Modern Python config
├── requirements.txt                # Dependencies
├── MANIFEST.in                     # Package manifest
├── LICENSE                         # MIT License
└── README.md                       # Главная документация
```

## 🎯 Архитектурные слои

### 1. Domain Layer (core/)

**Не имеет внешних зависимостей**

```python
core/
├── models.py       # ASRSegment, ChunkInfo, TranscriptionResult
└── config.py       # Config
```

**Ответственность:**
- Определение бизнес-моделей
- Правила домена
- Конфигурация приложения

**Принцип:** Чистая бизнес-логика без зависимостей от технических деталей.

### 2. Application Layer (services/)

**Зависит только от Domain**

```python
services/
├── api_client.py   # OpenAITranscriptionClient
└── pipeline.py     # TranscriptionPipeline
```

**Ответственность:**
- Use cases (сценарии использования)
- Бизнес-логика приложения
- Оркестрация между слоями

**Принцип:** Координация работы системы, реализация бизнес-процессов.

### 3. Infrastructure Layer (infrastructure/)

**Реализует интерфейсы, определенные в Domain**

```python
infrastructure/
├── audio/
│   ├── utils.py      # AudioUtils
│   └── chunker.py    # AudioChunker
├── cache/
│   └── manager.py    # CacheManager
└── io/
    └── writers.py    # OutputWriter
```

**Ответственность:**
- Работа с внешними системами (ffmpeg, файловая система)
- Конкретные реализации
- Технические детали

**Принцип:** Изоляция технических деталей от бизнес-логики.

### 4. Interface Layer (cli/)

**Точка входа в систему**

```python
cli/
└── main.py         # CLI interface
```

**Ответственность:**
- Command-line интерфейс
- Парсинг аргументов
- Запуск приложения

**Принцип:** Способ взаимодействия пользователя с системой.

## 🔗 Граф зависимостей

```
┌─────────────────────────────────────────────────┐
│              CLI (cli/main.py)                   │
│           Interface Layer                        │
└────────────────────┬────────────────────────────┘
                     │ uses
                     ▼
┌─────────────────────────────────────────────────┐
│        Services (api_client, pipeline)           │
│          Application Layer                       │
└────────────────────┬────────────────────────────┘
                     │ uses
                     ▼
┌─────────────────────────────────────────────────┐
│          Core (models, config)                   │
│            Domain Layer                          │
│         NO DEPENDENCIES                          │
└─────────────────────────────────────────────────┘
                     ▲
                     │ implements
┌─────────────────────────────────────────────────┐
│  Infrastructure (audio, cache, io)               │
│      Infrastructure Layer                        │
└─────────────────────────────────────────────────┘
```

## 🔄 Поток данных

```
1. Входной файл (audio.m4a)
           │
           ▼
2. Config загружает настройки
           │
           ▼
3. Конвертация m4a → WAV (если включено)
           │
           ├─► audio.wav (16kHz, mono, PCM)
           │   Сохраняется в: converted_wav/
           │
           ▼
4. AudioChunker разделяет на части (если нужно)
           │
           ├─► chunk_001.wav (offset=0s, guard=0s)
           ├─► chunk_002.wav (offset=58s, guard=2s)
           └─► chunk_003.wav (offset=116s, guard=2s)
           │
           ▼
5. CacheManager проверяет кеш
           │
           ├─► Cache HIT  ──► Берём из кеша
           └─► Cache MISS ──┐
                            ▼
6. APIClient транскрибирует
                            │
                            ▼
7. CacheManager сохраняет в кеш
                            │
                            ▼
8. Промежуточное сохранение результатов (если включено)
                            │
                            ├─► intermediate_results/
                            │   audio_chunk_000_result.json
                            │   audio_chunk_001_result.json
                            │   ...
                            ▼
9. APIClient парсит сегменты
                            │
                            ▼
10. OutputWriter форматирует
                            │
                            ▼
11. Выходные файлы
    ├─► transcript.md
    ├─► openai_response.json
    ├─► converted_wav/ (если включена конвертация)
    └─► intermediate_results/ (если включено промежуточное сохранение)
```

## 📊 Компоненты

### Core Layer

| Файл | Класс/Функция | Описание |
|------|---------------|----------|
| `models.py` | ASRSegment | Сегмент транскрипции |
| | ChunkInfo | Информация о чанке |
| | TranscriptionResult | Результат транскрипции |
| `config.py` | Config | Управление конфигурацией |

### Services Layer

| Файл | Класс | Описание |
|------|-------|----------|
| `api_client.py` | OpenAITranscriptionClient | API клиент с retry логикой |
| `pipeline.py` | TranscriptionPipeline | Главный оркестратор |

### Infrastructure Layer

| Файл | Класс | Описание |
|------|-------|----------|
| `audio/utils.py` | AudioUtils | ffmpeg утилиты + конвертация в WAV |
| `audio/chunker.py` | AudioChunker | Разделение на чанки с overlap |
| `cache/manager.py` | CacheManager | Кеширование по fingerprint |
| `io/writers.py` | OutputWriter | Markdown/JSON форматирование |

## 🎨 Принципы SOLID

### Single Responsibility Principle (SRP)
✅ Каждый модуль отвечает за одну задачу:
- `models.py` - только модели данных
- `api_client.py` - только работа с API
- `chunker.py` - только разделение аудио

### Open/Closed Principle (OCP)
✅ Легко расширять без изменения существующего кода:
```python
# Добавить новый формат вывода
class SRTWriter(OutputWriter):
    pass

# Добавить новый API провайдер
class AssemblyAIClient:
    pass
```

### Liskov Substitution Principle (LSP)
✅ Можно заменять реализации:
```python
# Можно заменить OpenAITranscriptionClient на другую реализацию
pipeline.api_client = MyCustomClient()
```

### Interface Segregation Principle (ISP)
✅ Маленькие, специфичные интерфейсы:
- AudioUtils - только утилиты
- CacheManager - только кеш
- OutputWriter - только вывод

### Dependency Inversion Principle (DIP)
✅ Зависимости через абстракции:
- Services зависят от Core, а не от Infrastructure
- Infrastructure реализует интерфейсы Core

## 🔧 Расширение системы

### Добавить новый формат вывода

```python
# agent03/infrastructure/io/srt_writer.py
class SRTWriter:
    """Экспорт в SRT субтитры."""
    
    def write_srt(self, segments, output_path):
        with open(output_path, 'w') as f:
            for i, seg in enumerate(segments, 1):
                start = self._format_time(seg.start)
                end = self._format_time(seg.end)
                f.write(f"{i}\n{start} --> {end}\n{seg.text}\n\n")
```

### Добавить новый API провайдер

```python
# agent03/services/assemblyai_client.py
class AssemblyAIClient:
    """Клиент для AssemblyAI API."""
    
    def transcribe(self, audio_path, **kwargs):
        # Ваша реализация
        pass
    
    @staticmethod
    def parse_segments(response):
        # Парсинг ответа
        pass
```

### Добавить параллельную обработку

```python
# agent03/services/parallel_pipeline.py
from concurrent.futures import ThreadPoolExecutor

class ParallelTranscriptionPipeline(TranscriptionPipeline):
    def process_chunks_parallel(self, chunks, max_workers=4):
        with ThreadPoolExecutor(max_workers=max_workers) as executor:
            futures = [executor.submit(self._process_chunk, c) for c in chunks]
            return [f.result() for f in futures]
```

## 🧪 Тестирование

### Структура тестов

```
tests/
├── test_core.py              # Тесты Domain Layer
├── test_infrastructure.py    # Тесты Infrastructure Layer
└── conftest.py               # Fixtures
```

### Пример теста

```python
def test_asr_segment():
    seg = ASRSegment(0.0, 2.5, "Hello", "speaker_1")
    assert seg.start == 0.0
    assert seg.text == "Hello"
```

## 📊 Метрики

| Метрика | Значение |
|---------|----------|
| Слоев | 4 |
| Модулей | 8 |
| Строк кода | ~890 |
| Циклических зависимостей | 0 |
| Test coverage | TBD |

## 🎓 Best Practices

### ✅ DO
- Импортируйте из верхних слоев вниз
- Держите Domain Layer чистым (без зависимостей)
- Тестируйте каждый слой отдельно
- Используйте dependency injection

### ❌ DON'T
- Не импортируйте CLI в Core
- Не смешивайте Infrastructure и Domain
- Не добавляйте бизнес-логику в Infrastructure
- Не создавайте циклические зависимости

---

**v2.1.0** - Enhanced with M4A→WAV Conversion & Intermediate Saves 🚀
