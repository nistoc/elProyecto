# Changelog v3.1: Workspace Organization & Batch Processing

## Overview

Version 3.1 introduces three major improvements:
1. **Workspace Organization**: A dedicated folder for each audio file being processed
2. **Batch Processing**: Automatic processing of all audio files from a directory
3. **Multi-Language Transcription**: Try multiple languages and select the best result

All intermediate files, outputs, and working files are now organized within file-specific workspaces, making it easy to process multiple files automatically with improved multilingual support.

## What Changed

### Before (v3.0)
All files from different audio sources were mixed together in shared directories:
```
project/
├── converted_wav/
│   ├── file1.wav
│   ├── file2.wav
│   └── file3.wav
├── diarization_segments/
│   ├── file1_seg_0001.wav
│   ├── file2_seg_0001.wav
│   └── ...
├── intermediate_results/
│   └── ...
├── file1_transcript.json
├── file2_transcript.json
└── ...
```

### After (v3.1)
Each audio file gets its own workspace with organized subdirectories:
```
project/
└── processing_workspaces/
    ├── file1/
    │   ├── converted_wav/
    │   │   └── file1.wav
    │   ├── segments/
    │   │   ├── file1_seg_0001_SPEAKER_00.wav
    │   │   ├── file1_seg_0002_SPEAKER_01.wav
    │   │   └── ...
    │   ├── intermediate/
    │   │   ├── file1_seg_0001_result.json
    │   │   └── ...
    │   ├── cache/
    │   │   └── file1_manifest.json
    │   └── output/
    │       ├── file1_transcript.json
    │       ├── file1_transcript.md
    │       └── file1_diarization.json
    └── file2/
        └── ...
```

## Workspace Structure

Each file workspace contains the following subdirectories:

### 1. **converted_wav/**
- Stores WAV-converted audio files (if conversion from M4A is needed)
- Keeps the converted working file used for processing

### 2. **segments/**
- Contains extracted audio segments from diarization
- Each segment is named with format: `{filename}_seg_{index:04d}_{speaker}.wav`

### 3. **intermediate/**
- Stores intermediate processing results
- Useful for debugging and resuming interrupted processes
- Contains JSON files with partial transcription results

### 4. **cache/**
- Stores cache manifest for API call management
- Helps avoid redundant API calls during reprocessing

### 5. **output/**
- Final output files:
  - `{filename}_transcript.json` - Full transcription in JSON format
  - `{filename}_transcript.md` - Formatted markdown transcription
  - `{filename}_diarization.json` - Speaker diarization segments

## Configuration

### workspace_root (optional)
You can customize the root directory for all workspaces in your config file:

```json
{
  "workspace_root": "my_custom_workspaces",
  "use_diarization": true,
  "save_intermediate_results": true
}
```

**Default**: `processing_workspaces`

## Benefits

### 1. **Better Organization**
- Each audio file has its own isolated workspace
- Easy to find all files related to a specific audio
- No mixing of files from different sources

### 2. **Easier Cleanup**
- Delete entire workspace folder to remove all files for one audio
- Keep only the results you need

### 3. **Parallel Processing**
- Multiple audio files can be processed simultaneously without conflicts
- Each workspace is independent

### 4. **Debugging**
- Easy to inspect all intermediate files for a specific audio
- Clear separation makes troubleshooting easier

### 5. **Resumable Processing**
- All cache and intermediate files are in one place
- Easy to resume processing if interrupted

## Example Usage

```bash
# Process a single file - creates workspace automatically
python -m cli.main --config config/default.json --file "my_audio.m4a"

# Result will be in:
# processing_workspaces/my_audio/output/my_audio_transcript.json
# processing_workspaces/my_audio/output/my_audio_transcript.md
```

## Migration from v3.0

If you're upgrading from v3.0, your existing files will continue to work. The new workspace system will be used for all new processing:

1. Old files remain in their original locations
2. New processing creates workspace structure
3. No manual migration needed

## Notes

- Workspace names are derived from the audio filename (without extension)
- Special characters in filenames are preserved in workspace names
- Empty workspaces can be safely deleted
- The system creates all necessary subdirectories automatically

## New Feature: Batch Processing

### input_dir Parameter

Version 3.1 adds support for automatic batch processing of multiple audio files. Instead of specifying individual files, you can now point to a directory, and all supported audio files will be processed sequentially.

**Supported formats:**
- `.m4a`, `.mp3`, `.wav`, `.flac`, `.ogg`, `.aac`, `.wma`, `.opus`

**Configuration:**

```json
{
  "input_dir": "taskstoparse",
  "use_diarization": true
}
```

**Three ways to specify input files:**

1. **Directory scan (new)**: `"input_dir": "path/to/directory"`
   - Automatically finds all audio files
   - Processes them in alphabetical order
   
2. **File list**: `"files": ["file1.m4a", "file2.mp3"]`
   - Process specific files
   
3. **Single file**: `"file": "audio.m4a"`
   - Process one file

**Priority**: `input_dir` > `files` > `file`

**Example:**

```python
from core import Config
from services import TranscriptionPipeline

config = Config({
    "input_dir": "taskstoparse",
    "use_diarization": True,
    "convert_to_wav": True
})

pipeline = TranscriptionPipeline(config)
results = pipeline.process_all_files()  # Processes all files in directory

# Each file gets its own workspace:
# processing_workspaces/file1/output/
# processing_workspaces/file2/output/
# processing_workspaces/file3/output/
```

**Benefits:**
- No need to manually specify each file
- Each file gets its own workspace automatically
- Sequential processing with progress tracking
- Resume capability via caching

See [BATCH_PROCESSING.md](BATCH_PROCESSING.md) for detailed documentation.

## New Feature: Multi-Language Transcription

### languages Parameter

Version 3.1 adds a new `languages` parameter that allows specifying multiple languages for transcription. Instead of relying solely on auto-detection or specifying a single language, you can now provide a list of candidate languages.

**How it works:**

1. For each audio segment, the system transcribes with each specified language
2. Compares all results
3. Selects the best transcription (longest non-empty text)

**Configuration:**

```json
{
  "languages": ["es", "ru"],
  "use_diarization": true
}
```

**Default**: `["es", "ru"]` (Spanish and Russian)

**Benefits:**

- **Better accuracy** for multilingual content (e.g., Spanish-Russian conversations)
- **No more guessing** which language will work best
- **Automatic selection** of the best result
- **Backward compatible** - falls back to single `language` parameter if not specified

**Example:**

```python
config = Config({
    "file": "multilingual_audio.m4a",
    "languages": ["es", "ru"],  # Try both Spanish and Russian
    "use_diarization": True
})

pipeline = TranscriptionPipeline(config)
md_path, json_path = pipeline.process_file("multilingual_audio.m4a")

# System will:
# 1. Try transcription with language="es"
# 2. Try transcription with language="ru"
# 3. Select the better result automatically
```

**Logging:**

During processing, you'll see:
```
[INFO] Trying transcription with languages: ['es', 'ru']
[INFO] Language 'es': transcribed 245 chars
[INFO] Language 'ru': transcribed 312 chars
[INFO] Selected best result: language='ru', length=312 chars
```

## Related Changes

- **Python 3.13 Compatibility**: Replaced `pydub` with `soundfile` for audio segment extraction
- **Improved Error Handling**: Better diagnostics for audio loading issues
- **Progress Indicators**: Added duration estimates for diarization process
- **Backward Compatibility**: All existing configurations continue to work unchanged

