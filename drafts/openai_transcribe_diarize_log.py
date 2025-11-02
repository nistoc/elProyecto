#!/usr/bin/env python3
"""
OpenAI API–based transcription (multilingual) with optional built‑in speaker labels.

This build adds **controlled chunk overlap** during pre-splitting and avoids
**duplicate emission** for overlapped portions in the Markdown output.

What's new
----------
- New config option: `chunk_overlap_sec` (float, default **2.0**; set 0 to disable).
- New splitter: slices with **fixed duration** (`segment_time`) and **start stride**
  `segment_time - chunk_overlap_sec`, i.e. consecutive chunks overlap by the given seconds.
- Output de-duplication: for chunk *i>0* the first `chunk_overlap_sec` of recognized
  segments are **not emitted** to Markdown, so the text isn't duplicated.
- All previous features preserved: sequential per-file processing, chunk cache by
  fingerprint, incremental Markdown appends, per-chunk JSON save (optional).

Usage
-----
1) Add to your config (example):
{
  "files": ["voice1.m4a"],
  "model": "gpt-4o-transcribe-diarize",
  "chunk_overlap_sec": 2.0,              // 2–3s recommended
  "target_chunk_mb": 24.5,
  "pre_split": true,
  "split_workdir": "chunks",
  "chunk_naming": "{base}_part_%03d.m4a",
  "md_output_path": "{base}.md",
  "raw_json_output_path": "{base}.json",
  "cache_dir": "cache"
}
2) Run: `python openai_transcribe_diarize_log.py --config transcribe_config.json`

Notes
-----
- Overlap is applied only when pre-splitting is active and the file exceeds `target_chunk_mb`.
- Emission guard: the *first* chunk emits everything; subsequent chunks skip segments with local
  start `< chunk_overlap_sec` (with a small epsilon) to avoid duplicated text.
"""
import argparse
import json
import os
import sys
import math
import shutil
import subprocess
import hashlib
from dataclasses import dataclass
from typing import List, Optional, Tuple, Any, Dict

# --- OpenAI client (>= v1) ---
try:
    from openai import OpenAI
except Exception:
    print("[FATAL] Missing 'openai' package. Install: pip install openai", file=sys.stderr)
    raise

# ---------------- Data classes ----------------
@dataclass
class ASRSegment:
    start: float
    end: float
    text: str
    speaker: Optional[str] = None

# ---------------- Helpers ----------------

def _secs(x: Any) -> float:
    try:
        return max(0.0, float(x))
    except Exception:
        return 0.0


def _fmt_mb(num_bytes: int) -> str:
    return f"{num_bytes/1024/1024:.2f} MB"


# Read JSON config with support for env:VAR indirection

def load_config(path: str) -> Dict[str, Any]:
    with open(path, "r", encoding="utf-8") as f:
        cfg = json.load(f)
    def resolve(v):
        if isinstance(v, str) and v.startswith("env:"):
            return os.getenv(v.split(":", 1)[1], None)
        return v
    for k in list(cfg.keys()):
        cfg[k] = resolve(cfg[k])
    return cfg


def print_plan(cfg: Dict[str, Any], cfg_path: str):
    sanitized = dict(cfg)
    for key in ["openai_api_key"]:
        if sanitized.get(key):
            sanitized[key] = "*** (sanitized config) ***"
    plan_lines = [
        "Execution plan:",
        f"- Load config from: {cfg_path}",
        f"- Model: {cfg.get('model')}",
        f"- Files: {cfg.get('files') or [cfg.get('file')]}",
        f"- Language: {cfg.get('language')} (null => auto / multi-language)",
        f"- Temperature: {cfg.get('temperature')}",
        f"- Prompt provided: {'yes' if cfg.get('prompt') else 'no'}",
        f"- Request speaker labels from API: {bool(cfg.get('openai_speaker_diarization'))}",
        f"- Pre-splitting: {bool(cfg.get('pre_split'))} (target <= {cfg.get('target_chunk_mb')} MB)",
        f"- Chunk overlap: {float(cfg.get('chunk_overlap_sec') or 0.0)} sec",
        f"- Split workdir: {cfg.get('split_workdir')} | ffmpeg: {cfg.get('ffmpeg_path')} | ffprobe: {cfg.get('ffprobe_path')}",
        f"- Cache dir: {cfg.get('cache_dir')}",
        f"- Save per-chunk JSON: {bool(cfg.get('save_per_chunk_json'))} -> dir: {cfg.get('per_chunk_json_dir')}",
        "",
        "Sanitized config (effective):",
        json.dumps(sanitized, ensure_ascii=False, indent=2)
    ]
    print("\n".join(plan_lines))

# ---- ffprobe / ffmpeg utilities ----

def which_or(path_key: Optional[str], default_name: str) -> Optional[str]:
    if path_key:
        return path_key
    return shutil.which(default_name)


def ffprobe_duration_and_size(ffprobe_path: str, filepath: str) -> Tuple[float, int]:
    size = os.path.getsize(filepath)
    try:
        cmd = [ffprobe_path, "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", filepath]
        out = subprocess.check_output(cmd, stderr=subprocess.STDOUT)
        dur = float(out.decode().strip())
    except Exception:
        dur = 0.0
    return (dur, size)


def segment_time_for_target_mb(size_bytes: int, duration_sec: float, target_mb: float) -> int:
    if duration_sec <= 0:
        return 480
    target_bytes = int(target_mb * 1024 * 1024)
    bytes_per_sec = size_bytes / max(duration_sec, 0.001)
    seg_seconds = max(60, int((target_bytes * 0.97) / max(bytes_per_sec, 1)))
    return seg_seconds


def _safe_float(v: Any, default: float) -> float:
    try:
        x = float(v)
        if math.isnan(x) or math.isinf(x):
            return default
        return x
    except Exception:
        return default

# --- Overlap slicer ----

def slice_with_overlap(ffmpeg_path: str, src: str, segment_time: int, overlap_sec: float, workdir: str, naming: str) -> Tuple[List[str], List[float], List[float]]:
    """Create overlapped chunks using a fixed window `segment_time` and stride
    `segment_time - overlap_sec`. Returns (chunk_paths, offsets, emit_guard_secs).

    - offsets[i] is the global time offset for chunk i.
    - emit_guard_secs[i] is the local time within the chunk before which *no text is emitted*.
      For i==0 it's 0.0; for i>0 it's `overlap_sec`.
    """
    os.makedirs(workdir, exist_ok=True)
    dur, _ = ffprobe_duration_and_size(which_or(None, "ffprobe") or "ffprobe", src)

    # clamp overlap
    overlap = max(0.0, min(float(overlap_sec), max(0.0, segment_time - 0.5)))
    stride = max(1.0, segment_time - overlap)

    base_no_ext = os.path.splitext(os.path.basename(src))[0]
    out_pattern = os.path.join(workdir, os.path.basename(naming.format(base=base_no_ext)))
    prefix = out_pattern
    # Determine numbering width based on worst-case chunk count
    est_count = int(math.ceil(max(1.0, dur) / stride))
    pad = max(3, int(math.ceil(math.log10(max(1, est_count + 1)))))

    chunk_paths: List[str] = []
    offsets: List[float] = []
    emit_guards: List[float] = []

    t = 0.0
    idx = 0
    while t < dur - 0.25:  # small tail tolerance
        # duration for this window
        win_dur = min(segment_time, max(0.0, dur - t))
        # output path
        out_path = prefix.replace("%03d", str(idx).zfill(pad))
        # precise cut: place -ss *after* input for accuracy (at cost of speed)
        cmd = [
            ffmpeg_path, "-y",
            "-i", src,
            "-ss", f"{t:.3f}",
            "-t", f"{win_dur:.3f}",
            "-c", "copy",
            out_path,
        ]
        subprocess.check_call(cmd)
        chunk_paths.append(out_path)
        offsets.append(t)
        emit_guards.append(0.0 if idx == 0 else overlap)

        idx += 1
        t += stride

    # Ensure natural sort order
    paired = list(zip(chunk_paths, offsets, emit_guards))
    paired.sort(key=lambda x: x[1])
    chunk_paths, offsets, emit_guards = map(list, zip(*paired)) if paired else ([], [], [])

    return chunk_paths, offsets, emit_guards

# ---- Re-encode if too big ----

def reencode_if_too_big(ffmpeg_path: str, path_in: str, target_mb: float, bitrate_kbps: int) -> str:
    size_mb = os.path.getsize(path_in) / 1024 / 1024
    if size_mb <= target_mb:
        return path_in
    root, ext = os.path.splitext(path_in)
    out_path = root + "_re.m4a"
    cmd = [
        ffmpeg_path, "-y", "-i", path_in,
        "-ac", "1", "-ar", "16000",
        "-b:a", f"{bitrate_kbps}k",
        out_path,
    ]
    subprocess.check_call(cmd)
    return out_path

# ---- Pre-splitting orchestrator ----

def make_chunks_if_needed(cfg: Dict[str, Any], src: str) -> Tuple[List[str], List[float], List[float]]:
    pre_split = bool(cfg.get("pre_split"))
    if not pre_split or not src or not os.path.isfile(src):
        return [src], [0.0], [0.0]

    ffmpeg_path = which_or(cfg.get("ffmpeg_path"), "ffmpeg")
    ffprobe_path = which_or(cfg.get("ffprobe_path"), "ffprobe")
    if not ffmpeg_path or not ffprobe_path:
        print("[WARN] ffmpeg/ffprobe not found in PATH; skipping pre-splitting.")
        return [src], [0.0], [0.0]

    target_mb = float(cfg.get("target_chunk_mb") or 24.5)
    workdir = cfg.get("split_workdir") or "chunks"
    naming = cfg.get("chunk_naming") or "{base}_part_%03d.m4a"

    dur, size = ffprobe_duration_and_size(ffprobe_path, src)
    print(f"[INFO] Source duration: {dur:.2f}s | size: {_fmt_mb(size)}")
    if size <= target_mb * 1024 * 1024:
        print("[INFO] Source is under target size; no split needed.")
        return [src], [0.0], [0.0]

    seg_time = segment_time_for_target_mb(size, dur, target_mb)
    overlap_sec = _safe_float(cfg.get("chunk_overlap_sec"), 2.0)
    print(f"[INFO] Overlap slicing: window ~{seg_time}s, overlap {overlap_sec:.2f}s")

    chunks, offsets, emit_guards = slice_with_overlap(
        ffmpeg_path, src, seg_time, overlap_sec, workdir, naming
    )

    reenc = bool(cfg.get("reencode_if_needed", True))
    br_kbps = int(cfg.get("reencode_bitrate_kbps") or 64)
    final_chunks: List[str] = []
    for ch in chunks:
        out = reencode_if_too_big(ffmpeg_path, ch, target_mb, br_kbps) if reenc else ch
        final_chunks.append(out)
        if os.path.getsize(out) > target_mb * 1024 * 1024:
            print(f"[WARN] Chunk still exceeds target after reencode: {out} ({_fmt_mb(os.path.getsize(out))})")

    print(f"[INFO] Produced {len(final_chunks)} chunks in '{workdir}'.")
    for i, ch in enumerate(final_chunks):
        mb = _fmt_mb(os.path.getsize(ch))
        print(f"  - [{i}] {ch} | {mb} | offset={offsets[i]:.2f}s | emit_guard={emit_guards[i]:.2f}s")
    return final_chunks, offsets, emit_guards

# ---- Cache helpers ----

def _ensure_dir(path: str):
    if not path:
        return
    os.makedirs(path, exist_ok=True)


def _file_fingerprint(path: str) -> str:
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1024*1024), b''):
            h.update(chunk)
    return h.hexdigest()


def load_manifest(manifest_path: str) -> Dict[str, Any]:
    if os.path.isfile(manifest_path):
        try:
            with open(manifest_path, 'r', encoding='utf-8') as f:
                return json.load(f)
        except Exception:
            pass
    return {"chunks": {}}  # chunk_basename -> {"fingerprint": str, "response": dict}


def save_manifest(manifest_path: str, manifest: Dict[str, Any]):
    _ensure_dir(os.path.dirname(manifest_path))
    with open(manifest_path, 'w', encoding='utf-8') as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)


# OpenAI transcription (single call)

def transcribe_with_openai(cfg: Dict[str, Any], audio_path: str) -> Dict[str, Any]:
    api_key = cfg.get("openai_api_key") or os.getenv("OPENAI_API_KEY")
    if not api_key:
        print("[FATAL] OPENAI_API_KEY not provided (config or env).", file=sys.stderr)
        sys.exit(2)

    client = OpenAI(
        api_key=api_key,
        base_url=cfg.get("openai_base_url") or None,
        organization=cfg.get("openai_organization") or None,
    )

    primary_model = cfg.get("model") or "gpt-4o-transcribe-diarize"
    fallbacks = cfg.get("fallback_models") or ["gpt-4o-mini-transcribe", "whisper-1"]
    models_to_try = [primary_model] + [m for m in fallbacks if m != primary_model]

    base_kwargs: Dict[str, Any] = {
        "prompt": cfg.get("prompt") or None,
        "temperature": float(cfg.get("temperature")) if cfg.get("temperature") is not None else None,
        "language": cfg.get("language") or None,
        "response_format": cfg.get("response_format") or None,
        "timestamp_granularities": cfg.get("timestamp_granularities") or None,
    }
    base_kwargs = {k: v for k, v in base_kwargs.items() if v is not None}

    if "diarize" in (primary_model or "").lower():
        if "prompt" in base_kwargs:
            print("[WARN] Prompt is not supported for diarization models; dropping 'prompt'.")
            base_kwargs.pop("prompt", None)
        if "chunking_strategy" not in base_kwargs:
            base_kwargs["chunking_strategy"] = "auto"

    import time
    def attempt(model: str, kwargs: Dict[str, Any]):
        with open(audio_path, "rb") as f:
            return client.audio.transcriptions.create(file=f, model=model, **kwargs)

    last_err = None
    for model in models_to_try:
        variations = [dict(base_kwargs)]
        if "timestamp_granularities" in base_kwargs:
            v2 = dict(base_kwargs); v2.pop("timestamp_granularities", None); variations.append(v2)
        if "response_format" in base_kwargs:
            v3 = dict(base_kwargs); v3.pop("response_format", None); variations.append(v3)
        if "diarize" in (primary_model or "").lower():
            for cs in ("auto", "none"):
                vv = dict(base_kwargs); vv["chunking_strategy"] = cs; variations.append(vv)
                vvr = dict(vv); vvr.pop("response_format", None); variations.append(vvr)
        variations.append({k: v for k, v in base_kwargs.items() if k in ("prompt", "temperature", "language")})

        for idx, kw in enumerate(variations, 1):
            for attempt_no in range(3):
                try:
                    print(f"[INFO] Sending file to OpenAI transcription API… (model={model}, variant={idx}, attempt={attempt_no})")
                    resp = attempt(model, kw)
                    if hasattr(resp, "model_dump_json"):
                        return json.loads(resp.model_dump_json())
                    if isinstance(resp, dict):
                        return resp
                    return json.loads(json.dumps(resp, default=lambda o: getattr(o, "__dict__", str(o))))
                except Exception as e:
                    last_err = e
                    msg = str(e)
                    if "InternalServerError" in msg or "status_code=500" in msg or "Error code: 500" in msg:
                        sleep_s = 2 ** attempt_no
                        print(f"[WARN] Server 500 on model {model}, variant {idx}. Retrying in {sleep_s}s…")
                        time.sleep(sleep_s)
                        continue
                    print(f"[WARN] Non-retryable error on model {model}, variant {idx}: {msg}")
                    break
        print(f"[INFO] Model {model} failed; trying next fallback if any…")

    print("[FATAL] All models/variations failed.", file=sys.stderr)
    if last_err:
        print(f"[DETAILS] Last error: {last_err}", file=sys.stderr)
    sys.exit(3)


# Extract ASR segments (and speakers, if returned by the API)

def parse_asr_segments(raw: Dict[str, Any]) -> List[ASRSegment]:
    segments: List[ASRSegment] = []
    if isinstance(raw, dict) and "segments" in raw and isinstance(raw["segments"], list):
        for s in raw["segments"]:
            start = _secs(s.get("start", 0.0))
            end = _secs(s.get("end", start))
            text = (s.get("text") or "").strip()
            spk = s.get("speaker") or s.get("speaker_label")
            segments.append(ASRSegment(start, end, text, spk))
    elif "text" in raw:
        txt = (raw.get("text") or "").strip()
        segments.append(ASRSegment(0.0, 0.0, txt, None))
    else:
        txt = json.dumps(raw, ensure_ascii=False)
        segments.append(ASRSegment(0.0, 0.0, txt, None))
    return segments


# Incremental Markdown writing

def md_paths_for_file(cfg: Dict[str, Any], src_path: str) -> Tuple[str, str]:
    base = os.path.splitext(os.path.basename(src_path))[0]
    md_path = cfg.get("md_output_path") or "transcript.md"
    raw_path = cfg.get("raw_json_output_path") or "openai_response.json"
    md_path = md_path.format(base=base)
    raw_path = raw_path.format(base=base)
    return md_path, raw_path


def md_open_or_init(md_path: str):
    if not os.path.isfile(md_path):
        with open(md_path, 'w', encoding='utf-8') as f:
            f.write(">>>>>>>\n")


def append_md_segments(md_path: str, asr: List[ASRSegment], offset: float, label_map: Dict[str, str], emit_guard_local: float):
    """Append segments, skipping any whose local start is < emit_guard_local to avoid
    duplicate emission caused by chunk overlap. Timestamps are globalized by `offset`."""
    def norm(label: Optional[str]) -> str:
        if not label:
            return "speaker_0"
        if label not in label_map:
            label_map[label] = f"speaker_{len(label_map)}"
        return label_map[label]

    EPS = 1e-3
    with open(md_path, 'a', encoding='utf-8') as f:
        for s in asr:
            if (s.start + EPS) < emit_guard_local:
                continue
            ts = f"{s.start + offset:.2f}"
            spk = norm(s.speaker)
            txt = (s.text or "").replace('"', '\\"')
            f.write(f"- {ts} {spk}: \"{txt}\"\n")


def finalize_md(md_path: str):
    with open(md_path, 'a', encoding='utf-8') as f:
        f.write("<<<<<\n")
    print(f"[INFO] Finalized Markdown: {md_path}")


# Per-chunk JSON (optional)

def _save_per_chunk_json_if_needed(cfg: Dict[str, Any], chunk_basename: str, raw: Dict[str, Any]):
    if not bool(cfg.get("save_per_chunk_json")):
        return
    out_dir = cfg.get("per_chunk_json_dir") or "chunks_json"
    _ensure_dir(out_dir)
    safe_base = os.path.splitext(os.path.basename(chunk_basename))[0]
    out_path = os.path.join(out_dir, f"{safe_base}.json")
    try:
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(raw, f, ensure_ascii=False, indent=2)
        print(f"[INFO] Saved per-chunk JSON: {out_path}")
    except Exception as e:
        print(f"[WARN] Failed to save per-chunk JSON for {chunk_basename}: {e}")


# ------------- Main per-file pipeline -------------

def process_one_file(cfg: Dict[str, Any], src: str):
    if not src or not os.path.isfile(src):
        print(f"[FATAL] Audio not found: {src}", file=sys.stderr)
        sys.exit(2)

    md_path, raw_path = md_paths_for_file(cfg, src)
    cache_dir = cfg.get("cache_dir") or "cache"
    _ensure_dir(cache_dir)
    base = os.path.splitext(os.path.basename(src))[0]
    manifest_path = os.path.join(cache_dir, f"{base}.manifest.json")

    print(f"\n[FILE] {src}")
    print(f"[OUT ] Markdown -> {md_path}")
    print(f"[OUT ] Raw JSON -> {raw_path}")
    print(f"[CACHE] Manifest -> {manifest_path}")

    md_open_or_init(md_path)
    manifest = load_manifest(manifest_path)

    chunk_paths, offsets, emit_guards = make_chunks_if_needed(cfg, src)

    combined_raw: List[Dict[str, Any]] = []
    label_map: Dict[str, str] = {}

    # process sequentially in original order
    for i, ch_path in enumerate(chunk_paths):
        ch_base = os.path.basename(ch_path)
        print(f"[INFO] Processing chunk {i+1}/{len(chunk_paths)}: {ch_base}")

        # fingerprint + cache check
        fp = _file_fingerprint(ch_path)
        cached = manifest.get("chunks", {}).get(ch_base)
        if cached and cached.get("fingerprint") == fp and "response" in cached:
            print("[CACHE] Using cached response for chunk.")
            raw = cached["response"]
        else:
            raw = transcribe_with_openai(cfg, ch_path)
            manifest.setdefault("chunks", {})[ch_base] = {"fingerprint": fp, "response": raw}
            save_manifest(manifest_path, manifest)

        _save_per_chunk_json_if_needed(cfg, ch_base, raw)
        combined_raw.append({"chunk": ch_base, "offset": offsets[i], "emit_guard": emit_guards[i], "response": raw})

        segs = parse_asr_segments(raw)
        append_md_segments(md_path, segs, offsets[i], label_map, emit_guards[i])
        print(f"[INFO] Appended {len(segs)} segments to Markdown (guard {emit_guards[i]:.2f}s).")

    # finalize artifacts per file
    try:
        with open(raw_path, "w", encoding="utf-8") as f:
            json.dump({"chunks": combined_raw}, f, ensure_ascii=False, indent=2)
        print(f"[INFO] Saved combined raw JSON to: {raw_path}")
    except Exception as e:
        print(f"[WARN] Failed to save combined raw JSON: {e}")

    finalize_md(md_path)


# ---------------- Main ----------------

def main():
    parser = argparse.ArgumentParser(description="Transcribe with OpenAI API; sequential per-file; incremental output; chunk-level caching; overlapped chunking without duplicate emission.")
    parser.add_argument("--config", default="transcribe_config.json", help="Path to JSON config.")
    args = parser.parse_args()

    if not os.path.isfile(args.config):
        print(f"[FATAL] Config not found: {args.config}", file=sys.stderr)
        sys.exit(2)

    cfg = load_config(args.config)

    # defaults
    cfg.setdefault("model", "gpt-4o-transcribe-diarize")
    cfg.setdefault("md_output_path", "{base}.md")
    cfg.setdefault("raw_json_output_path", "{base}.json")
    cfg.setdefault("pre_split", True)
    cfg.setdefault("target_chunk_mb", 24.5)
    cfg.setdefault("split_workdir", "chunks")
    cfg.setdefault("ffmpeg_path", "ffmpeg")
    cfg.setdefault("ffprobe_path", "ffprobe")
    cfg.setdefault("reencode_if_needed", True)
    cfg.setdefault("reencode_bitrate_kbps", 64)
    cfg.setdefault("chunk_naming", "{base}_part_%03d.m4a")
    cfg.setdefault("cache_dir", "cache")
    cfg.setdefault("chunk_overlap_sec", 2.0)

    print_plan(cfg, args.config)

    # Determine files list
    files = []
    if isinstance(cfg.get("files"), list) and cfg["files"]:
        files = cfg["files"]
    elif cfg.get("file"):
        files = [cfg["file"]]
    else:
        print("[FATAL] No input file(s) specified in config.", file=sys.stderr)
        sys.exit(2)

    # Process each file sequentially
    for src in files:
        process_one_file(cfg, src)


if __name__ == "__main__":
    main()
