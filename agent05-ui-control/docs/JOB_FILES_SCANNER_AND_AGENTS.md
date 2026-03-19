# Сканер файлов задания (`JobProjectFilesScanner`) и агенты 04 / 06

Реализация: [`API/Features/Jobs/Infrastructure/JobProjectFilesScanner.cs`](../API/Features/Jobs/Infrastructure/JobProjectFilesScanner.cs). Категории совпадают с наследием **agent-browser** (структурированный список для UI).

## 1. Категории и правила классификации

| Категория JSON (`files.*`) | Где на диске | Правило / паттерн | Поля индекса |
|----------------------------|--------------|-------------------|---------------|
| `original` | Корень каталога задания | Файлы с расширениями аудио: `.m4a`, `.mp3`, `.wav`, `.ogg`, `.flac` | — |
| `transcripts` | Корень | Имя содержит `transcript` (без учёта регистра), **или** расширение `.md`, **или** `.json` | — |
| `chunks` | `chunks/` | Все файлы папки; **`index`** = первое целое число из имени файла (regex `\d+`) | `index` |
| `chunkJson` | `chunks_json/` | То же извлечение индекса из имени | `index` |
| `intermediate` | `intermediate_results/` | Плоский список файлов | — |
| `converted` | `converted_wav/` | Плоский список (часто WAV после конвертации) | — |
| `splitChunks` | `split_chunks/chunk_<N>/` | Подкаталоги с префиксом `chunk_` и числом `N`; внутри `sub_chunks/` (имена с `_sub_<k>`) и `results/` (файлы `sub_chunk_<k>_result.json`) | `parentIndex` = N, `subIndex` = k |

Тип строки: `kind` = `text` | `audio` | `other` по расширению; для текста считается `lineCount`, для аудио — длительность (TagLib).

## 2. Что создаёт **Agent04** (транскрипция)

Значения по умолчанию из [`agent04/Agent04/config/default.json`](../../agent04/Agent04/config/default.json) (ваш конфиг может отличаться).

| Категория сканера | Agent04 | Комментарий |
|-------------------|---------|-------------|
| `original` | Да | Загруженное аудио в папке задания (agent05 кладёт файл; относительный путь в gRPC). |
| `transcripts` | Да | `transcript.md`, `openai_response.json` (или пути из `md_output_path` / `raw_json_output_path`; при `{jobId}` в шаблоне — файлы в подкаталоге/имени задания). |
| `chunks` | Да (если `pre_split: true`) | `split_workdir` → по умолчанию **`chunks/`**; шаблон имён `chunk_naming`, напр. `{base}_part_%03d.m4a`. |
| `chunkJson` | Да (если `save_per_chunk_json: true`) | **`chunks_json/`** (`per_chunk_json_dir`). |
| `intermediate` | Да (если `save_intermediate_results: true`) | **`intermediate_results/`**. |
| `converted` | Да (если `convert_to_wav: true`) | **`converted_wav/`** (`wav_output_dir`). |
| `splitChunks` | **Нет** | Иерархия **`split_chunks/chunk_N/...`** в текущем пайплайне Agent04 **не создаётся**; это сценарий старого UI/другого оркестратора. См. также [`agent04/docs/CHUNKS_AND_RENTGEN.md`](../../agent04/docs/CHUNKS_AND_RENTGEN.md). |

Кэш транскрипции: каталог **`cache/`** (из конфига) в workspace — сканер **не выделяет** отдельной категорией; файлы там не попадают в секции UI, если лежат только под `cache/`.

Сигналы отмены чанков: per-job каталог (например `.agent04_chunk_cancel/<jobId>/`), не путать с устаревшим глобальным `cancel_signals` в конфиге — см. документацию Agent04.

## 3. Что добавляет **Agent06** (refiner)

| Категория сканера | Agent06 | Комментарий |
|-------------------|---------|-------------|
| `transcripts` | Да | При совпадении **workspace** с каталогом заданий: **`transcript_fixed.md`** и при необходимости нумерованные варианты (`transcript_fixed_1.md`, …) — попадают под правило «имя содержит transcript» / `.md`. |
| Остальные | Обычно нет | Refiner не наполняет `chunks/`, `split_chunks/` и т.д. |

Пайплайн agent05 передаёт в gRPC относительный путь вида `{jobId}/transcript_fixed.md`; корень должен совпадать с `Jobs:WorkspacePath` или с `Agent06:WorkspaceRoot` (см. README основного сервиса).

## 4. Сводка расхождений UI и диска

- Секция **Split chunks** в UI будет **пустой** для «чистого» прогона **только Agent04 + Agent06** через XtractManager, пока никто не создаёт `split_chunks/chunk_*`.
- Индекс чанка в UI берётся из **имени файла** в `chunks/` (первое число), а не из внутреннего состояния gRPC; при нетипичных именах без цифр `index` может быть `null`.
- Корневые **любые** `.json` (в т.ч. `openai_response.json`) относятся к **`transcripts`**, не к `chunkJson` (тот только для `chunks_json/`).

## 5. Связанные документы

- [CHUNKS_AND_RENTGEN.md](../../agent04/docs/CHUNKS_AND_RENTGEN.md) — чанки, отмена, RENTGEN, отсутствие операторского split в Agent04.
- [PARITY_MINIMUM_READINESS.md](./PARITY_MINIMUM_READINESS.md) — чеклист критериев готовности плана паритета.
