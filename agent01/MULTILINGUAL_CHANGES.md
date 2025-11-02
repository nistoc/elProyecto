# Multi-Language Transcription Implementation

## Summary

Implemented a new `languages` parameter that allows specifying multiple languages for transcription. Instead of using a single `language` parameter or relying on auto-detection (`null`), the system now tries each specified language and selects the best result.

**Default languages**: Spanish (`es`) and Russian (`ru`)

## Changes Made

### 1. Core Functionality (`services/pipeline.py`)

**Modified `_transcribe_segment_whisper` method:**
- Now accepts multiple languages from `languages` config parameter
- Tries transcription with each specified language
- Compares results and selects the longest non-empty transcription
- Falls back to single `language` parameter for backward compatibility
- If no languages specified, uses auto-detection (`None`)

**Added new method `_transcribe_with_language`:**
- Handles transcription with a specific language hint
- Separated from main logic for clarity
- Supports optional prompt parameter

**Logging improvements:**
- Shows which languages are being tried
- Reports character count for each language attempt
- Displays selected language and result length

### 2. Configuration (`core/config.py`)

**Added default for `languages` parameter:**
```python
"languages": ["es", "ru"]  # Languages to try for transcription (v3.1+)
```

**Updated `print_plan` method:**
- Now displays `Languages: ['es', 'ru']` instead of `Language: null`
- Shows language list when configured
- Falls back to "Language: auto-detect" if not specified

### 3. Default Configuration (`config/default.json`)

**Changed from:**
```json
"language": null
```

**To:**
```json
"languages": ["es", "ru"]
```

### 4. Documentation Updates

**Updated files:**

1. **`docs/CHANGELOG_v3.1.md`**
   - Added "Multi-Language Transcription" to overview
   - New section explaining the feature
   - Examples of usage
   - Logging output examples

2. **`docs/BATCH_PROCESSING.md`**
   - Updated configuration examples
   - Added multilingual support explanation
   - Included note about default languages

3. **`docs/QUICK_START.md`**
   - Added multilingual information to batch processing section
   - Explained default behavior

4. **`README.md`**
   - Added to "Что нового в v3.1" section
   - Updated code examples
   - Added multilingual transcription explanation

## How It Works

### Algorithm

1. **Get language list** from `languages` config parameter
2. **Fallback**: If not specified, try single `language` parameter (backward compatibility)
3. **Auto-detect**: If no languages, use `None` (Whisper auto-detection)
4. **Try each language**:
   - Transcribe audio with `language="es"`
   - Transcribe audio with `language="ru"`
   - Store successful results
5. **Select best result**:
   - Compare by text length (longest non-empty wins)
   - Return the best transcription

### Example Process

```
Input: audio_segment.wav
Config: "languages": ["es", "ru"]

Step 1: Try language="es"
  → Result: "Hola, ¿cómo estás?" (20 chars)

Step 2: Try language="ru"  
  → Result: "Привет, как дела? Все хорошо." (31 chars)

Step 3: Compare results
  → Spanish: 20 chars
  → Russian: 31 chars
  
Step 4: Select Russian (longer result)
  → Return: Russian transcription
```

### Logging Example

```
[INFO] Trying transcription with languages: ['es', 'ru']
[INFO] Language 'es': transcribed 245 chars
[INFO] Language 'ru': transcribed 312 chars
[INFO] Selected best result: language='ru', length=312 chars
```

## Backward Compatibility

✅ **Fully backward compatible:**

**Old configuration (still works):**
```json
{
  "language": "es"
}
```
→ Will use single language "es"

**Old configuration (still works):**
```json
{
  "language": null
}
```
→ Will use auto-detection

**New configuration:**
```json
{
  "languages": ["es", "ru"]
}
```
→ Will try both languages and select best

## Benefits

1. **Better accuracy** for multilingual content (Spanish-Russian conversations)
2. **No guessing** which language will work better
3. **Automatic selection** based on transcription quality
4. **Flexible configuration** - can specify any number of languages
5. **Backward compatible** - existing configs continue to work

## Configuration Examples

### Minimal (uses defaults)

```json
{
  "input_dir": "taskstoparse",
  "use_diarization": true
}
```
→ Uses default `languages: ["es", "ru"]`

### Custom languages

```json
{
  "input_dir": "taskstoparse",
  "use_diarization": true,
  "languages": ["en", "de", "fr"]
}
```
→ Tries English, German, and French

### Auto-detection only

```json
{
  "input_dir": "taskstoparse",
  "use_diarization": true,
  "languages": []
}
```
→ Uses Whisper auto-detection

### Single language (backward compatible)

```json
{
  "input_dir": "taskstoparse",
  "use_diarization": true,
  "language": "es"
}
```
→ Uses only Spanish (old way, still works)

## Testing

The implementation has been tested with:
- ✅ Multiple languages configuration
- ✅ Single language fallback
- ✅ Auto-detection fallback
- ✅ Empty languages list
- ✅ Backward compatibility with `language` parameter
- ✅ Logging output
- ✅ Best result selection

## Performance Considerations

**Important:** This feature makes **N API calls per segment** where N is the number of languages.

Example:
- `languages: ["es", "ru"]` → 2 API calls per segment
- `languages: ["en", "de", "fr"]` → 3 API calls per segment

**Cost implications:**
- More API calls = higher cost
- But better accuracy for multilingual content
- Trade-off between cost and quality

**Recommendation:**
- Use 2-3 languages maximum for best balance
- Use only languages actually present in your audio
- Monitor API usage and costs

## Use Cases

### Perfect for:
- Spanish-Russian conversations ✅
- Multilingual interviews ✅
- Code-switching content ✅
- Unknown language mix ✅

### Not recommended for:
- Single-language content (use `language: "es"` instead)
- Many languages (3+ increases cost significantly)
- Already known language (direct specification is faster)

## Future Improvements

Potential enhancements:
1. Smart selection based on confidence scores (not just length)
2. Parallel API calls for faster processing
3. Language detection to skip unlikely languages
4. Result caching per language to avoid duplicate calls
5. Cost estimation before processing

## Files Modified

1. `services/pipeline.py` - Core transcription logic
2. `core/config.py` - Configuration defaults and display
3. `config/default.json` - Default configuration
4. `docs/CHANGELOG_v3.1.md` - Changelog documentation
5. `docs/BATCH_PROCESSING.md` - Batch processing guide
6. `docs/QUICK_START.md` - Quick start guide
7. `README.md` - Main documentation

## Testing Instructions

1. **Test with default languages:**
```bash
python -m cli.main --config config/default.json
```

2. **Check logs for language attempts:**
```
[INFO] Trying transcription with languages: ['es', 'ru']
[INFO] Language 'es': transcribed X chars
[INFO] Language 'ru': transcribed Y chars
[INFO] Selected best result: language='...', length=... chars
```

3. **Verify output files** contain correct transcriptions

4. **Test backward compatibility:**
   - Create config with `"language": "es"` (old way)
   - Verify it still works

## Conclusion

The multi-language transcription feature provides better accuracy for multilingual content while maintaining full backward compatibility. The default configuration targets Spanish-Russian content but can be easily customized for any language combination.

