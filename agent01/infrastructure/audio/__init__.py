"""
Audio infrastructure module.

Handles audio file processing using ffmpeg/ffprobe:
- Audio utilities (duration, size, re-encoding)
- Chunking with overlap
- Chunk splitting for problematic chunks
"""
from .utils import AudioUtils
from .chunker import AudioChunker
from .splitter import ChunkSplitter

__all__ = ["AudioUtils", "AudioChunker", "ChunkSplitter"]

