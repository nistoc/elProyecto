"""
Tests for core module (models, config).
"""
import pytest
from agent01.core import ASRSegment, ChunkInfo, TranscriptionResult, Config


class TestASRSegment:
    """Tests for ASRSegment model."""
    
    def test_create_segment(self):
        """Test creating an ASR segment."""
        seg = ASRSegment(
            start=0.0,
            end=2.5,
            text="Hello world",
            speaker="speaker_1"
        )
        assert seg.start == 0.0
        assert seg.end == 2.5
        assert seg.text == "Hello world"
        assert seg.speaker == "speaker_1"


class TestChunkInfo:
    """Tests for ChunkInfo model."""
    
    def test_create_chunk_info(self):
        """Test creating chunk info."""
        chunk = ChunkInfo(
            path="audio_part_001.m4a",
            offset=0.0,
            emit_guard=0.0
        )
        assert chunk.path == "audio_part_001.m4a"
        assert chunk.offset == 0.0
        assert chunk.emit_guard == 0.0


class TestConfig:
    """Tests for Config class."""
    
    def test_create_config_from_dict(self):
        """Test creating config from dictionary."""
        config = Config({
            "file": "test.m4a",
            "model": "gpt-4o-transcribe-diarize"
        })
        assert config.get("file") == "test.m4a"
        assert config.get("model") == "gpt-4o-transcribe-diarize"
    
    def test_config_defaults(self):
        """Test config default values."""
        config = Config({})
        assert config.get("model") == "gpt-4o-transcribe-diarize"
        assert config.get("target_chunk_mb") == 24.5
        assert config.get("chunk_overlap_sec") == 2.0

