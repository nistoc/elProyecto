# Pass 3 — the question about a word the code puts in doubt

- **Asked:** 12 words to a request. The words are picked by code, not by a model: a Spanish word that stands among Russian words of a line, or that no earlier lesson has, and that pass 2 did not list.
- **Model:** `gpt-4.1`, temperature 0, up to 2500 tokens of answer.
- **Messages:** system = `00-system.md`; user = the text below, `%s` = for every word its number, the word, its line (cut around the word) and the ends of the lines before and after.
- **Answer:** `words[{i, taught, es, ru, how: …|none, evidence}]`.
- **Measured:** about 61 % of the words it marks as taught are right; with the filter of pass 6 — 73 %.

## Text

```text
Задача. Ниже — слова из строк урока, которые стоят под вопросом. Для каждого слова даны его строка и соседние строки. Реши по каждому: УЧАТ ли здесь это слово.
taught = true, только если в показанных строках говорят о САМОМ ЭТОМ слове (или о выражении, в которое оно входит):
- дают перевод или объяснение именно этого слова или выражения;
- спрашивают именно о нём: что оно значит, как сказать;
- поправляют именно его;
- подсказывают его ученику, который запнулся;
- называют его как слово: говорят «слово такое-то», сравнивают с другим словом, говорят, где или кто так говорит.
taught = false:
- слово стоит внутри испанской фразы-примера, которую перевели или разобрали целиком ради конструкции или времени глагола: учат конструкцию, а не это слово;
- слово — форма глагола в примере на грамматику (estaba tomando, llevamos viviendo);
- слово просто употреблено в речи;
- слово — грамматический термин (indefinido, imperfecto, gerundio) или служебное слово.
evidence — слова из показанных строк, которыми именно это слово перевели, объяснили, о нём спросили или его назвали как слово, буква в букву, без пропусков (например: «это окно»; «по-испански будет ventana»; «надо говорить»). Если таких слов в показанных строках нет — taught = false.
Для taught = true: es — ПРАВИЛЬНОЕ испанское слово в словарной форме, а если учат выражение — всё выражение (de hecho, а не hecho); ru — значение ПО-РУССКИ: возьми перевод из строк, если он там есть, иначе переведи сам в значении этих строк; how — explained (объясняют или переводят), asked (ученик спрашивает), corrected (поправили), prompted (подсказали), introduced (назвали как слово без объяснения).
Для taught = false: es, ru, evidence — пустые строки, how — none.
Верни по одной записи на каждое слово, с его номером i.

Слова:
%s
```
