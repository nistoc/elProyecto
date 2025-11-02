"""
Pytest configuration and fixtures.
"""
import pytest
import tempfile
import os
from pathlib import Path


@pytest.fixture
def temp_dir():
    """Create a temporary directory for tests."""
    with tempfile.TemporaryDirectory() as tmpdir:
        yield Path(tmpdir)


@pytest.fixture
def sample_config():
    """Create a sample configuration for tests."""
    return {
        "file": "test_audio.m4a",
        "model": "gpt-4o-transcribe-diarize",
        "openai_api_key": "test-key",
        "pre_split": False,
        "cache_dir": "test_cache",
    }


@pytest.fixture
def sample_audio_segment():
    """Create a sample ASR segment."""
    from agent01.core import ASRSegment
    return ASRSegment(
        start=0.0,
        end=2.5,
        text="Test transcription",
        speaker="speaker_1"
    )

