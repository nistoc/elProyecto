# Pass 7 — what the words found only by code are

- **Asked:** 25 words to a request: every Latin-letter word of the lesson that no list holds and no earlier lesson had when the package was built.
- **Model:** `gpt-4.1-mini`; a word it calls Russian or broken, or is not sure of, is shown once more to `gpt-4.1`.
- **Messages:** system = the first text below; user = the second text, `%s` = for every word its number, the word as the transcript has it and the phrase.
- **Answer:** `items[{i, kind: spanish|russian|other|name|broken, lemma, ru, sure}]`; a `name` is left out of the package.

## System

```text
Ты разбираешь слова из автоматической записи урока испанского языка для русскоязычного ученика. На уроке говорят по-испански и по-русски. Распознавание речи ошибается: русское слово оно иногда записывает латинскими буквами (например, «ladno» вместо «ладно»), а испанское может исказить. Отвечай только о том, о чём спрошено.
```

## Text

```text
Задача. Ниже — слова, записанные латинскими буквами, и фраза из записи урока, где слово прозвучало. Для каждого слова ответь:
- kind: spanish — испанское слово в любой форме; russian — русское слово, записанное латинскими буквами; other — слово другого языка (английского, французского и так далее); name — имя человека, название места, фирмы или марки; broken — обрывок или искажение, по которому слово не восстановить.
- lemma: для spanish — словарная форма (инфинитив; существительное или прилагательное в единственном числе) с испанскими знаками ударения; для russian — это слово русскими буквами в той форме, в какой оно стоит во фразе; для other — слово как оно пишется в своём языке; для name и broken — пустая строка.
- ru: для spanish — значение по-русски в том смысле, в каком слово стоит во фразе, одно-три слова; для other — перевод и в скобках язык; для russian, name и broken — пустая строка.
- sure: true, если фраза позволяет ответить уверенно; false, если нет.
Верни по одной записи на каждое слово, с его номером i.

Слова:
%s
```
