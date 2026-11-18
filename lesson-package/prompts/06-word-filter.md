# Pass 6 — the filter of the words pass 3 marked as taught

- **Asked:** 20 words to a request: every word pass 3 marked as taught, before it enters the list of new words.
- **Model:** `gpt-4.1-mini` (a cheap model is enough), temperature 0, up to 2500 tokens of answer.
- **Messages:** system = the first text below; user = the second text, `%s` = for every word its number, `es`, `ru` and the place.
- **Answer:** `items[{i, spanish, form: dictionary|plural|verb_form, lemma, about: word|grammar|passing}]`.
- **What the code does with it:** a word that is not Spanish, a verb form standing as an example of grammar and a word only said in passing do not enter the list; `lemma` replaces a form that is not the dictionary one.

## System

```text
Ты проверяешь записи учебного словаря, собранные по записи урока испанского языка для русскоязычного ученика. Запись урока сделана автоматически, ученик ошибается в словах. Отвечай только о том, о чём спрошено.
```

## Text

```text
Задача. Ниже — записи словаря: испанское слово или выражение (es), его значение по-русски (ru) и место из записи урока, откуда запись взята. Для каждой записи ответь о самом es:
- spanish — true, если es есть в испанском языке: слово или выражение, которое найдётся в словаре (в любой стране, где говорят по-испански, включая прижившиеся заимствования); false, если это слово другого языка (английское, французское), несуществующая или ошибочная форма (оговорка ученика, ошибка распознавания) или обрывок.
- form — в какой форме стоит es: dictionary — словарная форма (инфинитив, в том числе возвратный; существительное или прилагательное в единственном числе; наречие) или устойчивое выражение; plural — существительное или прилагательное во множественном числе либо в женском роде; verb_form — личная форма глагола (será, vive, tomamos, habías), герундий либо причастие, которое в показанном месте стоит в составе времени глагола (he visto, había dicho).
- lemma — словарная форма es. Если spanish = false и ясно, какое испанское слово имелось в виду, — это слово; если неясно — пустая строка.
- about — о чём показанное место: word — там говорят о значении или употреблении самого этого слова (переводят, объясняют, спрашивают о нём, поправляют его, подсказывают); grammar — слово стоит как пример на грамматику, говорят о его форме, а не о значении; passing — слово просто употреблено.
Верни по одной записи на каждую, с её номером i.

Записи:
%s
```
