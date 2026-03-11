# agent06-improver-dot-net (TranslationImprover)

.NET service for refining transcripts: fixes Cyrillic transliterations of Spanish words back to Latin script using an LLM (OpenAI or compatible API). Port of the Python agent03-trans-improver with REST and gRPC APIs.

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
cd API
dotnet run --project TranslationImprover.Instance
```

Ensure `WorkspaceRoot` and (for refinement) `OPENAI_API_KEY` are set in `.env`, appsettings, or environment before starting.

## API overview

- **REST:** `POST /api/refine/jobs` (submit), `GET /api/refine/jobs/{id}`, `GET .../stream` (SSE), `GET .../result`, `POST .../cancel`, `GET /api/refine/jobs/query`, `GET .../nodes`
- **gRPC:** RefinerService (SubmitRefineJob, GetRefineStatus, StreamRefineStatus, CancelRefineJob, QueryRefineJobs)
- **Health:** `GET /`, `GET /health`

Input: `input_file_path` (relative to WorkspaceRoot) or `input_content`. Output: optional `output_file_path` (relative). Prompt template: optional `prompt_file` or built-in default with `{context}` and `{batch}` placeholders.

See [docs/RENTGEN_IMPLEMENTATION.md](docs/RENTGEN_IMPLEMENTATION.md) for the virtual model (nodes, semanticKey, tag).
