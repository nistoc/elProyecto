#!/usr/bin/env python3
"""
Rebuild transcript.md from individual chunk transcripts and split chunk transcripts.

This script:
1. Reads all chunk transcription results (from intermediate_results or chunks_json)
2. Reads all split chunk transcription results
3. Combines them in the correct order based on chunk indices and offsets
4. Writes the combined transcript to transcript.md
"""
import os
import json
import sys
import argparse
import re
from pathlib import Path
from typing import List, Tuple, Optional

# Add parent directory to path to import modules
sys.path.insert(0, str(Path(__file__).parent.parent))

from core.models import TranscriptionResult, ASRSegment
from infrastructure.io.writers import OutputWriter
from infrastructure.merger import TranscriptionMerger


def load_chunk_result(result_path: str, chunk_idx: int) -> Optional[TranscriptionResult]:
    """Load a chunk transcription result from JSON file."""
    try:
        with open(result_path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        
        segments = [
            ASRSegment(
                start=seg["start"],
                end=seg["end"],
                text=seg["text"],
                speaker=seg.get("speaker")
            )
            for seg in data.get("segments", [])
        ]
        
        # Fallback: if no segments, try raw_response
        if not segments:
            raw_response = data.get("raw_response")
            if isinstance(raw_response, dict) and "text" in raw_response:
                txt = (raw_response.get("text") or "").strip()
                if txt:
                    segments.append(ASRSegment(0.0, 0.0, txt, None))
            elif isinstance(raw_response, dict) and "segments" in raw_response:
                if isinstance(raw_response["segments"], list):
                    for s in raw_response["segments"]:
                        start = max(0.0, float(s.get("start", 0.0)))
                        end = max(start, float(s.get("end", start)))
                        text = (s.get("text") or "").strip()
                        speaker = s.get("speaker") or s.get("speaker_label")
                        if text:
                            segments.append(ASRSegment(start, end, text, speaker))
        
        return TranscriptionResult(
            chunk_basename=data.get("chunk_basename", f"chunk_{chunk_idx}"),
            offset=data.get("offset", 0.0),
            emit_guard=data.get("emit_guard", 0.0),
            segments=segments,
            raw_response=data.get("raw_response")
        )
    except Exception as e:
        print(f"[WARN] Failed to load chunk result from {result_path}: {e}")
        return None


def find_chunk_results(job_dir: str) -> List[Tuple[int, TranscriptionResult]]:
    """Find all chunk transcription results and return them sorted by chunk index."""
    results: List[Tuple[int, TranscriptionResult]] = []
    
    # Check intermediate_results directory
    intermediate_dir = os.path.join(job_dir, "intermediate_results")
    if os.path.exists(intermediate_dir):
        for filename in os.listdir(intermediate_dir):
            if filename.endswith("_result.json"):
                # Extract chunk index from filename: base_name_chunk_XXX_result.json
                match = re.search(r"_chunk_(\d+)_result\.json$", filename)
                if match:
                    chunk_idx = int(match.group(1))
                    result_path = os.path.join(intermediate_dir, filename)
                    result = load_chunk_result(result_path, chunk_idx)
                    if result:
                        results.append((chunk_idx, result))
    
    # Also check chunks_json directory
    chunks_json_dir = os.path.join(job_dir, "chunks_json")
    if os.path.exists(chunks_json_dir):
        for filename in os.listdir(chunks_json_dir):
            if filename.endswith(".json"):
                # Extract chunk index from filename: base_name_part_XXX.json
                match = re.match(r".*_part_(\d+)\.json$", filename)
                if not match:
                    # Try alternative pattern: base_name_chunk_XXX.json
                    match = re.match(r".*_chunk_(\d+)\.json$", filename)
                if match:
                    chunk_idx = int(match.group(1))
                    result_path = os.path.join(chunks_json_dir, filename)
                    # chunks_json files contain raw_response, need to parse segments
                    try:
                        with open(result_path, 'r', encoding='utf-8') as f:
                            data = json.load(f)
                        
                        # Parse segments from raw_response
                        segments = []
                        raw_response = data.get("response") or data
                        if isinstance(raw_response, dict) and "segments" in raw_response:
                            if isinstance(raw_response["segments"], list):
                                for s in raw_response["segments"]:
                                    start = max(0.0, float(s.get("start", 0.0)))
                                    end = max(start, float(s.get("end", start)))
                                    text = (s.get("text") or "").strip()
                                    speaker = s.get("speaker") or s.get("speaker_label")
                                    if text:
                                        segments.append(ASRSegment(start, end, text, speaker))
                        
                        if segments:
                            # Get offset from data or calculate from chunk index
                            offset = data.get("offset", 0.0)
                            emit_guard = data.get("emit_guard", 0.0)
                            
                            result = TranscriptionResult(
                                chunk_basename=filename,
                                offset=offset,
                                emit_guard=emit_guard,
                                segments=segments,
                                raw_response=raw_response
                            )
                            results.append((chunk_idx, result))
                    except Exception as e:
                        print(f"[WARN] Failed to load chunk JSON from {result_path}: {e}")
    
    return results


def find_split_chunk_results(job_dir: str) -> List[Tuple[int, TranscriptionResult]]:
    """Find all split chunk transcription results and return them sorted by parent chunk index."""
    results: List[Tuple[int, TranscriptionResult]] = []
    merger = TranscriptionMerger()
    
    split_chunks_dir = os.path.join(job_dir, "split_chunks")
    if not os.path.exists(split_chunks_dir):
        return results
    
    # Iterate through all chunk_X directories
    for item in os.listdir(split_chunks_dir):
        chunk_dir = os.path.join(split_chunks_dir, item)
        if not os.path.isdir(chunk_dir) or not item.startswith("chunk_"):
            continue
        
        # Extract parent chunk index
        match = re.match(r"chunk_(\d+)$", item)
        if not match:
            continue
        
        parent_idx = int(match.group(1))
        results_dir = os.path.join(chunk_dir, "results")
        
        if not os.path.exists(results_dir):
            continue
        
        # Find all sub_chunk_XX_result.json files
        sub_results: List[Tuple[int, TranscriptionResult]] = []
        found_sub_indices = set()
        
        # First, find all existing result files
        for filename in os.listdir(results_dir):
            if filename.startswith("sub_chunk_") and filename.endswith("_result.json"):
                match = re.match(r"sub_chunk_(\d+)_result\.json$", filename)
                if match:
                    sub_idx = int(match.group(1))
                    found_sub_indices.add(sub_idx)
                    result_path = os.path.join(results_dir, filename)
                    sub_result = TranscriptionMerger.load_sub_result(result_path, sub_idx)
                    if sub_result:
                        sub_idx_actual, result = sub_result
                        sub_results.append((sub_idx_actual, result))
                        print(f"[REBUILD] Found transcript for chunk {parent_idx}, sub-chunk {sub_idx_actual}: {len(result.segments)} segments")
                    else:
                        print(f"[WARN] Failed to load result for chunk {parent_idx}, sub-chunk {sub_idx}")
        
        if not sub_results:
            print(f"[WARN] No sub-chunk results found for chunk {parent_idx}, skipping")
            continue
        
        # Log which sub-chunks are missing
        if found_sub_indices:
            expected_count = max(found_sub_indices) + 1
            missing = [i for i in range(expected_count) if i not in found_sub_indices]
            if missing:
                print(f"[WARN] Chunk {parent_idx}: Missing sub-chunk transcripts: {missing}")
            print(f"[REBUILD] Chunk {parent_idx}: Found {len(sub_results)}/{expected_count} sub-chunk transcripts")
        
        # Get parent chunk offset from intermediate result or calculate from neighboring chunks
        parent_offset = 0.0
        intermediate_dir = os.path.join(job_dir, "intermediate_results")
        if os.path.exists(intermediate_dir):
            # Try to find parent chunk's intermediate result
            found = False
            for filename in os.listdir(intermediate_dir):
                match = re.match(r".*_chunk_(\d+)_result\.json$", filename)
                if match and int(match.group(1)) == parent_idx:
                    result_path = os.path.join(intermediate_dir, filename)
                    try:
                        with open(result_path, 'r', encoding='utf-8') as f:
                            data = json.load(f)
                        parent_offset = data.get("offset", 0.0)
                        found = True
                        print(f"[REBUILD] Found parent offset for chunk {parent_idx}: {parent_offset:.2f}s")
                        break
                    except Exception as e:
                        print(f"[WARN] Failed to load intermediate result for chunk {parent_idx}: {e}")
            
            # If not found, try to estimate from previous chunk
            if not found:
                print(f"[WARN] No intermediate result for chunk {parent_idx}, trying to estimate offset from previous chunk...")
                # Find the previous chunk's offset
                prev_chunk_idx = parent_idx - 1
                while prev_chunk_idx >= 0:
                    for filename in os.listdir(intermediate_dir):
                        match = re.match(r".*_chunk_(\d+)_result\.json$", filename)
                        if match and int(match.group(1)) == prev_chunk_idx:
                            result_path = os.path.join(intermediate_dir, filename)
                            try:
                                with open(result_path, 'r', encoding='utf-8') as f:
                                    data = json.load(f)
                                prev_offset = data.get("offset", 0.0)
                                # Estimate: assume chunks are ~60 seconds each
                                # This is a rough estimate, but better than 0.0
                                estimated_duration = 60.0
                                parent_offset = prev_offset + estimated_duration
                                print(f"[WARN] Estimated parent offset for chunk {parent_idx}: {parent_offset:.2f}s (from chunk {prev_chunk_idx})")
                                break
                            except:
                                pass
                    else:
                        prev_chunk_idx -= 1
                        continue
                    break
        
        # Merge sub-chunk results
        try:
            merged_result = merger.merge_transcriptions(
                sub_results=sub_results,
                parent_chunk_offset=parent_offset
            )
            results.append((parent_idx, merged_result))
        except Exception as e:
            print(f"[WARN] Failed to merge split chunks for chunk {parent_idx}: {e}")
    
    return results


def rebuild_transcript(job_dir: str, output_path: Optional[str] = None) -> str:
    """
    Rebuild transcript.md from all available chunk and split chunk transcripts.
    
    Args:
        job_dir: Directory containing the job files
        output_path: Optional path for output file (defaults to job_dir/transcript.md)
    
    Returns:
        Path to the rebuilt transcript file
    """
    if output_path is None:
        output_path = os.path.join(job_dir, "transcript.md")
    
    print(f"[REBUILD] Rebuilding transcript from chunks...")
    print(f"[REBUILD] Job directory: {job_dir}")
    print(f"[REBUILD] Output: {output_path}")
    
    # Find all chunk results
    chunk_results = find_chunk_results(job_dir)
    print(f"[REBUILD] Found {len(chunk_results)} chunk results")
    
    # Find all split chunk results
    split_results = find_split_chunk_results(job_dir)
    print(f"[REBUILD] Found {len(split_results)} split chunk results")
    
    # Combine and sort by chunk index
    all_results: List[Tuple[int, TranscriptionResult, bool]] = []
    
    # Add regular chunks
    for chunk_idx, result in chunk_results:
        all_results.append((chunk_idx, result, False))
    
    # Add split chunks (they replace regular chunks if they exist)
    split_chunk_indices = {idx for idx, _ in split_results}
    for chunk_idx, result in split_results:
        # Remove regular chunk if split chunk exists
        all_results = [(idx, res, is_split) for idx, res, is_split in all_results 
                       if idx != chunk_idx or is_split]
        all_results.append((chunk_idx, result, True))
    
    # Sort by chunk index
    all_results.sort(key=lambda x: x[0])
    
    print(f"[REBUILD] Total chunks to process: {len(all_results)}")
    
    # Initialize output writer
    writer = OutputWriter()
    writer.initialize_markdown(output_path)
    writer.reset_speaker_map()
    
    # Write all results in order
    for chunk_idx, result, is_split in all_results:
        print(f"[REBUILD] Processing chunk {chunk_idx} ({'split' if is_split else 'regular'})...")
        writer.append_segments_to_markdown(
            output_path,
            result.segments,
            result.offset,
            result.emit_guard
        )
    
    # Finalize
    writer.finalize_markdown(output_path)
    
    print(f"[REBUILD] ✅ Transcript rebuilt successfully: {output_path}")
    return output_path


def main():
    parser = argparse.ArgumentParser(description="Rebuild transcript.md from chunk transcripts")
    parser.add_argument("job_dir", help="Job directory containing chunk results")
    parser.add_argument("--output", "-o", help="Output file path (default: job_dir/transcript.md)")
    
    args = parser.parse_args()
    
    if not os.path.exists(args.job_dir):
        print(f"[ERROR] Job directory not found: {args.job_dir}")
        sys.exit(1)
    
    try:
        rebuild_transcript(args.job_dir, args.output)
    except Exception as e:
        print(f"[ERROR] Failed to rebuild transcript: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)


if __name__ == "__main__":
    main()
