#!/usr/bin/env python3
"""
Data models and structures for transcription system.
"""
from dataclasses import dataclass
from typing import List, Optional


@dataclass
class ASRSegment:
    """Represents a single transcription segment with timing and speaker info."""
    start: float
    end: float
    text: str
    speaker: Optional[str] = None


@dataclass
class ChunkInfo:
    """Information about a single audio chunk."""
    path: str
    offset: float
    emit_guard: float
    fingerprint: Optional[str] = None


@dataclass
class TranscriptionResult:
    """Result of a single chunk transcription."""
    chunk_basename: str
    offset: float
    emit_guard: float
    segments: List[ASRSegment]
    raw_response: dict

