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

Both entries hand the request to one place (`RefineJobSubmitter`): `RefineJobRequestMapper` trims it, gives the defaults (model `gpt-4o-mini`, block of 10 lines, 3 lines of context) and checks the paths, so a field means the same over REST and over gRPC.

### What a run leaves behind

- **Output file** (`output_file_path`): the transcript with the refined lines, in the form of its source — byte order mark, line ends and the line end after the last line are kept, so a run that changes nothing gives the source back byte for byte.
- **Run record** (`<output>.prompt.md`): model, block size, both prompts, and at the end the section `## Outcome` — how many blocks there were, how many of them **failed** (their lines are in the output as they were), and the tokens billed.
- **Block records** (`<intermediate_dir>/batch_NNNN_of_NNNN.json`, with `save_intermediate`): the lines before and after, the context, `rawText` — the model's answer as it came, before any check —, `editsLetThrough` / `editsPutBack` of the check, `promptTokens` / `completionTokens`.
- **Status** (REST `GET /api/refine/jobs/{id}`, gRPC `GetRefineStatus` / `StreamRefineStatus`): `failed_batches`, `prompt_tokens`, `completion_tokens`, `edits_let_through`, `edits_put_back` of the run so far.

A block whose request failed, or whose answer has no text, keeps its lines and is counted as failed; the job still ends `Completed`, so read `failed_batches`. A run that cannot read its transcript ends `Failed`.

### Check of edits: `respelling_only`

`SubmitRefineJobRequest.respelling_only` (gRPC, field 18; REST body `respellingOnly`) turns on a check of what the model changed (`RespellingGuard`). It is meant for the built-in prompt on lesson transcripts: the prompt asks for one kind of edit, a Spanish word written in Cyrillic letters by ear respelled in Latin letters, and a small model still does more. With the flag a changed stretch of a line is kept only when all of this holds:

- every word it replaces has a Cyrillic letter, and every word put in its place is of Latin letters only;
- the two sound alike (`SoundForm` of both, similarity from 0.85; a stretch shorter than 3 sounds is not judged);
- every new word is already written in Latin letters somewhere in the same transcript;
- digits and punctuation of the stretch are the same (the opening `¿` and `¡` aside).

Everything else is put back as it was: a translation, a Russian word written in Latin letters, a "correction" of the speaker's Spanish, a changed ending, added or dropped words. The time mark and the speaker of a line are never taken from the model, and a block that came back with another number of lines is put back whole. The next block gets the checked lines as its context, and the before/after table of a block shows them too.

The run record (`<output>.prompt.md`) says `Check of edits: on` or `off` and, with the check, ends with the section `## Check of edits`: how many changes were let through and how many were put back.

Without the flag nothing changes: the lines are written as the model returned them.

What the check does not catch: a Russian word that sounds like a Latin word of the same transcript. What it costs: a Spanish word in Cyrillic that occurs nowhere in the transcript in Latin letters stays in Cyrillic.

Measured on three stored answers of the model for one dialogue of 593 lines (no new requests): of 29, 47 and 7 lines the model changed, the check kept changes in 3, 4 and 1 — each of them a Spanish word respelled from Cyrillic.

See [docs/RENTGEN_IMPLEMENTATION.md](docs/RENTGEN_IMPLEMENTATION.md) for the virtual model (nodes, semanticKey, tag).

## Tests

```bash
cd agent06-improver-dot-net
dotnet test
```
