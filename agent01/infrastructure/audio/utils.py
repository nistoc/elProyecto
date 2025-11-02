#!/usr/bin/env python3
"""
Audio file utilities using ffmpeg/ffprobe.
"""
import os
import shutil
import subprocess
from typing import Optional, Tuple


class AudioUtils:
    """Utilities for working with audio files."""
    
    @staticmethod
    def which_or(path_key: Optional[str], default_name: str) -> Optional[str]:
        """Get executable path from config or find in PATH."""
        if path_key:
            return path_key
        return shutil.which(default_name)
    
    @staticmethod
    def get_duration_and_size(ffprobe_path: str, filepath: str) -> Tuple[float, int]:
        """Get audio duration (seconds) and file size (bytes) using ffprobe."""
        size = os.path.getsize(filepath)
        try:
            cmd = [
                ffprobe_path, "-v", "error",
                "-show_entries", "format=duration",
                "-of", "default=nw=1:nk=1",
                filepath
            ]
            out = subprocess.check_output(cmd, stderr=subprocess.STDOUT)
            dur = float(out.decode().strip())
        except Exception:
            dur = 0.0
        return (dur, size)
    
    @staticmethod
    def calculate_segment_time(size_bytes: int, duration_sec: float, target_mb: float) -> int:
        """Calculate optimal segment duration to achieve target chunk size."""
        if duration_sec <= 0:
            return 480
        target_bytes = int(target_mb * 1024 * 1024)
        bytes_per_sec = size_bytes / max(duration_sec, 0.001)
        seg_seconds = max(60, int((target_bytes * 0.97) / max(bytes_per_sec, 1)))
        return seg_seconds
    
    @staticmethod
    def reencode_if_needed(ffmpeg_path: str, input_path: str, target_mb: float, bitrate_kbps: int) -> str:
        """Re-encode audio file if it exceeds target size."""
        size_mb = os.path.getsize(input_path) / 1024 / 1024
        if size_mb <= target_mb:
            return input_path
        
        root, ext = os.path.splitext(input_path)
        output_path = root + "_re.m4a"
        cmd = [
            ffmpeg_path, "-y", "-i", input_path,
            "-ac", "1", "-ar", "16000",
            "-b:a", f"{bitrate_kbps}k",
            output_path,
        ]
        subprocess.check_call(cmd)
        return output_path
    
    @staticmethod
    def format_mb(num_bytes: int) -> str:
        """Format bytes as MB string."""
        return f"{num_bytes/1024/1024:.2f} MB"

