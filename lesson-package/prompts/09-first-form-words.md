# First form, request B — new words and vocabulary

- **Asked:** once per recording, with the same lines.
- **Model:** `gpt-4.1`, temperature 0, up to 12000 tokens of answer.
- **Messages:** system = `00-system.md` without the words «а ученик ошибается в словах» (the first form was written before they were added); user 1 = the lines; user 2 = the text below.
- **Answer:** `new_words[{es, ru, how, line, quote, note}]`, `vocabulary[{es, ru, pos, line, quote}]`.
- **Against pass 2:** no `heard`: a word is written as the line has it, also when the line has it wrong.

## Text

```text
Задача 2. Верни:
- new_words: слова, выражения и термины, которые на этом уроке объясняли, переводили, о которых спрашивали или в которых поправляли. how — что произошло, выбери одно: explained (преподаватель объясняет или переводит слово), asked (ученик спрашивает, что слово значит или как сказать), corrected (ученик ошибся в слове, преподаватель поправил), prompted (ученик запнулся, преподаватель подсказал слово), introduced (преподаватель называет слово в ряду других или вводит его без объяснения). Для каждого: es — слово или выражение в словарной форме; ru — значение: если на уроке прозвучал русский перевод, возьми его, иначе переведи сам; line и quote — строка и цитата, где это видно; note — одна фраза о том, что произошло.
- vocabulary: остальная содержательная лексика урока — существительные, глаголы, прилагательные, наречия и устойчивые выражения, которые прозвучали по-испански и полезны ученику среднего уровня. Без самых частых слов (ser, estar, tener, hacer, bueno, bien, y, pero и подобных), без повторов и без слов из new_words. До 80 записей. Для каждого: es — словарная форма; ru — перевод; pos — часть речи (noun, verb, adjective, adverb, expression); line и quote — первое место, где слово прозвучало.
```
