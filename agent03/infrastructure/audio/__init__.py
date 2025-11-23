"""
Audio infrastructure module.

Handles audio file processing using ffmpeg/ffprobe:
- Audio utilities (duration, size, re-encoding)
- Chunking with overlap
- Speaker diarization with pyannote.audio (v3.0+)
"""
from .utils import AudioUtils
from .chunker import AudioChunker
from .diarizer import AudioDiarizer, DiarizationSegment

__all__ = ["AudioUtils", "AudioChunker", "AudioDiarizer", "DiarizationSegment"]

