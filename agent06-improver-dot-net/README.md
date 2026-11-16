# agent06-improver-dot-net (TranslationImprover)

.NET service for refining transcripts: fixes Cyrillic transliterations of Spanish words back to Latin script using an LLM (OpenAI or compatible API). Port of the Python agent03-trans-improver with REST and gRPC APIs.

## Structure

- **API/** — main project (.NET 9). Solution: `TranslationImprover.slnx` (API, API.Tests). Build and run from `API/`.
- **docs/** — e.g. RENTGEN_IMPLEMENTATION.md (nodes, semanticKey, tag).

## Requirements

- .NET 9.0
- **WorkspaceRoot** — directory for input/output files (required in config or environment)
- **OpenAI API key** — set in config or environment (never in request body)

## Configuration

| Key | Description |
|-----|-------------|
| `WorkspaceRoot` or `workspace_root` | Root directory for file paths (required). All request paths are relative to this. |
| `OpenAI:ApiKey` or `OpenAiApiKey` | OpenAI API key. Can also use env `OPENAI_API_KEY` or `OpenAI__ApiKey`. |
| `OpenAI:BaseUrl` | Optional. Default `https://api.openai.com/`. Use for compatible endpoints. |

### Using a .env file (recommended, like agent03)

The app loads a `.env` file from the current or app directory at startup. **The `.env` file is gitignored** — your keys will not be committed.

1. Copy the example file and add your values:

```bash
cp env.example .env
# Edit .env and set OPENAI_API_KEY and WorkspaceRoot
```

2. Example `.env`:

```
OPENAI_API_KEY=sk-your-key-here
WorkspaceRoot=C:/data/workspace
```

3. Run from the repo root or from `API`; the app looks for `.env` in the current directory and in the application directory.

You can also use `appsettings.Development.json` or environment variables; `.env` is applied first so it is a convenient place for local secrets.

## Running

```bash
cd agent06-improver-dot-net/API
dotnet build
dotnet run
```

By default the service listens on **http://localhost:5033** for gRPC (HTTP/2 only, h2c — what agent05 calls) and **http://localhost:5034** for REST / health (HTTP/1.1); both are fixed in `API/Program.cs` (`ConfigureKestrel`). Ensure `WorkspaceRoot` and (for refinement) `OPENAI_API_KEY` are set in `.env`, appsettings, or environment before starting.

## API overview

- **REST:** `POST /api/refine/jobs` (submit), `GET /api/refine/jobs/{id}`, `GET .../stream` (SSE), `GET .../result`, `POST .../cancel`, `GET /api/refine/jobs/query`, `GET .../nodes`
- **gRPC:** RefinerService (SubmitRefineJob, GetRefineStatus, StreamRefineStatus, CancelRefineJob, QueryRefineJobs)
- **Health:** `GET /`, `GET /health`

Input: `input_file_path` (relative to WorkspaceRoot) or `input_content`. Output: optional `output_file_path` (relative). Prompt template: optional `prompt_file` or built-in default with `{context}` and `{batch}` placeholders.

Both entries hand the request to one place (`RefineJobSubmitter`): `RefineJobRequestMapper` trims it, gives the defaults (model `gpt-4o-mini` — `gpt-4.1-mini` for a run about the words in doubt —, block of 10 lines, 3 lines of context) and checks the paths, so a field means the same over REST and over gRPC.

### What a run leaves behind

- **Output file** (`output_file_path`): the transcript with the refined lines, in the form of its source — byte order mark, line ends and the line end after the last line are kept, so a run that changes nothing gives the source back byte for byte.
- **Run record** (`<output>.prompt.md`): model, block size, both prompts, and at the end the section `## Outcome` — how many blocks there were, how many of them **failed** (their lines are in the output as they were), and the tokens billed.
- **Block records** (`<intermediate_dir>/batch_NNNN_of_NNNN.json`, with `save_intermediate`): the lines before and after, the context, `rawText` — the model's answer as it came, before any check —, `editsLetThrough` / `editsPutBack` of the check, `promptTokens` / `completionTokens`.
- **Status** (REST `GET /api/refine/jobs/{id}`, gRPC `GetRefineStatus` / `StreamRefineStatus`): `failed_batches`, `prompt_tokens`, `completion_tokens`, `edits_let_through`, `edits_put_back` of the run so far.

A block whose request failed, or whose answer has no text, keeps its lines and is counted as failed; the job still ends `Completed`, so read `failed_batches`. A run that cannot read its transcript ends `Failed`.

### Check of edits: `respelling_only`

`SubmitRefineJobRequest.respelling_only` (gRPC, field 18; REST body `respellingOnly`) turns on a check of what the model changed (`RespellingGuard`). It is meant for the built-in prompt on lesson transcripts: the prompt asks for one kind of edit, a Spanish word written in Cyrillic letters by ear respelled in Latin letters, and a small model still does more. With the flag a changed stretch of a line is kept only when all of this holds:

- every word it replaces has a Cyrillic letter, and every word put in its place is of Latin letters only;
- the two sound alike (`SoundForm` of both, similarity from 0.85; «ль» before a vowel is heard both as "li" and as the Spanish "ll", so «бомбилья» is "bombilla");
- every new word is already written in Latin letters somewhere in the same transcript;
- digits and punctuation of the stretch are the same (the opening `¿` and `¡` aside);
- **Spanish stands next to it**: the nearest word on the left or on the right is in Latin letters, or is another word the check respells (so a phrase written in Cyrillic carries itself). A word in Cyrillic that sounds like a Spanish word of the transcript is as often a Russian word («институт» — "instituto", «история» — "historia"), and nothing but the speech around it tells the two apart. Not asked of a word written in both alphabets at once («Теngo»), nor of a word that is the whole text of its line.

A word of one or two sounds («ла» — "la", «йо» — "yo") is respelled only when the sound is exactly the same, Spanish stands on every side it has a neighbour on, and it is not a Russian word («и», «но», «мы» stay: «una casa и un perro»).

**When Spanish next to the word is not asked.** The rule above fits a transcript that writes its Spanish in Latin letters (participant tracks, `gpt-transcribe`). The older transcriber writes a good part of the Spanish in Cyrillic; there a word that sounds Spanish is Spanish nearly every time and its neighbours are in Cyrillic too. The run reads the habit off the transcript (`SpellingCandidates.Habit`): the words in Cyrillic that sound like a Spanish word of the transcript against the words in Latin letters. From 10 % (and at least 20 such words) the transcript counts as writing Spanish in Cyrillic, and nothing is asked of the neighbours. On 36 transcripts the share was 0.1–4.3 % for the first kind and 13–21 % for the second.

Everything else is put back as it was: a translation, a Russian word written in Latin letters, a "correction" of the speaker's Spanish, a changed ending, added or dropped words. The time mark and the speaker of a line are never taken from the model, and a block that came back with another number of lines is put back whole. The next block gets the checked lines as its context, and the before/after table of a block shows them too.

The run record (`<output>.prompt.md`) says `Check of edits: on` or `off` and, with the check, has the section `## Check of edits`: which rule about the neighbours was in force and why, how many changes were let through and how many were put back, and every change let through with the time mark of its line.

Without the flag nothing changes: the lines are written as the model returned them.

What the check does not catch: a Russian word that sounds like a Latin word of the same transcript and has Spanish next to it («в историю, en la historia»). What it costs: a Spanish word in Cyrillic that occurs nowhere in the transcript in Latin letters stays in Cyrillic, and so does a Spanish word quoted alone inside Russian speech («что такое вале»).

Measured on the stored answers of the model for 33 lesson dialogues (14,193 lines, 305 changes of the model; no new requests). Before the rule about the neighbours the check kept 24 changes: 14 right, 7 harmful (a Russian word turned Spanish), 3 doubtful. With it: 15 kept — 13 right, 1 harmful, 1 doubtful; 3 right ones are lost (a Spanish word alone in Russian speech). On 3 transcripts of the older transcriber (habit: Spanish in Cyrillic) the kept changes go from 71 to 75, all right — the 4 new ones are short words.

### Words in doubt: `candidates_only`

`SubmitRefineJobRequest.candidates_only` (gRPC, field 19; REST body `candidatesOnly`) starts another kind of run: no line is sent to the model to be rewritten. This is what agent05 asks for with `prompt: lesson`.

1. **Code finds the words in doubt** (`SpellingCandidates`, against every word the transcript has in Latin letters — `SpanishLexicon`):
   - a word in Cyrillic letters that sounds exactly like a Spanish word of the transcript («вале» — "vale");
   - a word written in letters of both alphabets («Теngo», «Nет»);
   - a word of one or two sounds with the exact sound of a Spanish word, not a Russian short word, with Spanish or another word in doubt on every side («эн» in «una casa эн Madrid»).

   A word that is only close to a Spanish one (`Likeness` 0.85–0.99) is not asked about: the lesson set has 225 of them and nearly all are Russian — a cognate or a case form («момент», «системы»).
2. **The model is the judge of them** (`ISpellingJudge`, `OpenAISpellingJudge`). Ten words to a request, the words of one line never parted. A word goes with the text of its line (at most 14 words on either side, the word marked `[[like this]]`; no time mark, no speaker). The answer is JSON: for each word `"said"` — `ru`, `es` or `mix` — and the word repeated; an answer that repeats another word belongs to another item and is dropped. The model is not asked for a spelling and none is read from its answer. Every request is made twice (`CandidateRespelling.TimesAsked`), and a word has a language only when both answers name the same one (`CandidateRespelling.Agreed`).
3. **Code writes the spelling** (`CandidateRespelling`):
   - a word in Cyrillic called Spanish gets the spelling the transcript has, with the capitals of the word; the punctuation around it stays;
   - a short word called Spanish gets it too when, after the other changes, Spanish stands on every side it has a neighbour on;
   - a word of both alphabets called Spanish gets the spelling of the transcript when it sounds exactly like a word of it, otherwise its Cyrillic letters are written in Latin — at most two of them, fewer than the Latin ones, and only letters with one reading («Теngo» → "Tengo"; «Кiero» stays: «К» may be c, qu or k);
   - a word of both alphabets called Russian gets its Latin letters written in Cyrillic by sound — at most two of them and fewer than its Cyrillic ones, or any number when the result is a word the transcript has in Cyrillic («Nет» → «Нет»);
   - a word called Russian or `mix`, a word the two answers disagree on, a word without an answer and every word of a request that failed stay as they were.

Everything else of the transcript is the source to the letter: only the tokens named change (`CandidateRespelling.ReplaceTokens`).

**Model.** `gpt-4.1-mini` when the request names none (`RefineJobRequestMapper.DefaultJudgeModel`); block size, context lines and the prompts of the request are not used. The run sends about fifty times less text than one that sends every line, so the stronger model costs less here than the small one does there.

**What the run leaves behind.** The run record says `Mode: words in doubt` and how many words of each kind were found; its section `## Words in doubt` lists every word written anew with the time mark of its line and counts the words left as they were by reason. A block of the status and of the block files is one request: `askedWords` (the words, each with the Spanish word it sounds like), the lines of the request before and after, `rawText` (both answers, under `[answer 1]` and `[answer 2]`), the tokens of both. `edits_let_through` counts the words written anew, `edits_put_back` the words left as they were.

**Measured** (2026-11-15, 33 lesson dialogues, 14,193 lines): 451 words asked about in 60 requests, 44,403 tokens in and 8,557 out — about $0.03 for all 33 at $0.40 / $1.60 per million. 54 words written anew in 20 of the dialogues: 43 right, 2 harmful, 9 doubtful. The run that sends every line with the check of edits, on the same dialogues: 15 changes kept — 13 right, 1 harmful, 1 doubtful — for about 2,840 requests. Two transcripts of the older transcriber (Spanish in Cyrillic as a habit): 52 words asked about, 46 written anew, all right; a third one was not run with the judge as it is now.

**Measured a second time** (the same 33 dialogues run as jobs through agent05; same prompt, model and temperature 0, token counts equal to the unit): 65 words written anew — 48 right, 7 harmful, 10 doubtful. 53 words are written in both runs (43 right, 2 harmful, 8 doubtful).

**Two answers.** Both measurements were made with one answer to a request. The run now makes every request twice and writes a word anew only when the answers agree. Replayed over the stored answers of the two runs, with no request to the model, it writes those 53 words; the answers disagree on 14 of the 451. A run bills twice the tokens — about $0.06 for the 33 dialogues. Run live on one of the dialogues (14 words, 2 requests, 2,922 tokens in and 538 out): the two answers of one request, made one after the other, differ on 4 of its 10 words — «проблем» and «нормаль», twice each, Spanish in the first answer and Russian in the second — and those stay; 5 words are written anew, none of them harmful.

**Two answers, measured live** (the 33 dialogues through the REST entry, nothing written into a job folder; 60 requests made twice, 88,806 tokens in and 17,114 out, $0.063): 62 words written anew — 45 right, 6 harmful, 11 doubtful. The two answers of a request name different languages for 8 of the 451 words. Two requests made one after the other agree far more often than two runs three hours apart did (14 words), and they agree on the wrong answers too: of the 7 harmful words of the single-answer run the rule held back one, and it held back 3 right ones. The replay above had promised 2 harmful words; it does not describe a run.

**A stronger judge, measured** (2026-11-16, the same 60 requests put once to `gpt-4.1` by the measuring tool, nothing written into a job folder; 44,403 tokens in and 9,427 out, $0.164 at $2 / $8 per million — five times the price of the default): 69 words written anew — 53 right, 3 harmful, 13 doubtful. It calls Russian the six harmful words `gpt-4.1-mini` wrote in its live runs («град», «проблем», and «примера» and «паре» in the lines all its runs changed) and makes errors of its own: «примера» in another line, a Russian word cut short, a grammar term in a Russian case form («перфекта» — "perfecta"). It leaves two right words of the small model as they were («Португаль»). Counted on the stored answers, with no request: a word written only when both models call it Spanish gives 50–55 words against the three stored runs of `gpt-4.1-mini` — 41–45 right, 0–1 harmful, 9–10 doubtful; the right words it loses are those only one model found (8–12 of `gpt-4.1`, 2–3 of the small one). This is one answer set of `gpt-4.1`; a rule of two models is not built and not run live.

**What was tried and dropped.**

- The question "in which language was it said", with the like-sounding Spanish word as a hint and the neighbour lines: `gpt-4o-mini` called about 45 Russian words Spanish out of 106 («роль», «без», «её»). The hint and the neighbour lines went; the prompt now has the model say first whether Russian has the word as written.
- `gpt-4o-mini` as the judge of the final question: 68 words called Spanish — 34 right, 28 harmful. `gpt-4.1-mini`: 4 harmful. Hence the default.
- A second request without the line ("is this string a Russian word"): it removed the gross errors and disagreed with itself between calls.
- Holding a word back in code whenever the model says that Russian has it: on the lesson set it stopped four respellings, all right (a Spanish word Russian has borrowed), and not one wrong. The model still says it, the code does not read it.

**Limits.**

- The judge does not give the same answer twice. In the second run it answered 12 of the 451 words differently, and they sit in 4 of the 60 requests: an answer that changes takes several words of its request with it. Five of the twelve are harmful: with one answer to a request one run wrote 2 harmful words and the next 7. Making every request twice does not hold that back: the two answers of one run mostly agree, also where they are wrong ("Two answers, measured live" above).
- A rule that asks for a word in Latin letters next to the word, as the check of edits does, was simulated on the three stored runs and not built: it holds back every harmful word and half of the right ones (21–24 kept of 45–48) — a Spanish word quoted in Russian speech has Russian on both sides. A rule "the word stands in Cyrillic in another lesson" holds back 3 of the 7 harmful words and 2 right ones: 33 dialogues are too few to tell a Russian word by.
- Any Latin word of the transcript counts as Spanish. Where the transcriber wrote a Russian word in Latin letters in one line ("grad", "problem"), the same word in Cyrillic elsewhere («град», «проблем») gets a like-sounding spelling, and when both answers call it Spanish it is written so. The code has no dictionary of Spanish to tell that "grad" is not a word.
- The two harmful changes that both runs made are of one kind: a Russian case form that is, sound for sound, a Spanish word of the transcript («примера» — "primera", «паре» — "pare"), and the judge called it Spanish. The code has no dictionary of Russian to hold it back with; `gpt-4.1` called both Russian ("A stronger judge, measured" above).
- The doubtful ones: a Spanish word that Russian has borrowed («гаспачо», «мате») said inside Russian speech is written in Latin letters.
- A Spanish word in Cyrillic that the transcript has nowhere in Latin letters is not found, as with the check of edits. A lexicon of all lessons would find it; there is none.

See [docs/RENTGEN_IMPLEMENTATION.md](docs/RENTGEN_IMPLEMENTATION.md) for the virtual model (nodes, semanticKey, tag).

## Tests

```bash
cd agent06-improver-dot-net
dotnet test
```
