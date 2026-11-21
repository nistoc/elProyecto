# Lesson package builder

Python 3, standard library only. Builds `package.json` ([CONTRACT.md](../CONTRACT.md)) of a lesson in the order of
[BUILDER.md](../BUILDER.md) ("The order of a build"). The prompts are read from [../prompts/](../prompts/); the
schemas, models, `max_tokens` and batch sizes are in the code.

## Run

From `lesson-package/`:

```bash
# a group of days; lessons go in parallel (3 at a time by default)
python -m builder build 2026-11-13 2026-10-10 --work <work folder> --mode dry
python -m builder build 2026-11-13 --work <work folder> --mode openai --max-usd 1.2
python -m builder publish 2026-11-13 --work <work folder> --packages <packages folder>   # --replace to overwrite
python -m unittest discover        # tests
```

Options of `build`: `--answer passN=mode` (a pass with its own mode; `first`, `pass1` … `pass7`),
`--parallel N`, `--first-form a|all|none` (requests of the first form: `a` — request A alone — is the default
since 2026-11-20, by the owner's pick; `all` asks A, B and C, as every package built before it), `--agent05 URL`
(default `http://localhost:5010`, GET only), `--refresh-input`, `--env <env file with the OpenAI key line>`
(default `agent06-improver-dot-net/API/.env`; the key is never printed).

## Modes

| Mode | A missing answer is |
|---|---|
| `dry` | not asked; the estimate of what is missing is printed; no package is written |
| `openai` | asked (paid), stored as it came. `--max-usd` is required: before every request the money paid, the estimate of this request and of everything else still known to be asked must stay within it, else the call stops (exit 4). A first estimate is made before anything is asked |
| `files` | written as `<name>.request.json` (`model`, `temperature`, `max_tokens`, `messages`, `schema`) and not asked. The answer is the file `<name>.json` next to it in the chat-completions shape `{"choices": [{"message": {"content": "<json string>"}, "finish_reason": "stop"}], "usage": {"prompt_tokens": 0, "completion_tokens": 0}, "_answered_by": "<who>"}`. After a stage with missing answers the lesson stops; the call ends with exit code 3 and goes on when run again |
| `replay` | an error (stored answers only) |

A stored answer is never asked again, in any mode. Stages whose input is not answered yet are `blocked`; the
others still write their request files, so a `files` round trip answers as much as it can at once.

An answer cut at its `max_tokens` (`finish_reason` `length`: its content is not whole JSON) is not asked again
either. The cut answer is kept as `<name>.cut.json`; the answer stored in its place holds its whole items, exact
repeats of an item dropped, the fields after the cut empty, and a block `_cut` that says so. The report names it per
pass — "the model looped" when repeats were dropped, else "the list was longer than the room": then raise that
`max_tokens` in the code, move the two files aside and run again (paid). A builder before this rule stored a cut
answer as it came; the next run mends it the same way, for free.

Exit codes: 0 done, 2 error, 3 answers are to be written (files mode), 4 stopped by `--max-usd`.

## Work folder

```
<work>/<date>/
  input/recordings.json      the lines of every recording (period), as agent05 gave them
  input/earlier.json         the plain Latin forms of the earlier lessons (GET /api/sessions/words?before=<date>)
  input/spelling_marks.json  optional: old spelling marks {slug: {word, right}}
  p<n>/task1.json task2.json task1b.json     passes 1, 2, 4
  p<n>/candidates.json cq_NN.json            pass 3: the words put in doubt, the answers about them
  p<n>/first/task1.json task2.json task3.json   the first form (A, B, C)
  merge3.json                pass 5
  filter_words.json filter_gpt-4.1-mini_NN.json   pass 6: the words shown to the filter, its answers
  bycode_todo.json bycode_words.json bycode_<hash>.json bycode2_<hash>.json   pass 7
  package.json
```

The input is read from agent05 only when `input/` lacks it (or with `--refresh-input`); later runs use the stored copy.
The lists that decide which requests are asked (`candidates.json`, `filter_words.json`, `bycode_todo.json`) are
stored and reused like answers, so a stored build can be replayed exactly. Delete one to have it computed again.

## Where it differs from the old scripts

- A recording is one period; its speakers are `П` (teacher), `У` (student), `?` (other or unknown) by `role`.
- Earlier lessons come as forms from agent05, by whole date. A phrase of a word entry is looked up exactly only if
  `earlier.json` holds a `text` for the lesson (agent05 does not send it); otherwise every word of the phrase must be
  among the forms. This only touches the counts `never*` in `stats`.
- Pass 5 is stored as `merge3.json`. `stats.caught` and `stats.keep` (a probe of one lesson) are not written.
- The short labels that put data into a prompt (`строка [n]`, `слово «…»`) are in the code; the prompts themselves are not.

## Page

```bash
python -m builder page <date>... --packages <packages folder>   # writes <packages>/<date>/lesson.html; GET only, free
python -m builder page <date>... --out <folder>                 # <folder>/<date>.html, to look at a page first
```

The page of a lesson day: one self-contained HTML file (a whole document; marked and DOMPurify from cdnjs, the fonts
from Google Fonts, everything else inside) made anew from what agent05 gives — the package as the app shows it (the
owner's edits on, items taken out left out, "said before" and «?» as counted at that moment) and the notes of the
vault copy of the lesson (`GET /api/sessions/{date}/notes`). It lies next to the package of the day; a day folder
without `package.json` is not a package for agent05, so a day of notes alone may have one. `days --publish` makes
the page of every day it built. Pictures of the notes are made at most 1400 px wide (Pillow, when installed) and put
in as data URIs; a sound is put in while it is under 3 MB and the page under 14 MB, else the page names it. A day
with neither a package nor a note gets no page (exit 2 when no date gave one).

The page is not published anywhere (the owner's word of 2026-11-21: files on the disk, not claude.ai Artifacts).
Template: `page_template.html` (`{{title}}`, `{{data}}`; the data is JSON with every `<` written `<`). Tests:
`builder/tests/test_page.py`.

## Days

`days` takes a group of lesson days from a recording to a package. It can be run again and again: every call reads
agent05 anew and does only what is missing, and it stops before every paid step.

```bash
python -m builder days <date>... --work <work folder> --mode dry|files|openai [build options]
    [--transcribe-max-usd X] [--retry-failed] [--publish --packages <packages folder>]
```

The build options are those of `build` (`--mode`, `--answer`, `--max-usd`, `--parallel`, `--first-form`, `--agent05`,
`--refresh-input`, `--env`).

Steps of a call:

1. `POST /api/sessions/batch` with `dryRun: true` for all dates. Per lesson it prints the count of steps by kind
   (`run`, `dialogue`, `wait`, `failed`, `duplicate`, `done`), the minutes to transcribe and the estimate in dollars.
   `failed` and `duplicate` steps are listed with their reason.
2. A lesson with `run` or `dialogue` steps (or `failed` ones with `--retry-failed`) needs a start. Without
   `--transcribe-max-usd` nothing is sent: the estimate is printed and the call ends with 5. With it, one
   `POST` with `dryRun: false`, `maxUsd` set to the flag (and `retryFailed` if given) queues them all; the call prints
   what was queued and ends with 6. Answer 409 (estimate above the limit, nothing started) ends with 4.
3. A lesson with a `wait` step (queued or going) is not built yet (6). A `failed` step without `--retry-failed` is
   printed and the lesson is not built (5). A lesson with no transcript and nothing to start is also 5.
4. Lessons with nothing missing are built in the same call by the code of `build` (same modes, budget and
   parallelism). Lessons not ready are listed as not built.
5. With `--publish` every lesson of the call that has a `package.json` in the work folder is copied to the packages
   folder as `publish` does, never replacing; an existing package is left and reported.

Exit codes: 0 all done, 2 error (also a date agent05 does not know, or agent05 unreachable), 3 answers are to be
written (files mode), 4 over a limit (`--max-usd` or 409 of `--transcribe-max-usd`), 5 a paid or manual step waits for
the person (start not asked for, failed track, nothing to build from), 6 transcription is queued or going: run the same
command again later. The call exits with the most urgent code of its lessons, in this order: 2, 4, 5, 3, 6, 0.
