#!/usr/bin/env python3
"""
CLI entry point for retranscribing a single sub-chunk.

Usage:
    python -m cli.retranscribe_sub --config retranscribe_config.json
"""
import os
import sys
import json
import argparse

# Ensure parent directory is in path for imports
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from core.config import Config
from core.models import ChunkInfo, TranscriptionResult
from services.api_client import OpenAITranscriptionClient

# Event marker for split progress (parsed by Node server)
SPLIT_MARKER = "@@SPLIT_EVENT"


def emit_split_event(event_type: str, **kwargs):
    """Emit a split event that the Node server can parse."""
    payload = {"event": event_type, **kwargs}
    print(f"{SPLIT_MARKER} {json.dumps(payload)}")
    sys.stdout.flush()


def main():
    parser = argparse.ArgumentParser(description="Retranscribe a single sub-chunk")
    parser.add_argument("--config", required=True, help="Path to config JSON file")
    args = parser.parse_args()
    
    if not os.path.exists(args.config):
        print(f"[ERROR] Config file not found: {args.config}")
        sys.exit(1)
    
    try:
        with open(args.config, 'r', encoding='utf-8') as f:
            config_data = json.load(f)
    except Exception as e:
        print(f"[ERROR] Failed to load config: {e}")
        sys.exit(1)
    
    # Extract configuration
    sub_chunk_audio_path = config_data.get("sub_chunk_audio_path")
    parent_chunk_idx = config_data.get("parent_chunk_idx")
    sub_chunk_idx = config_data.get("sub_chunk_idx")
    results_dir = config_data.get("results_dir")
    
    if not sub_chunk_audio_path or not os.path.exists(sub_chunk_audio_path):
        print(f"[ERROR] Sub-chunk audio file not found: {sub_chunk_audio_path}")
        sys.exit(1)
    
    if results_dir:
        os.makedirs(results_dir, exist_ok=True)
    
    # Initialize API client
    api_client = OpenAITranscriptionClient(
        api_key=config_data.get("openai_api_key"),
        base_url=config_data.get("openai_base_url"),
        organization=config_data.get("openai_organization"),
        model=config_data.get("model", "whisper-1"),
        fallback_models=config_data.get("fallback_models")
    )
    
    print(f"[INFO] Retranscribing sub-chunk #{parent_chunk_idx + 1}.{sub_chunk_idx + 1}")
    print(f"[INFO] Audio: {sub_chunk_audio_path}")
    
    # Emit started event
    emit_split_event("sub_chunk_started", parentIdx=parent_chunk_idx, subIdx=sub_chunk_idx)
    
    try:
        # Transcribe the sub-chunk
        raw_response = api_client.transcribe(
            audio_path=sub_chunk_audio_path,
            language=config_data.get("language"),
            prompt=config_data.get("prompt"),
            temperature=config_data.get("temperature"),
            response_format=config_data.get("response_format", "verbose_json"),
            timestamp_granularities=config_data.get("timestamp_granularities", ["segment"]),
            chunk_label=f"sub-chunk {sub_chunk_idx}"
        )
        
        print(f"[INFO] Transcription completed for sub-chunk #{parent_chunk_idx + 1}.{sub_chunk_idx + 1}")
        
        # Try to get offset and emit_guard from config or existing result
        offset = config_data.get("offset", 0.0)
        emit_guard = config_data.get("emit_guard", 0.0)
        
        # If not in config, try to load from existing result file
        if offset == 0.0 and emit_guard == 0.0 and results_dir:
            existing_result_path = os.path.join(results_dir, f"sub_chunk_{sub_chunk_idx:02d}_result.json")
            if os.path.exists(existing_result_path):
                try:
                    with open(existing_result_path, 'r', encoding='utf-8') as f:
                        existing_data = json.load(f)
                    offset = existing_data.get("offset", 0.0)
                    emit_guard = existing_data.get("emit_guard", 0.0)
                    print(f"[INFO] Loaded offset={offset:.2f}s, emit_guard={emit_guard:.2f}s from existing result")
                except Exception as e:
                    print(f"[WARN] Could not load offset/emit_guard from existing result: {e}")
        
        # Save result
        if results_dir:
            result_path = os.path.join(results_dir, f"sub_chunk_{sub_chunk_idx:02d}_result.json")
            result_data = {
                "sub_idx": sub_chunk_idx,
                "chunk_basename": os.path.basename(sub_chunk_audio_path),
                "offset": offset,
                "emit_guard": emit_guard,
                "raw_response": raw_response
            }
            
            with open(result_path, 'w', encoding='utf-8') as f:
                json.dump(result_data, f, ensure_ascii=False, indent=2)
            
            print(f"[INFO] Saved result to: {result_path} (offset={offset:.2f}s, emit_guard={emit_guard:.2f}s)")
        
        # Emit completed event
        emit_split_event("sub_chunk_completed", parentIdx=parent_chunk_idx, subIdx=sub_chunk_idx)
        
        print(f"[DONE] Sub-chunk #{parent_chunk_idx + 1}.{sub_chunk_idx + 1} retranscribed successfully")
        
    except Exception as e:
        print(f"[ERROR] Failed to transcribe sub-chunk: {e}")
        emit_split_event("sub_chunk_failed", parentIdx=parent_chunk_idx, subIdx=sub_chunk_idx, error=str(e))
        sys.exit(1)


if __name__ == "__main__":
    main()
