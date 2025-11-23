# 🛡️ Error Handling in Agent01

## Overview

Agent01 has robust error handling to ensure that critical errors stop processing immediately, while allowing graceful interruption via Ctrl+C.

## Critical Errors (Stop All Processing)

The following errors are considered **FATAL** and will stop all processing immediately:

- `authentication` - Authentication failed
- `api_key` / `invalid_api_key` - Invalid or missing API key
- `insufficient_quota` - API quota exceeded
- `rate_limit_exceeded` - Rate limit reached
- `permission_denied` - Permission denied
- `invalid_request_error` - Invalid request format

### Behavior

When a critical error occurs:

1. **Parallel Mode**: 
   - Current chunks finish processing
   - Remaining chunks are cancelled
   - Script stops with error message
   
2. **Sequential Mode**:
   - Processing stops immediately
   - No more chunks are processed
   - Script exits with error code 1

### Example Output

```
[ERROR] Chunk 2/7 failed: Error code: 401 - {'error': {'message': 'Invalid API key'}}
[FATAL] Critical API error detected - stopping all processing
[FATAL] Processing stopped due to critical error: Invalid API key
[INFO] 1/7 chunks completed before error
```

## Non-Critical Errors

Non-critical errors (e.g., network timeouts) are handled differently:

- **Parallel Mode**: Other chunks continue processing
- **Sequential Mode**: Processing stops (safer approach)

## Testing Error Handling

Run the test script to verify error handling works correctly:

```bash
cd agent01
python test_error_handling.py
```

This will:
1. Test that invalid API keys stop the script
2. Provide instructions for testing Ctrl+C

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Error (API failure, invalid config, etc.) |
| 2 | Config file not found |

## Resume After Error

If processing encounters an error, you can resume by running the script again:

```bash
python -m cli.main
```

Already processed chunks will be loaded from cache, and only remaining chunks will be processed.

To force re-processing all chunks, set `clean_before_run: true` in config or delete the cache directory:

```bash
rm -rf cache/
```

## Configuration

Control error handling behavior via config:

```json
{
  "parallel_transcription_workers": 3,  // Use 1 for safer sequential mode
  "clean_before_run": false,            // Keep cache for resume
  "save_intermediate_results": true     // Save progress after each chunk
}
```

## Best Practices

1. **Start with sequential mode** (workers=1) when testing new configurations
2. **Enable intermediate results** to save progress
3. **Disable clean_before_run** for long processing jobs
4. **Monitor logs** for non-fatal errors

