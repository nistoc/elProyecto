# First form, request A — topics, grammar, examples

- **What it is:** the first form of the builder: three requests per recording (`08`, `09`, `10`). Every build still runs it before the passes 1–7: its new words that no later list has go through the filter of pass 6, and its grammar notes go into the merge of pass 5.
- **Asked:** once per recording of the lesson.
- **Model:** `gpt-4.1`, temperature 0, up to 7000 tokens of answer.
- **Messages:** system = `00-system.md` without the words «а ученик ошибается в словах» (the first form was written before they were added); user 1 = `Строки диалога:` and every line as `[n] П|У: text`; user 2 = the text below.
- **Answer:** `topics[{title, summary, from_line, to_line}]`, `grammar[{topic, rule, kind, examples[{line, quote}]}]` (no `theme`), `examples[{line, quote, ru, shows}]`.
- **Against pass 1:** grammar has no theme, and the meaning of a word may be written as a grammar note.

## Text

```text
Задача 1. Верни:
- topics: темы разговора по порядку — о чём говорили. Для каждой: заголовок по-русски, одна фраза о содержании, номера первой и последней строки темы.
- grammar: грамматика, о которой на уроке ШЛА РЕЧЬ: её объясняли, о ней спрашивали или преподаватель поправил ученика. Не включай грамматику, которая просто встречается в речи. Для каждой: topic — тема по-русски; rule — само правило одной-двумя фразами своими словами, так, как его объяснили на уроке (не копируй строку); kind — explained (объясняли), corrected (поправили ошибку), asked (ученик спросил); examples — примеры из урока: строка и цитата.
- examples: испанские фразы, которые на уроке давались как образец, разбирались или повторялись за преподавателем. Для каждой: строка, цитата, перевод на русский, что фраза показывает (одна фраза).
```
