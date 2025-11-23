#!/usr/bin/env python3
"""
Test script to verify error handling.
Run this to check that critical errors stop the script properly.
"""
import sys
import os

# Add parent directory to path
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from core import Config
from services import TranscriptionPipeline


def test_invalid_api_key():
    """Test that invalid API key stops the script."""
    print("\n" + "="*60)
    print("TEST 1: Invalid API Key")
    print("="*60)
    print("This should STOP after first chunk fails...\n")
    
    config_dict = {
        "file": "audio1210697591.m4a",
        "model": "gpt-4o-transcribe-diarize",
        "openai_api_key": "sk-INVALID_KEY_FOR_TESTING",
        "response_format": "diarized_json",
        "md_output_path": "test_output.md",
        "raw_json_output_path": "test_output.json",
        "pre_split": True,
        "target_chunk_mb": 2,
        "chunk_overlap_sec": 5.0,
        "split_workdir": "chunks",
        "cache_dir": "cache",
        "max_duration_minutes": 1,
        "parallel_transcription_workers": 3,
        "clean_before_run": False  # Don't clean to speed up test
    }
    
    try:
        config = Config(config_dict)
        pipeline = TranscriptionPipeline(config)
        results = pipeline.process_all_files()
        print("\n[FAIL] ❌ Script did NOT stop on invalid API key!")
        return False
    except RuntimeError as e:
        if "critical" in str(e).lower() or "api" in str(e).lower():
            print("\n[PASS] ✅ Script correctly stopped on critical API error!")
            print(f"[INFO] Error message: {e}")
            return True
        else:
            print(f"\n[FAIL] ❌ Wrong error type: {e}")
            return False
    except Exception as e:
        print(f"\n[PASS] ✅ Script stopped with error: {e}")
        return True


if __name__ == "__main__":
    print("Testing Agent01 Error Handling")
    print("="*60)
    
    # Test 1: Invalid API Key
    test1_passed = test_invalid_api_key()
    
    print("\n" + "="*60)
    print("SUMMARY")
    print("="*60)
    print(f"Test 1 (Invalid API Key): {'✅ PASS' if test1_passed else '❌ FAIL'}")
    print("="*60)

