# Legacy work-state recovery (§1a)

When `transcription_work_state.json` is missing, Agent04 can **bootstrap** a first version from on-disk artifacts (legacy agent01/agent03-style jobs).

## Layout

- `chunks/*_part_NNN.wav` (or `.m4a`) — chunk audio  
- `chunks_json/*.json` — per-chunk API JSON (pairing by same stem as the chunk file)  
- Optional: `cache/*.manifest.json` — Agent04 cache (successful responses only)

## Heuristic (summary)

1. **No `chunks/`** (or no chunk-shaped files) → no chunk indices inferred; do not invent `total_chunks` from thin air.
2. **`chunks/` present**, state file missing → **legacy**: derive indices from `part_NNN` in filenames (handles spaces in basename). `total_chunks = max(index) + 1` after deterministic sort; gaps may be logged as `Unknown` / `Pending` in code paths that detect them.
3. **Per index**: non-empty JSON in `chunks_json/` with matching stem → **Completed** (optional JSON shape check); `completed_at` ≈ JSON mtime.
4. **Audio present, JSON missing** → **Pending** (or **Unknown**); do not mark **Failed** without an error source.
5. **JSON present, audio missing** → anomaly; log; other indices still recover.

## Metadata

Bootstrapped documents set `recovered_from_artifacts: true` and `schema_version` so UIs can show a “recovered” badge when needed.

## Examples

See sample job folders under `agent-browser/runtime/` (e.g. `6df0edec-*`, `4b31c2cf-*`) for `chunks/` + `chunks_json/` pairs.

## Related

- [CHUNKS_AND_RENTGEN.md](CHUNKS_AND_RENTGEN.md) — chunking and VM overview.
