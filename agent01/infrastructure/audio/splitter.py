#!/usr/bin/env python3
"""
Chunk splitter for dividing problematic audio chunks into smaller parts.

Used when a chunk fails to transcribe due to size/complexity issues.
Splits the chunk into N equal parts with overlap for better transcription.
"""
import os
import subprocess
from typing import List
from core.models import ChunkInfo
from .utils import AudioUtils


class ChunkSplitter:
    """Splits a single audio chunk into multiple smaller sub-chunks."""
    
    def __init__(self, ffmpeg_path: str, ffprobe_path: str):
        """
        Initialize the splitter with ffmpeg paths.
        
        Args:
            ffmpeg_path: Path to ffmpeg executable
            ffprobe_path: Path to ffprobe executable
        """
        self.ffmpeg_path = ffmpeg_path
        self.ffprobe_path = ffprobe_path
    
    def split_chunk(
        self,
        chunk_path: str,
        parts: int,
        output_dir: str,
        overlap_sec: float = 1.0
    ) -> List[ChunkInfo]:
        """
        Split a single chunk into N equal parts with overlap.
        
        Args:
            chunk_path: Path to the chunk audio file
            parts: Number of parts to split into (2, 3, or 4)
            output_dir: Directory for sub-chunk files
            overlap_sec: Overlap duration between consecutive sub-chunks
        
        Returns:
            List of ChunkInfo for each sub-chunk
        """
        if parts < 2 or parts > 4:
            raise ValueError(f"Parts must be 2, 3, or 4, got {parts}")
        
        if not os.path.isfile(chunk_path):
            raise FileNotFoundError(f"Chunk file not found: {chunk_path}")
        
        os.makedirs(output_dir, exist_ok=True)
        
        # Get chunk duration
        duration, size_bytes = AudioUtils.get_duration_and_size(
            self.ffprobe_path, chunk_path
        )
        
        print(f"[SPLIT] Source chunk: {chunk_path}")
        print(f"[SPLIT] Duration: {duration:.2f}s, Size: {AudioUtils.format_mb(size_bytes)}")
        print(f"[SPLIT] Splitting into {parts} parts with {overlap_sec}s overlap")
        
        # Calculate segment duration and stride
        # Each part should be approximately duration/parts long
        # Stride = segment_duration - overlap
        segment_duration = duration / parts + overlap_sec * (parts - 1) / parts
        stride = (duration - segment_duration) / (parts - 1) if parts > 1 else duration
        
        # Ensure minimum segment duration
        min_segment = 2.0  # At least 2 seconds per segment
        if segment_duration < min_segment:
            segment_duration = min(duration, min_segment)
            stride = (duration - segment_duration) / (parts - 1) if parts > 1 else duration
        
        base_name = os.path.splitext(os.path.basename(chunk_path))[0]
        sub_chunks: List[ChunkInfo] = []
        
        for i in range(parts):
            start_time = i * stride
            # Last segment should extend to the end
            if i == parts - 1:
                end_time = duration
            else:
                end_time = min(start_time + segment_duration, duration)
            
            actual_duration = end_time - start_time
            
            # Output filename
            ext = os.path.splitext(chunk_path)[1] or ".wav"
            out_path = os.path.join(output_dir, f"{base_name}_sub_{i:02d}{ext}")
            
            # Extract sub-chunk with ffmpeg
            cmd = [
                self.ffmpeg_path, "-y",
                "-loglevel", "error",
                "-hide_banner",
                "-i", chunk_path,
                "-ss", f"{start_time:.3f}",
                "-t", f"{actual_duration:.3f}",
                "-c", "copy",
                out_path,
            ]
            
            try:
                subprocess.check_call(cmd)
            except subprocess.CalledProcessError as e:
                # Try with re-encoding if copy fails
                print(f"[SPLIT] Copy failed, trying re-encode for sub-chunk {i}")
                cmd = [
                    self.ffmpeg_path, "-y",
                    "-loglevel", "error",
                    "-hide_banner",
                    "-i", chunk_path,
                    "-ss", f"{start_time:.3f}",
                    "-t", f"{actual_duration:.3f}",
                    "-acodec", "pcm_s16le",
                    "-ar", "16000",
                    out_path,
                ]
                subprocess.check_call(cmd)
            
            # Emit guard: first sub-chunk emits everything, others skip overlap
            emit_guard = 0.0 if i == 0 else overlap_sec
            
            sub_chunk = ChunkInfo(
                path=out_path,
                offset=start_time,  # Offset relative to parent chunk
                emit_guard=emit_guard
            )
            sub_chunks.append(sub_chunk)
            
            # Get actual file size
            actual_size = os.path.getsize(out_path)
            print(f"[SPLIT] Created sub-chunk {i}: {out_path}")
            print(f"        Start: {start_time:.2f}s, Duration: {actual_duration:.2f}s, "
                  f"Size: {AudioUtils.format_mb(actual_size)}")
        
        print(f"[SPLIT] Successfully created {len(sub_chunks)} sub-chunks")
        return sub_chunks
    
    def validate_sub_chunks(self, sub_chunks: List[ChunkInfo]) -> bool:
        """
        Validate that all sub-chunks exist and have non-zero size.
        
        Args:
            sub_chunks: List of ChunkInfo to validate
        
        Returns:
            True if all sub-chunks are valid
        """
        for i, chunk in enumerate(sub_chunks):
            if not os.path.isfile(chunk.path):
                print(f"[SPLIT] ERROR: Sub-chunk {i} not found: {chunk.path}")
                return False
            if os.path.getsize(chunk.path) == 0:
                print(f"[SPLIT] ERROR: Sub-chunk {i} is empty: {chunk.path}")
                return False
        return True

