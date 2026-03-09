#!/usr/bin/env python3
"""
Transcription merger for combining sub-chunk transcriptions.

After splitting a problematic chunk and transcribing each sub-chunk,
this module merges the results back into a single coherent transcription.
"""
import os
import json
from typing import List, Tuple, Optional
from dataclasses import dataclass
from core.models import TranscriptionResult, ASRSegment


class TranscriptionMerger:
    """Merges transcriptions from sub-chunks into a single result."""
    
    def __init__(self, overlap_sec: float = 1.0):
        """
        Initialize the merger.
        
        Args:
            overlap_sec: Expected overlap duration between sub-chunks
        """
        self.overlap_sec = overlap_sec
    
    def merge_transcriptions(
        self,
        sub_results: List[Tuple[int, TranscriptionResult]],
        parent_chunk_offset: float = 0.0
    ) -> TranscriptionResult:
        """
        Merge sub-chunk transcriptions into a single result.
        
        Args:
            sub_results: List of (sub_idx, TranscriptionResult) pairs
            parent_chunk_offset: The parent chunk's offset in the full audio
        
        Returns:
            Combined TranscriptionResult with adjusted timings
        """
        if not sub_results:
            raise ValueError("No sub-results to merge")
        
        # Sort by sub-index to ensure correct order
        sorted_results = sorted(sub_results, key=lambda x: x[0])
        
        print(f"[MERGE] Merging {len(sorted_results)} sub-chunk transcriptions")
        print(f"[MERGE] Parent chunk offset: {parent_chunk_offset:.2f}s")
        
        merged_segments: List[ASRSegment] = []
        
        for sub_idx, result in sorted_results:
            print(f"[MERGE] Processing sub-chunk {sub_idx}: "
                  f"offset={result.offset:.2f}s, emit_guard={result.emit_guard:.2f}s, "
                  f"segments={len(result.segments)}")
            
            for segment in result.segments:
                # Skip segments in the overlap region (emit_guard)
                # BUT: if segment has zero timestamps (0.0, 0.0), it means it's a fallback from raw_response.text
                # In that case, we should include it regardless of emit_guard
                is_fallback_segment = (segment.start == 0.0 and segment.end == 0.0)
                
                if not is_fallback_segment and segment.start < result.emit_guard:
                    # Check if segment extends beyond emit_guard
                    if segment.end <= result.emit_guard:
                        # Completely in overlap - skip
                        continue
                    # Partially in overlap - we still include it but note this
                    # The transcription API usually aligns to word boundaries
                
                # Adjust timing:
                # segment.start/end are relative to sub-chunk
                # result.offset is sub-chunk's offset relative to parent chunk
                # parent_chunk_offset is parent chunk's offset in full audio
                adjusted_start = segment.start + result.offset + parent_chunk_offset
                adjusted_end = segment.end + result.offset + parent_chunk_offset
                
                merged_segment = ASRSegment(
                    start=adjusted_start,
                    end=adjusted_end,
                    text=segment.text,
                    speaker=segment.speaker
                )
                merged_segments.append(merged_segment)
        
        # Sort by start time
        merged_segments.sort(key=lambda s: s.start)
        
        # Remove duplicate segments (may occur in overlap regions)
        merged_segments = self._deduplicate_segments(merged_segments)
        
        print(f"[MERGE] Merged result: {len(merged_segments)} segments")
        
        # Create merged result
        first_basename = sorted_results[0][1].chunk_basename
        return TranscriptionResult(
            chunk_basename=f"merged_{first_basename}",
            offset=parent_chunk_offset,
            emit_guard=0.0,  # Already handled in parent
            segments=merged_segments,
            raw_response=None
        )
    
    def _deduplicate_segments(
        self, 
        segments: List[ASRSegment],
        time_threshold: float = 0.5,
        text_similarity_threshold: float = 0.8
    ) -> List[ASRSegment]:
        """
        Remove duplicate segments that may appear in overlap regions.
        
        Two segments are considered duplicates if:
        - Their start times are within time_threshold of each other
        - Their texts are very similar (based on text_similarity_threshold)
        
        Args:
            segments: List of segments sorted by start time
            time_threshold: Maximum time difference to consider as potential duplicate
            text_similarity_threshold: Minimum similarity ratio to consider as duplicate
        
        Returns:
            Deduplicated list of segments
        """
        if len(segments) <= 1:
            return segments
        
        result = [segments[0]]
        
        for segment in segments[1:]:
            prev_segment = result[-1]
            
            # Check if this might be a duplicate
            time_diff = abs(segment.start - prev_segment.start)
            
            if time_diff < time_threshold:
                # Check text similarity
                similarity = self._text_similarity(
                    prev_segment.text.strip().lower(),
                    segment.text.strip().lower()
                )
                
                if similarity >= text_similarity_threshold:
                    # This is likely a duplicate - keep the longer one
                    if len(segment.text) > len(prev_segment.text):
                        result[-1] = segment
                    continue
            
            result.append(segment)
        
        if len(result) < len(segments):
            print(f"[MERGE] Deduplicated: {len(segments)} -> {len(result)} segments")
        
        return result
    
    def _text_similarity(self, text1: str, text2: str) -> float:
        """
        Calculate simple similarity ratio between two texts.
        Uses longest common subsequence approach.
        
        Args:
            text1: First text
            text2: Second text
        
        Returns:
            Similarity ratio between 0 and 1
        """
        if not text1 or not text2:
            return 0.0
        
        if text1 == text2:
            return 1.0
        
        # Simple approach: compare word sets
        words1 = set(text1.split())
        words2 = set(text2.split())
        
        if not words1 or not words2:
            return 0.0
        
        intersection = len(words1 & words2)
        union = len(words1 | words2)
        
        return intersection / union if union > 0 else 0.0
    
    def save_merged_result(
        self,
        result: TranscriptionResult,
        output_path: str,
        format: str = "json"
    ):
        """
        Save merged transcription result to file.
        
        Args:
            result: The merged TranscriptionResult
            output_path: Path to save the result
            format: Output format ('json' or 'md')
        """
        os.makedirs(os.path.dirname(output_path), exist_ok=True)
        
        if format == "json":
            data = {
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
                ]
            }
            with open(output_path, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
        
        elif format == "md":
            with open(output_path, "w", encoding="utf-8") as f:
                for seg in result.segments:
                    timestamp = self._format_timestamp(seg.start)
                    speaker = f"**{seg.speaker}**: " if seg.speaker else ""
                    f.write(f"[{timestamp}] {speaker}{seg.text}\n\n")
        
        print(f"[MERGE] Saved merged result to: {output_path}")
    
    def _format_timestamp(self, seconds: float) -> str:
        """Format seconds as MM:SS.mmm"""
        minutes = int(seconds // 60)
        secs = seconds % 60
        return f"{minutes:02d}:{secs:06.3f}"
    
    @staticmethod
    def load_sub_result(result_path: str, sub_idx: int) -> Tuple[int, TranscriptionResult]:
        """
        Load a sub-chunk transcription result from file.
        
        If segments are missing or empty, falls back to raw_response.text.
        
        Args:
            result_path: Path to the JSON result file
            sub_idx: Index of the sub-chunk
        
        Returns:
            Tuple of (sub_idx, TranscriptionResult)
        """
        with open(result_path, "r", encoding="utf-8") as f:
            data = json.load(f)
        
        # Try to extract segments from segments array
        segments = [
            ASRSegment(
                start=seg["start"],
                end=seg["end"],
                text=seg["text"],
                speaker=seg.get("speaker")
            )
            for seg in data.get("segments", [])
        ]
        
        # Fallback: if no segments found, try to extract from raw_response.text
        if not segments:
            raw_response = data.get("raw_response")
            if isinstance(raw_response, dict) and "text" in raw_response:
                txt = (raw_response.get("text") or "").strip()
                if txt:
                    print(f"[MERGE] No segments found for sub-chunk {sub_idx}, using raw_response.text")
                    segments.append(ASRSegment(0.0, 0.0, txt, None))
            elif isinstance(raw_response, dict) and "segments" in raw_response:
                # Also check if segments are in raw_response
                if isinstance(raw_response["segments"], list):
                    for s in raw_response["segments"]:
                        start = max(0.0, float(s.get("start", 0.0)))
                        end = max(start, float(s.get("end", start)))
                        text = (s.get("text") or "").strip()
                        speaker = s.get("speaker") or s.get("speaker_label")
                        if text:
                            segments.append(ASRSegment(start, end, text, speaker))
        
        result = TranscriptionResult(
            chunk_basename=data.get("chunk_basename", f"sub_{sub_idx}"),
            offset=data.get("offset", 0.0),
            emit_guard=data.get("emit_guard", 0.0),
            segments=segments,
            raw_response=data.get("raw_response")
        )
        
        return (sub_idx, result)

