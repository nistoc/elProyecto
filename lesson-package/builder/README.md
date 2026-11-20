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
`--parallel N`, `--first-form all|a|none` (requests of the first form; `all` is today's build), `--agent05 URL`
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
