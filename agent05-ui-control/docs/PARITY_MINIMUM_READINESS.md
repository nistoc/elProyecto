# Критерий готовности (минимум) — план паритета UI

Чеклист по плану **agent05 UI parity gaps** (Transcriber / Refiner / Result, файлы, agent04). Статус на момент закрытия глав **§6** и **«Критерий готовности»**.

| Критерий | Статус | Где смотреть |
|----------|--------|----------------|
| **Transcriber:** виден `jobDir`, есть «Обновить» для списка файлов | Выполнено | `ProjectFilesPanel` + `GET .../files` → `jobDir`; кнопка обновления; i18n |
| **Transcriber:** двойной клик по текстовому файлу → редактор | Выполнено | `ProjectFilesPanel` / модалка редактора |
| **Refiner:** тот же уровень метаданных и быстрых ссылок, что **Result**, плюс транскрипты | Выполнено | `App.tsx` вкладка Refiner → `ResultSection variant="refiner"` |
| **Refiner / логи:** отдельный поток или фильтр по фазе | **Не делаем** (зафиксировано) | Общий буфер SSE; см. README, §2 плана |
| **Result:** логи + тот же блок, что Refiner (без фазовых подсказок) | Выполнено | `App.tsx` вкладка Result |
| **resultLinks:** `transcript_fixed_*.md` | Выполнено | `ResultSection` / `buildKeyLinks` |
| **§4.1 Agent04:** снят внешний HTTP API, только gRPC | Выполнено | Agent04 README/CHANGELOG; порт 5032 h2c |
| **ChunkControlPanel:** решение задокументировано, cancel + UI | Выполнено | README Endpoints, `ChunkControlPanel`, §5.6–5.7 плана; `CHUNKS_AND_RENTGEN.md` |
| **§5.7:** аудит чанков, RENTGEN для `ChunkCommand`, тест `chunk-actions` | Выполнено | `agent04/docs/CHUNKS_AND_RENTGEN.md`, `API.Tests/ChunkActionsControllerTests.cs` |
| **§6:** таблица сканера vs agent04/06 | Выполнено | `docs/JOB_FILES_SCANNER_AND_AGENTS.md`, ссылка в README |
| Опционально **не делаем:** RefiningTextPreview, Start/Skip Refiner | Зафиксирован отказ | README «Опционально из плана…» |

Итог: **минимальный паритет по плану достигнут**; открытые намеренные отличия — только отказные пункты выше и отсутствие операторского `split_chunks` в Agent04 (см. CHUNKS_AND_RENTGEN).
