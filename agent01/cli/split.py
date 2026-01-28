#!/usr/bin/env python3
"""
CLI entry point for split and re-transcribe operation.

This module handles splitting a problematic audio chunk into smaller parts,
transcribing each part separately, and merging the results.

Usage:
    python -m cli.split --config split_config.json
"""
import os
import sys
import json
import argparse
from typing import List, Tuple, Optional
from concurrent.futures import ThreadPoolExecutor, Future, TimeoutError as FuturesTimeoutError

# Ensure parent directory is in path for imports
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from core.config import Config
from core.models import ChunkInfo, TranscriptionResult, ASRSegment
from infrastructure.audio.splitter import ChunkSplitter
from infrastructure.audio.utils import AudioUtils
from infrastructure.merger import TranscriptionMerger
from infrastructure.cancellation import CancellationManager
from infrastructure.io import OutputWriter
from services.api_client import OpenAITranscriptionClient


# Event marker for split progress (parsed by Node server)
SPLIT_MARKER = "@@SPLIT_EVENT"


def emit_split_event(event_type: str, **kwargs):
    """Emit a split event that the Node server can parse."""
    payload = {"event": event_type, **kwargs}
    print(f"{SPLIT_MARKER} {json.dumps(payload)}")
    sys.stdout.flush()


class SplitTranscriber:
    """
    Handles the complete split-and-transcribe workflow.
    
    1. Split the problematic chunk into N sub-chunks
    2. Transcribe each sub-chunk
    3. Merge results back together
    """
    
    def __init__(self, config: dict):
        """
        Initialize the split transcriber.
        
        Args:
            config: Configuration dictionary with split parameters
        """
        self.config = config
        self.chunk_audio_path = config["chunk_audio_path"]
        self.parent_chunk_idx = config["parent_chunk_idx"]
        self.parts = config["parts"]
        self.sub_chunks_dir = config["sub_chunks_dir"]
        self.results_dir = config["results_dir"]
        self.cancel_dir = config.get("cancel_dir", "cancel_signals")
        
        # Main transcript files for integration
        self.main_transcript_path = config.get("main_transcript_path")
        self.main_json_path = config.get("main_json_path")
        
        # Initialize components
        self._init_splitter()
        self._init_api_client()
        self._init_cancellation()
        self._init_merger()
        self._init_output_writer()
    
    def _init_splitter(self):
        """Initialize audio splitter."""
        ffmpeg = AudioUtils.which_or(self.config.get("ffmpeg_path"), "ffmpeg")
        ffprobe = AudioUtils.which_or(self.config.get("ffprobe_path"), "ffprobe")
        
        if not ffmpeg or not ffprobe:
            raise RuntimeError("ffmpeg/ffprobe not found")
        
        self.splitter = ChunkSplitter(ffmpeg, ffprobe)
    
    def _init_api_client(self):
        """Initialize OpenAI API client."""
        # Prefer environment variable over config value
        api_key = os.environ.get("OPENAI_API_KEY") or self.config.get("openai_api_key")
        
        # Handle "env:..." format from config
        if api_key and api_key.startswith("env:"):
            env_var_name = api_key.split(":", 1)[1]
            api_key = os.environ.get(env_var_name)
        
        self.api_client = OpenAITranscriptionClient(
            api_key=api_key,
            base_url=self.config.get("openai_base_url"),
            organization=self.config.get("openai_organization"),
            model=self.config.get("model", "whisper-1"),
            fallback_models=self.config.get("fallback_models")
        )
    
    def _init_cancellation(self):
        """Initialize cancellation manager."""
        self.cancel_manager = CancellationManager(self.cancel_dir)
    
    def _init_merger(self):
        """Initialize transcription merger."""
        self.merger = TranscriptionMerger(
            overlap_sec=self.config.get("overlap_sec", 1.0)
        )
    
    def _init_output_writer(self):
        """Initialize output writer for transcript integration."""
        self.output_writer = OutputWriter()
    
    def is_sub_chunk_cancelled(self, sub_idx: int) -> bool:
        """Check if a sub-chunk has been cancelled."""
        # Check for sub-chunk specific cancel flag
        flag_path = os.path.join(self.cancel_dir, f"cancel_sub_{sub_idx}.flag")
        return os.path.exists(flag_path)
    
    def _wait_for_transcription_with_cancel_check(
        self,
        future: Future,
        sub_idx: int,
        check_interval: float = 1.0,
        max_wait: float = 300.0
    ) -> Tuple[Optional[TranscriptionResult], str]:
        """
        Wait for transcription to complete while checking for cancellation.
        
        Args:
            future: Future object from thread pool
            sub_idx: Sub-chunk index for cancellation check
            check_interval: How often to check cancellation (seconds)
            max_wait: Maximum time to wait (seconds)
        
        Returns:
            Tuple of (result or None, status: 'completed'|'cancelled'|'failed'|'timeout')
        """
        elapsed = 0.0
        
        while elapsed < max_wait:
            # Check if cancelled
            if self.is_sub_chunk_cancelled(sub_idx):
                print(f"[INFO] Sub-chunk {sub_idx} cancelled during transcription")
                # Don't wait for future, just return
                return None, "cancelled"
            
            try:
                # Try to get result with short timeout
                result = future.result(timeout=check_interval)
                return result, "completed"
            except FuturesTimeoutError:
                # Not done yet, continue waiting
                elapsed += check_interval
                continue
            except Exception as e:
                print(f"[ERROR] Transcription failed: {e}")
                return None, "failed"
        
        print(f"[WARN] Sub-chunk {sub_idx} transcription timed out after {max_wait}s")
        return None, "timeout"
    
    def run(self) -> Optional[TranscriptionResult]:
        """
        Run the complete split-and-transcribe workflow.
        
        Returns:
            Merged TranscriptionResult, or None if completely failed
        """
        print(f"\n{'='*60}")
        print(f"[SPLIT] Starting split-and-transcribe for chunk {self.parent_chunk_idx}")
        print(f"[SPLIT] Audio: {self.chunk_audio_path}")
        print(f"[SPLIT] Parts: {self.parts}")
        print(f"{'='*60}\n")
        
        # Step 1: Split the chunk
        print("[STEP 1] Splitting audio chunk...")
        try:
            sub_chunks = self.splitter.split_chunk(
                chunk_path=self.chunk_audio_path,
                parts=self.parts,
                output_dir=self.sub_chunks_dir,
                overlap_sec=self.config.get("overlap_sec", 1.0)
            )
        except Exception as e:
            print(f"[ERROR] Failed to split chunk: {e}")
            emit_split_event("split_failed", error=str(e))
            raise
        
        # Emit event with sub-chunks info
        sub_chunks_info = [
            {"idx": i, "status": "pending", "audioPath": sc.path}
            for i, sc in enumerate(sub_chunks)
        ]
        emit_split_event("sub_chunks_created", subChunks=sub_chunks_info)
        
        # Step 2: Transcribe each sub-chunk
        print(f"\n[STEP 2] Transcribing {len(sub_chunks)} sub-chunks...")
        sub_results: List[Tuple[int, TranscriptionResult]] = []
        failed_sub_chunks: List[int] = []
        cancelled_sub_chunks: List[int] = []
        
        # Use thread pool for interruptible transcription
        with ThreadPoolExecutor(max_workers=1) as executor:
            for i, sub_chunk in enumerate(sub_chunks):
                # Check cancellation before starting
                if self.is_sub_chunk_cancelled(i):
                    print(f"[INFO] Sub-chunk {i} already cancelled")
                    emit_split_event("sub_chunk_cancelled", subIdx=i)
                    cancelled_sub_chunks.append(i)
                    continue
                
                print(f"\n--- Transcribing sub-chunk {i+1}/{len(sub_chunks)} ---")
                emit_split_event("sub_chunk_started", subIdx=i)
                
                # Submit transcription to thread pool
                future = executor.submit(self._transcribe_sub_chunk, sub_chunk, i)
                
                # Wait with cancellation checks
                api_timeout = self.config.get("api_timeout_seconds", 240)
                result, status = self._wait_for_transcription_with_cancel_check(
                    future=future,
                    sub_idx=i,
                    check_interval=1.0,
                    max_wait=float(api_timeout) + 30  # Add buffer
                )
                
                if status == "completed" and result is not None:
                    sub_results.append((i, result))
                    emit_split_event("sub_chunk_completed", subIdx=i)
                    self._save_sub_result(result, i)
                    
                elif status == "cancelled":
                    emit_split_event("sub_chunk_cancelled", subIdx=i)
                    cancelled_sub_chunks.append(i)
                    # Continue to next sub-chunk immediately
                    
                elif status == "failed":
                    emit_split_event("sub_chunk_failed", subIdx=i, error="Transcription failed")
                    failed_sub_chunks.append(i)
                    
                elif status == "timeout":
                    emit_split_event("sub_chunk_failed", subIdx=i, error="Transcription timed out")
                    failed_sub_chunks.append(i)
        
        # Check results
        if not sub_results:
            print("[ERROR] No sub-chunks were successfully transcribed")
            emit_split_event("merge_failed", error="No successful transcriptions")
            return None
        
        if failed_sub_chunks:
            print(f"[WARN] Failed sub-chunks: {failed_sub_chunks}")
        if cancelled_sub_chunks:
            print(f"[INFO] Cancelled sub-chunks: {cancelled_sub_chunks}")
        
        # Step 3: Merge results
        print(f"\n[STEP 3] Merging {len(sub_results)} transcriptions...")
        emit_split_event("merging")
        
        try:
            # Get parent chunk offset (from original chunk's position in full audio)
            parent_offset = self.config.get("parent_chunk_offset", 0.0)
            
            merged_result = self.merger.merge_transcriptions(
                sub_results=sub_results,
                parent_chunk_offset=parent_offset
            )
            
            # Save merged result
            merged_path = os.path.join(
                self.results_dir, 
                f"chunk_{self.parent_chunk_idx}_merged.json"
            )
            self.merger.save_merged_result(merged_result, merged_path, format="json")
            
            # Also save as markdown
            merged_md_path = os.path.join(
                self.results_dir,
                f"chunk_{self.parent_chunk_idx}_merged.md"
            )
            self.merger.save_merged_result(merged_result, merged_md_path, format="md")
            
            # Extract merged text for event
            merged_text = " ".join(seg.text for seg in merged_result.segments)
            
            # Step 4: Integrate into main transcript files
            print(f"\n[STEP 4] Integrating into main transcript...")
            
            integration_success = True
            
            if self.main_transcript_path and os.path.exists(self.main_transcript_path):
                print(f"[INFO] Inserting into: {self.main_transcript_path}")
                success = self.output_writer.insert_split_result_into_transcript(
                    md_path=self.main_transcript_path,
                    segments=merged_result.segments,
                    chunk_offset=parent_offset,
                    emit_guard=merged_result.emit_guard
                )
                if success:
                    print(f"[OK] Successfully integrated into transcript.md")
                else:
                    print(f"[WARN] Failed to integrate into transcript.md")
                    integration_success = False
            else:
                print(f"[WARN] Main transcript path not provided or doesn't exist")
                integration_success = False
            
            if self.main_json_path:
                print(f"[INFO] Updating: {self.main_json_path}")
                success = self.output_writer.insert_split_result_into_json(
                    json_path=self.main_json_path,
                    result=merged_result,
                    chunk_index=self.parent_chunk_idx
                )
                if success:
                    print(f"[OK] Successfully updated response.json")
                else:
                    print(f"[WARN] Failed to update response.json")
            
            emit_split_event(
                "merge_completed",
                mergedText=merged_text[:500] + "..." if len(merged_text) > 500 else merged_text,
                segmentCount=len(merged_result.segments),
                failedSubChunks=failed_sub_chunks,
                cancelledSubChunks=cancelled_sub_chunks,
                integrated=integration_success
            )
            
            print(f"\n[DONE] Split-and-transcribe completed successfully")
            print(f"  Merged segments: {len(merged_result.segments)}")
            print(f"  Failed sub-chunks: {len(failed_sub_chunks)}")
            print(f"  Cancelled sub-chunks: {len(cancelled_sub_chunks)}")
            print(f"  Integrated into main transcript: {'Yes' if integration_success else 'No'}")
            
            return merged_result
            
        except Exception as e:
            print(f"[ERROR] Failed to merge transcriptions: {e}")
            emit_split_event("merge_failed", error=str(e))
            raise
    
    def _transcribe_sub_chunk(
        self, 
        sub_chunk: ChunkInfo, 
        sub_idx: int
    ) -> TranscriptionResult:
        """
        Transcribe a single sub-chunk.
        
        Args:
            sub_chunk: ChunkInfo for the sub-chunk
            sub_idx: Index of the sub-chunk
        
        Returns:
            TranscriptionResult for this sub-chunk
        """
        print(f"[API] Sending sub-chunk {sub_idx} to API...")
        
        raw_response = self.api_client.transcribe(
            audio_path=sub_chunk.path,
            language=self.config.get("language"),
            prompt=self.config.get("prompt"),
            temperature=self.config.get("temperature"),
            response_format=self.config.get("response_format", "verbose_json"),
            timestamp_granularities=self.config.get("timestamp_granularities", ["segment"]),
            chunk_label=f"sub-chunk {sub_idx}"
        )
        
        print(f"[API] Sub-chunk {sub_idx} response received")
        
        # Parse segments
        segments = self.api_client.parse_segments(raw_response)
        
        return TranscriptionResult(
            chunk_basename=os.path.basename(sub_chunk.path),
            offset=sub_chunk.offset,
            emit_guard=sub_chunk.emit_guard,
            segments=segments,
            raw_response=raw_response
        )
    
    def _save_sub_result(self, result: TranscriptionResult, sub_idx: int):
        """Save a sub-chunk transcription result."""
        result_path = os.path.join(
            self.results_dir,
            f"sub_chunk_{sub_idx:02d}_result.json"
        )
        
        data = {
            "sub_idx": sub_idx,
            "chunk_basename": result.chunk_basename,
            "offset": result.offset,
            "emit_guard": result.emit_guard,
            "segments": [
                {
                    "start": seg.start,
                    "end": seg.end,
                    "text": seg.text,
                    "speaker": seg.speaker
                }
                for seg in result.segments
            ],
            "raw_response": result.raw_response
        }
        
        os.makedirs(self.results_dir, exist_ok=True)
        with open(result_path, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        
        print(f"[INFO] Saved sub-chunk {sub_idx} result: {result_path}")


def main():
    """Main entry point for split CLI."""
    parser = argparse.ArgumentParser(
        description="Split and re-transcribe a problematic audio chunk"
    )
    parser.add_argument(
        "--config",
        required=True,
        help="Path to split configuration JSON file"
    )
    
    args = parser.parse_args()
    
    # Load config
    if not os.path.isfile(args.config):
        print(f"[ERROR] Config file not found: {args.config}")
        sys.exit(1)
    
    with open(args.config, "r", encoding="utf-8") as f:
        config = json.load(f)
    
    # Validate required fields
    required_fields = ["chunk_audio_path", "parent_chunk_idx", "parts", 
                       "sub_chunks_dir", "results_dir"]
    for field in required_fields:
        if field not in config:
            print(f"[ERROR] Missing required config field: {field}")
            sys.exit(1)
    
    # Run split transcriber
    try:
        transcriber = SplitTranscriber(config)
        result = transcriber.run()
        
        if result is None:
            print("[ERROR] Split-and-transcribe failed completely")
            sys.exit(1)
        
        sys.exit(0)
        
    except Exception as e:
        print(f"[FATAL] Split-and-transcribe failed: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)


if __name__ == "__main__":
    main()

