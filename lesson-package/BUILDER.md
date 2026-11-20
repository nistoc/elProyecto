# Lesson package — how the lists are built

The lists of `package.json` are built by models that read the transcripts of a lesson, with code around
them that picks what to ask and checks what comes back. This file tells how; the text of every prompt is in
[prompts/](prompts/).

## State (2026-11-18)

- The builder is a set of Python scripts kept **outside the repository**: they grew by trial on real
  lessons and hold lines of lessons and names of people, and the repository is public. Their output is
  converted to `package.json` by a script that also lies outside.
- The prompts in [prompts/](prompts/) are the builder's own, word for word, but for the few examples that
  were taken from real lessons — those are replaced by made-up ones — and for one phrase of
  `03-word-in-doubt.md` written in other words of the same meaning («стоят под вопросом»). The scripts still
  carry their own copy.
- **Next step**: the builder moves into the repository — cleaned of lesson text and names, reading its
  prompts from `prompts/`, writing `package.json` itself. From then on a prompt is changed in one place.
- Four lessons have a package: three built by the third form described here, one by the first form alone
  (its lists and a request "what is missing"; its words found by code have no `kind`, and its grammar is
  not merged into themes).

## What is read

The dialogue of every recording of the lesson: the merged transcript of the participant tracks (a
dialogue job), each line with its time and speaker. The builder numbers the lines and marks the speaker
as `П` (teacher) or `У` (student); a line goes to a model as `[n] П: text`. Nothing is written into a job
folder.

Since 2026-11-20 agent05 gives this text for every kind of recording in one answer:
`GET /api/sessions/{date}/text` (agent05 README, «Текст урока с говорящими»). Every line has its time, its
speaker and a `role` — `teacher`, `student`, `other` or none. A lesson with participant tracks has the
speakers of its tracks; a lesson recorded as one mixed file gets them from the model that told speakers, or
from the answer of another recognition service lying next to the audio, laid onto our lines by the sound of
the words. The role is the one a person gave (`PUT …/text/speakers`, file `speakers.json` next to the
package), the one a track label names, or a guess by the volume of speech (the teacher says more: 33 of 33
on role-labelled recordings). A builder reading it marks `teacher` as `П`, `student` as `У`, and a line
without a role as `?` — such a line is still read, but a list does not count it as the teacher's
explanation.

## The passes

| # | Pass | Asked | Model | Prompt |
|---|---|---|---|---|
| 1 | topics, grammar spoken of, examples | once per recording | gpt-4.1 | [01](prompts/01-topics-grammar-examples.md) |
| 2 | new words, vocabulary | once per recording | gpt-4.1 | [02](prompts/02-words.md) |
| 3 | "is this word taught here?" about every word the code puts in doubt | 12 words to a request | gpt-4.1 | [03](prompts/03-word-in-doubt.md) |
| 4 | grammar the first pass missed | once per recording | gpt-4.1 | [04](prompts/04-grammar-second-pass.md) |
| 5 | grammar notes of the whole lesson merged into themes | once per lesson | gpt-4.1 | [05](prompts/05-grammar-merge.md) |
| 6 | filter of the words pass 3 marked as taught | 20 words to a request | gpt-4.1-mini | [06](prompts/06-word-filter.md) |
| 7 | what the words found only by code are | 25 words to a request | gpt-4.1-mini, the doubtful ones again by gpt-4.1 | [07](prompts/07-words-by-code.md) |
| A, B, C | **the first form**, still run before all of the above: its own lists and "what is missing" | three requests per recording | gpt-4.1 | [08](prompts/08-first-form-topics-grammar-examples.md), [09](prompts/09-first-form-words.md), [10](prompts/10-first-form-what-is-missing.md) |

All requests: the chat completions API, temperature 0, a strict JSON schema for the answer (every field
required, no other fields), the system message [00](prompts/00-system.md) unless the prompt file names its
own. Every answer is stored as it came; an answer that is stored is not asked again, so a build that
stopped goes on from where it was and a rebuild after a change of code costs nothing.

### The order of a build

The passes grew one on another, so a build runs them in this order (the scripts as they are today, each
with a dry run that asks nothing and prints the estimate of what is left):

1. the first form — requests A, B, C for every recording;
2. passes 1 and 2 for every recording;
3. pass 3 — the code picks the words in doubt from the lists of step 2 and the earlier lessons;
4. pass 4 for every recording;
5. pass 5 — once, over the grammar notes of passes 1 and 4 **and of the first form**;
6. pass 6 — over the words pass 3 marked as taught **and the new words only the first form has**;
7. the assembling step (free) — it also writes the list of the words found only by code;
8. pass 7 over that list, then the assembling step once more (free): `package.json`.

What the first form still gives a build: on the three lessons 7 to 14 of the new words (the ones no later
list has and the filter lets in) and 24–25 of the 39 to 53 grammar notes that go into the merge. It is 9
of the 61–68 requests and 35–39 % of the price (see "What is weak", 6).

## What the code does around the models

- **Checks every quote.** An entry names a line and quotes it. The quote is looked up in that line: found
  as it is (`exact`), found by its letters alone — the signs between them differ (`close`), found by its
  letters in another line, the nearest to the named one (`other`), not found (`none`). The time, the
  speaker and the whole line of an entry come from the line the quote was found in, not from the model.
- **Picks the words in doubt (pass 3).** A Latin-letter word of the lesson that pass 2 did not list and
  that either stands in a line among Russian words (a word quoted inside Russian speech) or was said in no
  earlier lesson. One candidate per word, with its line and the ends of the lines around it.
- **Lets a word in by the filter (pass 6).** A word pass 3 marked as taught enters the list of new words
  only when pass 6 says it is Spanish, the place is about the word itself (not grammar, not said in
  passing) and — for a verb — it is not a form standing as an example. The dictionary form of pass 6
  replaces a form that is not one.
- **Merges grammar (pass 5).** The notes of passes 1 and 4 of all recordings go into one request. A group
  marked as "about a word, not a rule" goes to `word_notes`. A theme keeps the notes it was merged from as
  its `parts`, each with its examples.
- **Lists the words found only by code.** Every Latin-letter word of the lesson that no list holds and
  that no earlier lesson had at build time, with how many times it was said and its first line. Pass 7
  tells what each is; a word it calls a name is left out of the list.

## Price and time

Measured on three lessons of three recordings each:

| Lines of the lesson | Requests | Price | First form | Passes 1–2 | Pass 3 | Pass 4 | Passes 5–7 |
|---|---|---|---|---|---|---|---|
| 999 | 61 | $0.73 | $0.29 | $0.17 | $0.19 | $0.05 | $0.03 |
| 1219 | 68 | $0.92 | $0.35 | $0.24 | $0.23 | $0.06 | $0.04 |
| 1390 | 67 | $0.86 | $0.30 | $0.23 | $0.23 | $0.06 | $0.04 |

The price follows the words of the answers more than the lines of the lesson (the middle lesson has fewer
lines and costs more): before a build name the estimate with a margin of a quarter. A build takes under
ten minutes. A lesson built by the first form alone took 9 requests and $0.33.

A build is a paid action: the owner is told the estimate before it starts and the cost after
([CLAUDE.md](CLAUDE.md), rule 7). Every script has a dry run that prints the estimate of what is not asked yet.

## What was measured

| What | Result |
|---|---|
| quotes found in the named line | 95–97 % (three lessons of the third form) |
| translations with a real defect (checked by a second reader) | about 2 % |
| twelve misses a person found in one lesson of the first form | first form with its second pass caught 3; the passes here catch 9 |
| words pass 3 marks as taught that really were | about 61 %; after the filter of pass 6 — 73 % |
| grammar themes | with pass 4 the section got back the rules a single pass lost: 20 and 11 themes on the two lessons |

### The first form taken out (2026-11-18)

The three lessons of the third form were assembled again from their stored answers in two other ways:
without the first form, and with its request A alone. Only what the change touches was asked again — the
merge, the last requests of the filter, the few more words found by code: 12 requests, $0.09. Sums over the
three lessons:

| | With the first form | Without it | Request A alone |
|---|---|---|---|
| Price of a build | $0.73–0.92 | $0.43–0.57 | $0.52–0.69 |
| New words | 413 | 381 | 381 |
| Notes in the grammar themes | 117 | 67 | 98 |
| Examples of the grammar themes | 142 | 93 | 127 |
| Notes about words (`word_notes`) | 26 | 2 | 23 |

- **Words.** Of the 32 entries lost, five are words no earlier lesson has, and three of the five come back
  in the list of the words found by code: the default view of the screen loses two rows in three lessons.
  Of the twelve known misses of one lesson the lists catch 8 instead of 9; the ninth is one of the three.
  Vocabulary, topics and examples do not change — the first form gives them nothing.
- **Grammar.** Nine themes (three a lesson) stood on notes of the first form alone — four from request A,
  five from request C. Request A alone keeps those four, two as themes and two among the notes about
  words. The count of themes is no measure: the merge groups the same notes differently from one run to
  another (20, 18 and 16 themes of one lesson in the three builds) — count notes and examples.
- **The price of the first form by request:** A $0.08–0.12, B (words) $0.15–0.17, C $0.06–0.07. Request B
  is half of it and gives the least. Request C is asked with the lists of B, so as it is written it cannot
  stay without B.

## What is weak

1. **Pass 3 is right about three words of four.** A word standing inside a phrase that was translated
   whole is still taken for a taught word now and then. The owner takes such rows out by hand on the
   screen; a better prompt or a second opinion would spare that.
2. **Misheard words enter the lists as words.** The transcript has errors of recognition; a model cannot
   always tell. The screen lets the owner correct the spelling; the correction does not reach the
   transcript.
3. **"New" at build time and "new" on the screen are two counts.** The builder picks candidates against
   the earlier lessons it knew when it ran; the screen counts against all earlier lessons on every read.
   The screen is the one that is right; a word the builder skipped as "said before" by its own rule is not
   in the package at all.
4. **A lesson spoken mostly in Spanish fills the grammar section with word meanings** unless pass 5 sorts
   them out — look at `word_notes` after a build.
5. **Themes are named by the model** (`theme`, Spanish, as in a textbook's contents); the same theme may
   get two names in two lessons. Nothing ties the themes of different lessons together yet.
6. **The first form is still paid for in every build** — 35–39 % of the price. Measured (see "The first
   form taken out"): its request about words is half of that and adds about ten entries a lesson, nearly
   all of them words said before; its request A adds most of what the grammar gains from it. Which of its
   requests stay is the owner's decision — it changes what a package holds.

## Changing a prompt

1. Change the file in `prompts/`. Keep the three rules of the system message that make the answer
   checkable: only what the lines hold; a quote letter for letter from one line; no entry without a line.
2. A model does what a long "never do" list names. When a prompt has to keep a model from something, say
   what the right entry looks like, with the wrong case as its opposite, as the prompts here do.
3. Check on a lesson that already has stored answers: only the changed pass is asked again. Write here
   what was measured before and after, with the date.
4. A change that adds a field to the answer is a change of the JSON schema in the builder and, when the
   field reaches the package, of [CONTRACT.md](CONTRACT.md).
