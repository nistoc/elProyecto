"""
Audio infrastructure module.

Handles audio file processing using ffmpeg/ffprobe:
- Audio utilities (duration, size, re-encoding)
- Chunking with overlap
"""
from .utils import AudioUtils
from .chunker import AudioChunker

__all__ = ["AudioUtils", "AudioChunker"]

