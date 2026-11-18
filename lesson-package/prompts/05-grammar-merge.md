# Pass 5 — grammar notes of the whole lesson merged into themes

- **Asked:** once per lesson, with the notes of passes 1 and 4 of every recording.
- **Model:** `gpt-4.1`, temperature 0, up to 8000 tokens of answer.
- **Messages:** system = `00-system.md`; user = the text below, `%s` = the notes, numbered, as `[theme] topic — rule`.
- **Asked with:** also the grammar notes of the first form (`08`, `10`): in the text every note has its number.
- **Answer:** `groups[{theme, title, rule, ids[], is_grammar}]`; a group with `is_grammar` false goes to `word_notes` of the package, not to `grammar`.
- **Measured:** the sentence about `is_grammar` was first one line ("true for a rule, false when the notes are about the meaning of a word"): on one lesson the model then marked 21 of 29 groups as not grammar. With the sentence below, which names what a rule is, it marked 2 of 14.

## Text

```text
Задача. Ниже — записи о грамматике, найденные в разных частях одного урока. Некоторые говорят об одной и той же теме разными словами. Собери их в темы:
- записи об одном и том же правиле или об одной теме грамматики (например, все записи о subjuntivo; все о согласовании рода и числа) — в одну группу;
- записи о разных правилах не объединяй;
- запись о значении слова или выражения (что слово значит, чем одно слово отличается от другого по смыслу, как слово переводится) не клади в группу правила: для таких записей — свои группы с is_grammar = false;
- каждая запись входит ровно в одну группу; ни одной не теряй.
Для каждой группы: theme — название темы по-испански, строчными, как в оглавлении учебника; title — заголовок по-русски; rule — одно общее правило двумя-четырьмя фразами: всё, что сказано в записях группы, без повторов, своими словами; ids — номера записей группы; is_grammar — true, если записи группы о правиле грамматики: как образуется или употребляется форма. Это время и наклонение глагола и выбор между ними (будущее, плюсквамперфект, subjuntivo), залог, согласование рода и числа, порядок слов, местоимение при глаголе, предлог при глаголе или в конструкции (pensar en, soñar con), сравнение (tan ... como), выбор между ser, estar и haber, словообразование. false — только если записи о том, что слово или выражение значит, как оно переводится или чем по смыслу отличается от другого слова (madrugar — рано вставать; разница между saber и conocer). Запись, в которой названо слово, но речь о его форме или о конструкции с ним, — грамматика.

Записи:
%s
```
