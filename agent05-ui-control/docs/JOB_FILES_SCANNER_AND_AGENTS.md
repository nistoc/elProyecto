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

Кэш транскрипции: каталог **`cache/`** (из конфига) **внутри папки задания** (рядом с аудио), если так настроен пайплайн — сканер **не выделяет** отдельной категорией; файлы там не попадают в секции UI, если лежат только под `cache/`.

Сигналы отмены чанков: каталог **`.agent04_chunk_cancel/<внутренний job id Agent04>`** под **корнем артефактов задания** (та же папка, что и входной аудиофайл). Пока процесс Agent04 жив, база отмены также резолвится по **in-process registry** `agent04JobId → artifactRoot`. После рестарта без registry клиент обязан передать в gRPC **`job_directory_relative`** (= сегмент пути к папке задания под общим workspace, у Xtract — id задания). Не путать с устаревшим глобальным `cancel_signals` в конфиге.

## 3. Что добавляет **Agent06** (refiner)

| Категория сканера | Agent06 | Комментарий |
|-------------------|---------|-------------|
| `transcripts` | Да | При совпадении **workspace** с каталогом заданий: **`transcript_fixed.md`** и при необходимости нумерованные варианты (`transcript_fixed_1.md`, …) — попадают под правило «имя содержит transcript» / `.md`. |
| Остальные | Обычно нет | Refiner не наполняет `chunks/`, `split_chunks/` и т.д. |

Пайплайн agent05 передаёт в gRPC **`job_directory_relative`** (= id задания) и **`output_file_path`** `transcript_fixed.md`, когда выход в координатах папки job; для старых версий Agent06 без поля — по-прежнему возможен путь `{jobId}/transcript_fixed.md` от корня workspace. В типичном деплое **`Jobs:WorkspacePath`** и **`Agent06:WorkspaceRoot`** должны указывать на **один и тот же** каталог `runtime` (проверка при старте API — см. README).

## 4. UI: SSE, список файлов и RENTGEN

- **Снимок задания (SSE `snapshot`)** несёт `chunks.total`, `chunkVirtualModel` и прочие поля `JobSnapshot`, но **не заменяет** ответ **`GET /api/jobs/{id}/files`**. Список файлов на диске подтягивается отдельным HTTP-запросом; после сплита чанков панель файлов обновляется при новом snapshot (счётчик ревизии в UI) или вручную (**«Обновить»** / смена шага).
- **RENTGEN** в Agent04 — это внутренняя виртуальная модель узлов (`INodeModel`); Xtract **не подписан** на отдельные события RENTGEN. То, что видит UI по чанкам, приходит в **`chunk_virtual_model`** внутри gRPC-статуса и далее в JSON snapshot.

## 5. Сводка расхождений UI и диска

- Секция **Split chunks** в UI будет **пустой** для «чистого» прогона **только Agent04 + Agent06** через XtractManager, пока никто не создаёт `split_chunks/chunk_*`.
- Индекс чанка в UI берётся из **имени файла** в `chunks/` (первое число), а не из внутреннего состояния gRPC; при нетипичных именах без цифр `index` может быть `null`.
- Корневые **любые** `.json` (в т.ч. `openai_response.json`) относятся к **`transcripts`**, не к `chunkJson` (тот только для `chunks_json/`).

## 6. Связанные документы

- [CHUNKS_AND_RENTGEN.md](../../agent04/docs/CHUNKS_AND_RENTGEN.md) — чанки, отмена, RENTGEN, отсутствие операторского split в Agent04.
- [PARITY_MINIMUM_READINESS.md](./PARITY_MINIMUM_READINESS.md) — чеклист критериев готовности плана паритета.
