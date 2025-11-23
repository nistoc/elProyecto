# Where to See Multi-Language Logs

## Expected Log Output Locations

### 1. Execution Plan (Start of Processing)

**Location:** Very beginning, when you run the command

**What you'll see:**
```
Execution plan:
...
- Languages: ['es', 'ru']    ← This shows the configuration is loaded
...
```

This confirms the configuration has `languages` parameter set correctly.

---

### 2. Per-Segment Processing (Main Logs)

**Location:** During `[STAGE 5-6] Processing X diarized segments...`

**What you'll see for EACH segment:**

```
--- Processing segment 1/10 ---
    Speaker: SPEAKER_00, Time: 0.50s - 5.20s
[INFO] Transcribing segment with whisper-1...
[INFO] Trying transcription with languages: ['es', 'ru']    ← HERE!
[INFO] Language 'es': transcribed 45 chars                  ← Spanish result
       Text: Hola, ¿cómo estás? Muy bien, gracias.          ← Spanish text
[INFO] Language 'ru': transcribed 41 chars                  ← Russian result
       Text: Привет! Как дела? Всё отлично.                 ← Russian text
[INFO] Trying transcription with auto-detection (language=null)  ← Auto-detect
[INFO] Auto-detection: transcribed 48 chars, detected language: es
       Text: Hola, ¿cómo estás? Muy bien, muchas gracias.   ← Auto-detect text
[INFO] Successfully transcribed with 2 language(s) + auto-detection
[INFO] Saved intermediate result: processing_workspaces/.../intermediate/...
```

**This happens for EVERY segment!** If you have 10 segments, you'll see this 10 times.

---

## Where You WON'T See It

### ❌ In Execution Plan Details
The execution plan only shows the **configuration** (`Languages: ['es', 'ru']`), not the actual transcription process.

### ❌ Before Diarization Starts
Multi-language transcription only happens AFTER:
1. File is converted to WAV
2. Speaker diarization runs
3. Segments are extracted
4. Each segment is transcribed

### ❌ If Using Non-Diarization Mode
Multi-language transcription only works with `"use_diarization": true`

---

## Example Full Log Flow

```
================================================================================
STARTING PROCESSING
================================================================================

Execution plan:
- Load config from: config/default.json
- Input directory: taskstoparse (2 file(s) found)
- Languages: ['es', 'ru']                    ← Configuration loaded
- Use diarization (v3.0+): True
...

[FILE] taskstoparse/Voice 251022_191028-1-1-1.m4a

[STAGE 1] Creating workspace...
[INFO] Created workspace for 'Voice_251022_191028-1-1-1': processing_workspaces/Voice_251022_191028-1-1-1

[STAGE 3] Converting to WAV if needed...
[INFO] Converting m4a to wav: Voice 251022_191028-1-1-1.m4a -> processing_workspaces/Voice_251022_191028-1-1-1/converted_wav/Voice_251022_191028-1-1-1.wav
[INFO] Working with converted file: processing_workspaces/Voice_251022_191028-1-1-1/converted_wav/Voice_251022_191028-1-1-1.wav

[STAGE 4] Running speaker diarization...
... (diarization logs) ...

[STAGE 5-6] Processing 8 diarized segments...

--- Processing segment 1/8 ---                               ↓↓↓ START HERE ↓↓↓
    Speaker: SPEAKER_00, Time: 0.50s - 5.20s
[INFO] Transcribing segment with whisper-1...
[INFO] Trying transcription with languages: ['es', 'ru']
[INFO] Language 'es': transcribed 120 chars
       Text: Hola, ¿cómo estás? Muy bien, gracias.
[INFO] Language 'ru': transcribed 0 chars
       Text: 
[INFO] Trying transcription with auto-detection (language=null)
[INFO] Auto-detection: transcribed 125 chars, detected language: es
       Text: Hola, ¿cómo estás? Muy bien, gracias por preguntar.
[INFO] Successfully transcribed with 2 language(s) + auto-detection
[INFO] Saved intermediate result: ...

--- Processing segment 2/8 ---
    Speaker: SPEAKER_01, Time: 5.80s - 12.30s
[INFO] Transcribing segment with whisper-1...
[INFO] Trying transcription with languages: ['es', 'ru']
[INFO] Language 'es': transcribed 0 chars
       Text: 
[INFO] Language 'ru': transcribed 156 chars
       Text: Привет! Как дела? Всё отлично, спасибо за вопрос.
[INFO] Trying transcription with auto-detection (language=null)
[INFO] Auto-detection: transcribed 158 chars, detected language: ru
       Text: Привет! Как дела? Всё отлично, большое спасибо.
[INFO] Successfully transcribed with 2 language(s) + auto-detection
[INFO] Saved intermediate result: ...

... (continues for all segments) ...

[STAGE 7] Sorting results by start time...
[STAGE 8] Saving final results...
[DONE] Diarization-based processing complete!
```

---

## How to Test

Run a small test file:

```bash
cd agent03
python -m cli.main --config config/default.json
```

**What to look for:**
1. At the start: `Languages: ['es', 'ru']` in execution plan ✓
2. During segment processing: Multiple `[INFO] Language 'XX': transcribed...` messages ✓
3. For each segment: `[INFO] Auto-detection: transcribed...` message ✓

---

## If You Don't See Multi-Language Logs

### Check 1: Is diarization enabled?
```json
{
  "use_diarization": true    ← Must be true
}
```

### Check 2: Are you looking at the right part of logs?
Multi-language logs appear during **segment processing**, not in execution plan.

### Check 3: Is the file being processed?
If the file is cached or fails before segment processing, you won't see these logs.

### Check 4: Are there segments to process?
If diarization finds 0 segments, no transcription happens.

---

## Verification: Check Output JSON

After processing, open the output JSON file:

```bash
# Find your output file
ls processing_workspaces/*/output/*.json

# View it
cat processing_workspaces/Voice_251022_191028-1-1-1/output/Voice_251022_191028-1-1-1_transcript.json
```

**You should see fields like:**
```json
[
  {
    "speaker": "SPEAKER_00",
    "text": "...",
    "text-es": "...",        ← Spanish transcription
    "text-ru": "...",        ← Russian transcription
    "text-null": "...",      ← Auto-detect transcription
    "words-es": [...],
    "words-ru": [...],
    "words-null": [...]
  }
]
```

If these fields exist, multi-language transcription is working!

---

## Summary

**You will see multi-language logs:**
✅ During segment processing (after diarization)
✅ For each segment individually
✅ With lines like: `[INFO] Trying transcription with languages: ['es', 'ru']`

**You won't see them:**
❌ In initial execution plan (only shows config)
❌ Before diarization runs
❌ If diarization is disabled
❌ If there are no segments to process

