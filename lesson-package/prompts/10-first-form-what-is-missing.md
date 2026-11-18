# First form, request C — what the first two requests missed

- **Asked:** once per recording, after `08` and `09`.
- **Model:** `gpt-4.1`, temperature 0, up to 6000 tokens of answer.
- **Messages:** system = `00-system.md` without the words «а ученик ошибается в словах» (the first form was written before they were added); user 1 = the lines; user 2 = the text below, the first `%s` = the words found (new words and vocabulary), the second = the grammar topics found.
- **Answer:** `new_words[...]`, `grammar[...]` of the same shape as in `08` and `09`.
- **Measured:** of twelve misses a person found in one lesson this request caught 3; the passes 3 and 4, which replaced it, catch 9.

## Text

```text
Задача 3. Первый проход по этим же строкам уже нашёл слова и грамматику — список ниже. Назови, чего в списке НЕ ХВАТАЕТ:
- new_words: слова, выражения и термины, которые на уроке объясняли, переводили, о которых спрашивали, в которых поправляли или которые подсказывали, — и которых нет в списке (другая форма слова из списка считается найденной). Поля те же: es, ru, how (explained, asked, corrected, prompted, introduced), line, quote, note.
- grammar: грамматика, о которой шла речь (объясняли, спрашивали, поправили) и которой нет в списке. Поля: topic, rule (правило одной-двумя фразами своими словами), kind (explained, corrected, asked), examples (строка и цитата).
Не повторяй найденное. Обычные слова, о которых на уроке не говорили, не включай. Если пропусков нет — верни пустые списки.

Уже найдено.
Слова: %s
Грамматика: %s
```
