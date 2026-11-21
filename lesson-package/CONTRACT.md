# Lesson package — files and API

Two files per lesson, in `<packages folder>/<YYYY-MM-DD>/`:

| File | Written by | Read by |
|---|---|---|
| `package.json` | the builder, once per build | agent05 (never rewritten) |
| `marks.json` | agent05, on every edit of the owner | agent05; the builder may read it |

The packages folder is `LessonPackages:RootPath` of agent05; without the key it is the folder
`lesson-packages` next to the jobs folder (`Jobs:WorkspacePath`). A lesson has a package when its folder
holds `package.json`; a folder whose name is not a date is skipped.

## `package.json`

Names are in snake case. `schema_version` is 1; a file with a greater one is refused (409). A list written as
`null` or left out reads as empty; a field the service does not know is skipped.

```jsonc
{
  "schema_version": 1,
  "lesson": "2026-10-23",              // date of the lesson
  "built_at": "2026-10-24T09:00:00Z",  // UTC
  "builder": "…",                      // what built the lists, for the reader
  "requests": 61,                      // model requests of the build
  "cost_usd": 0.73,
  "periods":    [ { "n": 1, "job": "<job id>", "start": "19:01", "lines": 412, "end_at": "40:01" } ],
  "topics":     [ { "period": 1, "title": "…", "summary": "…", "from_line": 1, "to_line": 30,
                    "from_at": "0:04", "to_at": "3:12", "from_sec": 4.2, "to_sec": 192.0 } ],
  "grammar":    [ { "topic": "…", "theme": "pretérito perfecto", "rule": "…",
                    "kind": "explained|corrected|asked", "period": 1, "points": 3,
                    "examples": [ <place> ],
                    "parts": [ { "topic": "…", "rule": "…", "examples": [ <place> ] } ] } ],
  "word_notes": [ /* the same shape as grammar: notes about the meaning of a word, not a rule */ ],
  "examples":   [ { <place>, "ru": "…", "shows": "…" } ],
  "new_words":  [ { <place>, "es": "…", "ru": "…", "how": "explained|asked|corrected|prompted|introduced",
                    "heard": "…", "note": "…", "source": 1 } ],
  "vocabulary": [ { <place>, "es": "…", "ru": "…", "pos": "noun|verb|adjective|adverb|expression" } ],
  "other_words":[ { "word": "plain form", "raw": "as the transcript has it", "count": 2, "period": 1,
                    "at": "12:40", "sec": 760.1, "who": "teacher", "said": "the whole line",
                    "kind": "spanish|russian|maybe_russian|other|broken|", "lemma": "…", "ru": "…", "sure": true } ],
  "stats": { "…": 0 }
}
```

A **place** is where an item stands in the recording:

```jsonc
{ "period": 1,              // which recording of the lesson (periods[].n)
  "at": "12:34",            // time in that recording; null when the quote was not found
  "sec": 754.3,
  "who": "teacher|student|",
  "line": 187,              // number of the line in the transcript the builder read
  "quote": "…",             // the words the item quotes
  "said": "…",              // the whole line around the quote
  "check": "exact|close|other|none" }  // how the quote was found: in the named line as it is, there by
                            // its letters alone, in another line, not at all
```

`period.job` is the job whose transcript was read (a dialogue job, as a rule). It ties the recording to its
audio: agent05 finds the mixed track of the recording this job belongs to.

## Ids of the items

agent05 gives every item an id each time it reads the package (`LessonPackageViewBuilder.Items`). The id is
made of the builder's own text, never of the edited one and never of the position in the list:

| Item | Id |
|---|---|
| new word | `n-` + slug of `es` |
| vocabulary word | `v-` + slug of `es` |
| word found by code | `o-` + slug of `raw` (of `word` when there is no `raw`) |
| topic | `t-` + 10 hex digits of SHA-1 over `title`, `period`, `from_line` |
| grammar theme | `g-` + digest over `topic`, `rule`, `period` |
| word note | `w-` + the same digest |
| example | `e-` + digest over `quote`, `period`, `line` |
| example of a theme | `<id of the theme>-e-` + digest over `quote`, `period`, `line` |
| example of a part of a theme | `<id of the theme>-p-` + digest over the part's `topic` and `rule` and the example's `quote`, `period`, `line` |

The slug is the text in lower case without accents, every run of other characters a dash, at most 120
characters (`LessonWordRules.Slug`); a text with no Latin letter or digit gives `x`.

Two items that get the same id (the same word twice in one list, as a rule) are told apart by the rest of
their text: **each** of them gets `-` + 6 hex digits of a digest over that rest — a word over `ru`, `period`,
`line`, `quote`; a word found by code over `word`, `period`, `at`, `said`; a topic over `summary`,
`to_line`; a theme over `theme`, `kind`, `points`; an example over `ru`, `shows`; an example of a theme over
`at`, `who`, `said`. So `n-ventana` is the id of a word met once, and `n-ventana-3f9a1c` and
`n-ventana-b07e42` are the ids of two. Only items the same in all of that get `-2`, `-3` by their order —
the one place where the order matters, and there the items cannot be told apart anyway. A word that is
written into a list a second time by a later build therefore changes the id of the first one: its edit
becomes stale (see below), which is the price of ids that do not depend on the order.

## `marks.json`

```jsonc
{
  "n-ventana": {
    "fields": { "ru": "окошко" },    // field -> the owner's text
    "was":    { "ru": "окно" },      // field -> what the builder had, for the reader of the file
    "left_out": false,               // true: the owner took the item out of the lists
    "of": "new_words: ventana",      // which list and item, for the reader
    "at": "2026-10-25T10:00:00Z"
  }
}
```

- Keys are item ids: small Latin letters, digits and dashes, at most 160 characters. An entry under another
  key is not shown but is kept by every write.
- A field the service does not know inside an entry (a comment written by hand, say `"why": "heard wrong"`)
  is kept by every write — also when the owner's edit of that item is taken off: the entry then stays with
  `of` and the hand-written fields only, and the item reads as the builder wrote it.
- An entry whose id the package no longer has (built anew, the text changed) is **stale**: it is counted,
  shown as a number on the screen and left in the file.
- The file is written to a temporary file and moved over, so a reader never meets half of it. A file that
  cannot be read is never written over.

### Fields the owner may edit

| List | Fields (the first one names the item and cannot be emptied) |
|---|---|
| `new_words`, `vocabulary` | `es`, `ru`, `note`, `quote` |
| `other_words` | `raw`, `ru`, `lemma`, `said` |
| `examples` | `quote`, `ru`, `shows` |
| `grammar`, `word_notes` | `topic`, `rule` |
| an example of a theme or of its part | `quote` |
| `topics` | `title`, `summary` |

A text is trimmed, every run of spaces and line breaks becomes one space, and it is at most 2000
characters. A field given as `null`, or given the very text the builder wrote, goes back to the builder's
text. An edit with no field left and `left_out` false is removed from the file, unless the entry holds
fields added by hand (see above).

## What is counted on read

These fields are in the answer of the API only (camel case there), never in `package.json`:

- `id`, `edited` (field → the builder's text), `leftOut`, `editedAt` of every item.
- `seenIn` of every word: the dates of the earlier lessons that have it, oldest first. Empty — no earlier
  lesson has it (the word is **new**). `null` — the entry is a pattern with no word of its own to look for.
  The word is looked for as it reads now, after the owner's edits.
- `heardElsewhere` of every word of the three lists: `true` — another hearing of the same speech has the word;
  `false` — none of the hearings there are has it, the word may not have been said; `null` — nothing to tell by
  (no Latin word of four letters and more, its line not found, no other hearing of the line) or the owner wrote
  the word anew (`es`, `raw`). `counts.notHeardElsewhere` counts the `false` ones that are not taken out.
- `audioTrack` of every period: the id of the mixed track of its recording, or `null`.

How they are counted:

- "Heard elsewhere" (`HeardElsewhere`): the line of the word is the line of the text runs of the recording
  (the tracks of the newest dialogue, else the text run of each track) within 40 s of `sec` that holds at
  least 0.6 of the words of `said` (then of `quote`) in order — its text or the text it had before a second
  look replaced it. The word (`heard` when the builder wrote it, else `es`; `raw` of a word found by code) is
  taken in the form the line has it and looked for by sound (`SaidText.SoundForm`; a letter or two apart, two
  words written as one) in three hearings: what the piece of the line heard alone (`own_text`), what the piece
  asked again with the audio around it gave (`context_text`), and the lines of the newest diarize run of the
  track within 20 s (a participant track with none takes the mixed file's). One hearing that has every word is
  enough for `true`.

- The rule (`LessonWordRules`): words are compared in lower case without accents. A form counts as known
  when the lesson has it, or — for a form of five letters and more — a form with the same beginning but for
  the last two letters (never fewer than four). A single word is also looked for by the form it has in its
  quote and, for a long infinitive, by its stem. A phrase must stand inside one line. A pattern written with
  `+` is looked for by its first word. Small words (articles, prepositions, pronouns) and placeholder words
  (`gerundio`, `algo`…) are not looked for; what stands in brackets is skipped.
- The earlier lessons are the lessons of the recordings folder with a date before this one that have a
  text: per recording the newest dialogue job, else the newest run with a transcript of each track.
- The text of a job is what was said (`SaidText`), not its refined transcript as it is. An old refine
  respelled Spanish the recogniser wrote in Cyrillic — that word was said — and also wrote Russian lines in
  Spanish — those words were not. So the lines of the newest raw `transcript*.md` are taken whole, and of
  the newest `transcript_fixed*.md` only the Latin-letter words the raw text of the same job has: in the
  same letters, or with the same sound in either alphabet (the sound rule of the refiner's
  `RespellingGuard.SoundForm`, copied — a change of one is a change of both). A word that is not kept
  breaks its line, so no phrase is found across it. A job that kept no raw transcript is checked against
  the segments of its `chunks_json/*.json`; with neither, the refined transcript is taken as it is.
  Known leak: a Russian word that sounds like a Spanish one cannot be told from it and is kept.
- A lesson of a day folder that no job transcribed is read from the answer files of another recognition
  service lying in that day folder: every top-level `*.json` but `*response.json`, the `text` of its
  `segments`. Once a job has transcribed one of the lesson's recordings, these files are not read.

## API (agent05, names in camel case)

| Method and path | What it does |
|---|---|
| `GET /api/sessions` | every lesson has `package`: `{ "builtAt" }` or `null` |
| `GET /api/sessions/{date}/package` | `{ date, package, compared: { lessons, from, to, dates }, counts: { newWords, newWordsNeverSeen, vocabulary, vocabularyNeverSeen, otherWords, otherWordsNeverSeen, edited, leftOut, stale } }` — the package with the edits put on and the words counted |
| `PUT /api/sessions/{date}/package/marks/{id}` | body `{ "fields"?: { name: text \| null }, "leftOut"?: bool }`; answers with the package as it reads now |
| `DELETE /api/sessions/{date}/package/marks/{id}` | takes every edit off one item; answers with the package as it reads now |
| `GET /api/sessions/tracks/audio?track=<track id>` | the audio file of a track of the recordings folder; a part of it can be asked for (HTTP range) |
| `GET /api/sessions/{date}/text` | for the builder: every recording of the lesson, each line with its time, speaker and `role` (`teacher`, `student`, `other` or none) — agent05 README, «Текст урока с говорящими» |
| `GET /api/sessions/words?before=YYYY-MM-DD` | for the builder: `{ before, lessons: [{ date, forms }] }` — the word forms of every earlier lesson, the ones "said before" is counted with |

Errors: 400 — the date is not `YYYY-MM-DD`, the id is not an id, no `fields` and no `leftOut`, a field the
item does not have, a text too long, the name of the item left empty; 404 — the lesson has no package, the
package has no such item, the item has no edit to take off, no such track; 409 — `package.json` or
`marks.json` cannot be read; 500 with the reason — the edit could not be written.
