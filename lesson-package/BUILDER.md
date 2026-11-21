# Lesson package — how the lists are built

The lists of `package.json` are built by models that read the transcripts of a lesson, with code around
them that picks what to ask and checks what comes back. This file tells how; the text of every prompt is in
[prompts/](prompts/).

## State (2026-11-20)

- The builder is in the repository: [builder/](builder/README.md), `python -m builder build <date>…` from
  `lesson-package/` (Python 3, standard library only). It reads the prompts from [prompts/](prompts/), the
  lesson from agent05 (`GET /api/sessions/{date}/text` — lines with their speaker and role; `GET
  /api/sessions/words?before=` — the forms of the earlier lessons, by whole date) and writes `package.json`
  itself; lessons of a group go in parallel. The models can be answered by OpenAI (paid, with `--max-usd`),
  or by request files that another hand answers (`files`), pass by pass.
- It was ported from the scripts that grew by trial on real lessons and stay outside the repository (they
  hold lines of lessons and names). A replay of the stored answers of the three lessons of the third form
  gives their `package.json` again, field for field, but for two fields of `stats` that were a probe of
  one lesson and are no longer written (`caught`, `keep`). The notes below on "scripts" describe that code;
  the passes and rules are the same.
- The prompts in [prompts/](prompts/) are the old scripts' own, word for word, but for the few examples that
  were taken from real lessons — those are replaced by made-up ones — and for one phrase of
  `03-word-in-doubt.md` written in other words of the same meaning («стоят под вопросом»). A replay does not
  ask anything, so it does not see these changes.
- Fourteen lessons have a package. Ten were built by `builder/` itself on 2026-11-20 with pass 3 answered
  by a Claude subagent (see "What was measured"): three of 2025 recorded as one mixed file, and seven with
  participant tracks, taken through `python -m builder days` from the batch plan to the published package
  in two calls. All ten asked the whole first form (A, B, C); since the owner's pick of 2026-11-20 a build
  asks request A alone unless `--first-form all` is given — a replay of their stored answers needs that
  option to give the same package. Of the four built before by the old scripts, three by the
  third form described here, one by the first form alone
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
| A, B, C | **the first form**, run before all of the above: its own lists and "what is missing"; since 2026-11-20 only A by default (`--first-form a`; `all` asks the three) | one request per recording (three with `all`) | gpt-4.1 | [08](prompts/08-first-form-topics-grammar-examples.md), [09](prompts/09-first-form-words.md), [10](prompts/10-first-form-what-is-missing.md) |

All requests: the chat completions API, temperature 0, a strict JSON schema for the answer (every field
required, no other fields), the system message [00](prompts/00-system.md) unless the prompt file names its
own. Every answer is stored as it came; an answer that is stored is not asked again, so a build that
stopped goes on from where it was and a rebuild after a change of code costs nothing.

### The order of a build

The passes grew one on another, so a build runs them in this order (the scripts as they are today, each
with a dry run that asks nothing and prints the estimate of what is left):

1. the first form — request A for every recording (A, B, C with `--first-form all`);
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
- **Mends an answer cut at its `max_tokens`** (`builder/README.md`, "Modes"). It happened once in about
  forty lessons (2026-11-21, pass 2 of one recording of 2025-12-18): the vocabulary list looped — 403
  entries, 99 different — until the 12000 tokens ran out. The whole entries before the cut, repeats
  dropped, are the answer (34 new words and 65 words of vocabulary, as many as a recording usually gives);
  asking the same at temperature 0 again would loop again.

## Price and time

Measured on three lessons of three recordings each:

| Lines of the lesson | Requests | Price | First form | Passes 1–2 | Pass 3 | Pass 4 | Passes 5–7 |
|---|---|---|---|---|---|---|---|
| 999 | 61 | $0.73 | $0.29 | $0.17 | $0.19 | $0.05 | $0.03 |
| 1219 | 68 | $0.92 | $0.35 | $0.24 | $0.23 | $0.06 | $0.04 |
| 1390 | 67 | $0.86 | $0.30 | $0.23 | $0.23 | $0.06 | $0.04 |

Three lessons of one or two recordings, built by `builder/` on 2026-11-20 with pass 3 answered by a Claude
subagent (sonnet) from the request files — its price is in Claude tokens, about 110–145 thousand a lesson:

| Lines of the lesson | Requests (of them pass 3) | OpenAI price | First form | Passes 1–2 | Pass 3 | Pass 4 | Passes 5–7 |
|---|---|---|---|---|---|---|---|
| 366 (two recordings) | 37 (16) | $0.34 | $0.16 | $0.13 | — | $0.03 | $0.02 |
| 586 | 44 (26) | $0.36 | $0.17 | $0.13 | — | $0.03 | $0.03 |
| 546 | 40 (25) | $0.31 | $0.14 | $0.11 | — | $0.03 | $0.02 |

Seven lessons with participant tracks, built the same way on 2026-11-20 (two or three recordings each; the
subagent read 1.07 million Claude tokens for the seven, about 150 thousand a lesson):

| Lines of the lesson | Requests (of them pass 3) | OpenAI price | First form | Passes 1–2 | Pass 3 | Pass 4 | Passes 5–7 |
|---|---|---|---|---|---|---|---|
| 907 (two recordings) | 37 (18) | $0.51 | $0.26 | $0.18 | — | $0.04 | $0.03 |
| 1275 | 61 (33) | $0.71 | $0.35 | $0.26 | — | $0.06 | $0.04 |
| 1287 | 65 (37) | $0.69 | $0.36 | $0.23 | — | $0.06 | $0.04 |
| 1361 | 64 (35) | $0.68 | $0.32 | $0.25 | — | $0.07 | $0.04 |
| 1386 | 54 (28) | $0.75 | $0.36 | $0.30 | — | $0.06 | $0.03 |
| 1419 | 58 (32) | $0.72 | $0.37 | $0.25 | — | $0.07 | $0.03 |
| 1483 | 63 (36) | $0.73 | $0.36 | $0.27 | — | $0.07 | $0.04 |

The seven cost $4.79 in OpenAI; the estimate named before was $6 with the margin, and the dry run's own
estimate of what was known to be asked, $4.50, was 4 % under what the same requests then cost. With pass 3
taken off OpenAI the first form is about half of what is left.

Four lessons of autumn 2025 (one mixed recording a period, speakers told by voice), built the same way on
2026-11-20 and the first with request A of the first form alone; the subagent read 1.07 million Claude tokens
for the four (177 requests in eight parts of up to three sheets):

| Lines of the lesson | Requests (of them pass 3) | OpenAI price | First form | Passes 1–2 | Pass 3 | Pass 4 | Passes 5–7 |
|---|---|---|---|---|---|---|---|
| 815 | 57 (36) | $0.40 | $0.09 | $0.23 | — | $0.05 | $0.03 |
| 877 | 67 (44) | $0.44 | $0.10 | $0.25 | — | $0.05 | $0.03 |
| 932 | 67 (44) | $0.44 | $0.10 | $0.25 | — | $0.05 | $0.04 |
| 972 | 69 (46) | $0.51 | $0.12 | $0.31 | — | $0.06 | $0.03 |

Request A alone is 22–23 % of the price (the three requests were 35–39 %). The dry run's estimate of what was
known to be asked (the first form and passes 1–2), $1.94, was 28 % over what those requests cost ($1.52); the
four cost $1.91 in all. One of the four was built twice:
a copy of one recording under another name lay in its day folder, and the lesson text gave it twice until
agent05 learned to give such a file once (`8f4fbb9`); the second build took every answer of the other
recordings as stored (all 44 pass-3 requests came out the same byte for byte) and asked only passes 5–7
again ($0.03).

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
| pass 3 answered by a Claude subagent from the request files (`--answer pass3=files`), the same 69 requests and 806 words of two lessons as gpt-4.1 (2026-11-20, no OpenAI) | words marked taught that are right, by the same reader's key: gpt-4.1 97 right / 54 wrong (64 %); **sonnet 112 / 27 (81 %)**; haiku 136 / 70 (66 %, its extra words mostly said in passing — the kind the filter of pass 6 cannot tell). Of the twelve known misses: gpt-4.1 9, sonnet 8, haiku 10. Sonnet's one weak place: a list of words of one suffix the teacher gave on purpose, left as not taught. About 150 thousand Claude tokens a lesson for sonnet; the OpenAI price of the pass ($0.19–0.23 a lesson) is saved. The words outside the key were read by the same person who wrote it, knowing which hand answered |
| the `evidence` of a word sonnet marked as taught, found letter for letter in the lines its request showed (whitespace aside; a code check of the answer files, 2026-11-20) | 470 of 474 on the seven lessons with tracks, 123 of 125 on the three of 2025, 336 of 339 on the four of autumn 2025 |

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
6. **The first form was paid for in every build** — 35–39 % of the price. Measured (see "The first
   form taken out"): its request about words is half of that and adds about ten entries a lesson, nearly
   all of them words said before; its request A adds most of what the grammar gains from it. The owner
   chose on 2026-11-20 to keep request A alone: it is the default of `builder/` since then (`--first-form
   a`); on the first four lessons built so (2026-11-20) the first form came to 22–23 % of the price, $0.09–0.12
   a lesson (see "State"). The fourteen packages built before keep A, B and C.

## Changing a prompt

1. Change the file in `prompts/`. Keep the three rules of the system message that make the answer
   checkable: only what the lines hold; a quote letter for letter from one line; no entry without a line.
2. A model does what a long "never do" list names. When a prompt has to keep a model from something, say
   what the right entry looks like, with the wrong case as its opposite, as the prompts here do.
3. Check on a lesson that already has stored answers: only the changed pass is asked again. Write here
   what was measured before and after, with the date.
4. A change that adds a field to the answer is a change of the JSON schema in the builder and, when the
   field reaches the package, of [CONTRACT.md](CONTRACT.md).
