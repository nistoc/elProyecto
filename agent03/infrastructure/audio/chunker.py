#!/usr/bin/env python3
"""
Audio chunking with controlled overlap.
"""
import os
import math
import subprocess
from typing import List, Tuple
from core.models import ChunkInfo
from .utils import AudioUtils


class AudioChunker:
    """Handles audio file splitting with overlap support."""
    
    def __init__(self, ffmpeg_path: str, ffprobe_path: str):
        self.ffmpeg_path = ffmpeg_path
        self.ffprobe_path = ffprobe_path
    
    def slice_with_overlap(
        self,
        source_path: str,
        segment_time: int,
        overlap_sec: float,
        workdir: str,
        naming_pattern: str
    ) -> List[ChunkInfo]:
        """
        Create overlapped chunks using fixed window and stride.
        
        Args:
            source_path: Path to source audio file
            segment_time: Duration of each segment in seconds
            overlap_sec: Overlap duration between consecutive chunks
            workdir: Directory to store chunks
            naming_pattern: Naming pattern with {base} and %03d placeholder
        
        Returns:
            List of ChunkInfo objects with paths, offsets, and emit guards
        """
        os.makedirs(workdir, exist_ok=True)
        
        # Get duration
        dur, _ = AudioUtils.get_duration_and_size(self.ffprobe_path, source_path)
        
        # Clamp overlap to reasonable range
        overlap = max(0.0, min(float(overlap_sec), max(0.0, segment_time - 0.5)))
        stride = max(1.0, segment_time - overlap)
        
        # Prepare output naming
        base_no_ext = os.path.splitext(os.path.basename(source_path))[0]
        out_pattern = os.path.join(workdir, os.path.basename(naming_pattern.format(base=base_no_ext)))
        
        # Calculate padding width
        est_count = int(math.ceil(max(1.0, dur) / stride))
        pad = max(3, int(math.ceil(math.log10(max(1, est_count + 1)))))
        
        chunk_infos: List[ChunkInfo] = []
        t = 0.0
        idx = 0
        
        while t < dur - 0.25:  # Small tail tolerance
            # Window duration for this chunk
            win_dur = min(segment_time, max(0.0, dur - t))
            
            # Output path
            out_path = out_pattern.replace("%03d", str(idx).zfill(pad))
            
            # Precise cut using ffmpeg
            cmd = [
                self.ffmpeg_path, "-y",
                "-i", source_path,
                "-ss", f"{t:.3f}",
                "-t", f"{win_dur:.3f}",
                "-c", "copy",
                out_path,
            ]
            subprocess.check_call(cmd)
            
            # Emit guard: first chunk emits everything, others skip overlap portion
            emit_guard = 0.0 if idx == 0 else overlap
            
            chunk_infos.append(ChunkInfo(
                path=out_path,
                offset=t,
                emit_guard=emit_guard
            ))
            
            idx += 1
            t += stride
        
        # Sort by offset to ensure natural order
        chunk_infos.sort(key=lambda x: x.offset)
        
        return chunk_infos
    
    def process_chunks_for_file(
        self,
        source_path: str,
        target_mb: float,
        workdir: str,
        naming_pattern: str,
        overlap_sec: float,
        reencode: bool = True,
        reencode_bitrate: int = 64
    ) -> List[ChunkInfo]:
        """
        Main entry point: split file into chunks if needed.
        
        Returns:
            List of ChunkInfo objects (single item if no split needed)
        """
        if not os.path.isfile(source_path):
            raise FileNotFoundError(f"Source file not found: {source_path}")
        
        # Check if splitting is needed
        dur, size = AudioUtils.get_duration_and_size(self.ffprobe_path, source_path)
        print(f"[INFO] Source duration: {dur:.2f}s | size: {AudioUtils.format_mb(size)}")
        
        if size <= target_mb * 1024 * 1024:
            print("[INFO] Source is under target size; no split needed.")
            return [ChunkInfo(path=source_path, offset=0.0, emit_guard=0.0)]
        
        # Calculate segment time
        seg_time = AudioUtils.calculate_segment_time(size, dur, target_mb)
        print(f"[INFO] Overlap slicing: window ~{seg_time}s, overlap {overlap_sec:.2f}s")
        
        # Slice with overlap
        chunk_infos = self.slice_with_overlap(
            source_path, seg_time, overlap_sec, workdir, naming_pattern
        )
        
        # Re-encode if needed
        if reencode:
            for chunk_info in chunk_infos:
                new_path = AudioUtils.reencode_if_needed(
                    self.ffmpeg_path, chunk_info.path, target_mb, reencode_bitrate
                )
                chunk_info.path = new_path
                
                if os.path.getsize(new_path) > target_mb * 1024 * 1024:
                    print(f"[WARN] Chunk still exceeds target after reencode: {new_path} ({AudioUtils.format_mb(os.path.getsize(new_path))})")
        
        # Print summary
        print(f"[INFO] Produced {len(chunk_infos)} chunks in '{workdir}'.")
        for i, info in enumerate(chunk_infos):
            mb = AudioUtils.format_mb(os.path.getsize(info.path))
            print(f"  - [{i}] {info.path} | {mb} | offset={info.offset:.2f}s | emit_guard={info.emit_guard:.2f}s")
        
        return chunk_infos

