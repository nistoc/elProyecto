# Changelog v3.1: Workspace Organization

## Overview

Version 3.1 introduces a new workspace organization system that creates a dedicated folder for each audio file being processed. All intermediate files, outputs, and working files are now organized within file-specific workspaces.

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

## Related Changes

- **Python 3.13 Compatibility**: Replaced `pydub` with `soundfile` for audio segment extraction
- **Improved Error Handling**: Better diagnostics for audio loading issues
- **Progress Indicators**: Added duration estimates for diarization process

