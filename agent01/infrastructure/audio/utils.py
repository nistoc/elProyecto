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
        
        # Keep same extension as input file
        # For WAV files, use PCM encoding; for others use compressed format
        if ext.lower() == '.wav':
            output_path = root + "_re.wav"
            cmd = [
                ffmpeg_path, "-y",
                "-loglevel", "error",  # Suppress verbose ffmpeg output
                "-hide_banner",         # Hide configuration details
                "-i", input_path,
                "-ac", "1", "-ar", "16000",
                "-acodec", "pcm_s16le",  # Keep PCM for WAV
                output_path,
            ]
        else:
            output_path = root + "_re.m4a"
            cmd = [
                ffmpeg_path, "-y",
                "-loglevel", "error",  # Suppress verbose ffmpeg output
                "-hide_banner",         # Hide configuration details
                "-i", input_path,
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
    
    @staticmethod
    def convert_to_wav(ffmpeg_path: str, input_path: str, output_dir: Optional[str] = None) -> str:
        """
        Convert audio file to WAV format.
        
        Args:
            ffmpeg_path: Path to ffmpeg executable
            input_path: Path to input audio file
            output_dir: Directory for output file (default: same as input)
        
        Returns:
            Path to converted WAV file
        """
        # Determine output path
        if output_dir:
            os.makedirs(output_dir, exist_ok=True)
            basename = os.path.splitext(os.path.basename(input_path))[0]
            output_path = os.path.join(output_dir, f"{basename}.wav")
        else:
            root, _ = os.path.splitext(input_path)
            output_path = root + ".wav"
        
        # Skip if already WAV
        if input_path.lower().endswith('.wav'):
            print(f"[INFO] File is already WAV format: {input_path}")
            return input_path
        
        # Skip if converted file already exists
        if os.path.exists(output_path):
            print(f"[INFO] Converted WAV file already exists: {output_path}")
            return output_path
        
        # Convert to WAV
        print(f"[INFO] Converting to WAV: {input_path} -> {output_path}")
        cmd = [
            ffmpeg_path, "-y",
            "-loglevel", "error",     # Suppress verbose ffmpeg output
            "-hide_banner",           # Hide configuration details
            "-i", input_path,
            "-acodec", "pcm_s16le",   # 16-bit PCM
            "-ar", "16000",           # 16kHz sample rate
            "-ac", "1",               # Mono
            output_path
        ]
        
        try:
            subprocess.check_call(cmd)
            size_mb = os.path.getsize(output_path) / 1024 / 1024
            print(f"[INFO] Conversion complete: {size_mb:.2f} MB")
            return output_path
        except subprocess.CalledProcessError as e:
            print(f"[ERROR] Failed to convert to WAV: {e}")
            return input_path  # Return original file on failure

