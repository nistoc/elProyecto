#!/usr/bin/env python3
"""
Agent01 - Modular transcription system with OpenAI API.

Clean Architecture implementation with clear separation of concerns:
- Core: Domain models and configuration
- Services: Business logic and orchestration
- Infrastructure: External dependencies (audio, cache, I/O)
- CLI: Command-line interface

Usage:
    from agent01 import Config, TranscriptionPipeline
    
    config = Config.from_file("config/default.json")
    pipeline = TranscriptionPipeline(config)
    md_path, json_path = pipeline.process_file("audio.m4a")
"""

# Load environment variables from .env file
try:
    from dotenv import load_dotenv
    load_dotenv()
except ImportError:
    # python-dotenv not installed, skip
    pass

# Core exports (Domain Layer)
from .core import (
    ASRSegment,
    ChunkInfo,
    TranscriptionResult,
    Config,
)

# Services exports (Application Layer)
from .services import (
    OpenAITranscriptionClient,
    TranscriptionPipeline,
)

# Infrastructure exports (Infrastructure Layer)
from .infrastructure import (
    AudioUtils,
    AudioChunker,
    CacheManager,
    OutputWriter,
)

__version__ = "2.1.0"
__author__ = "Agent01 Team"
__all__ = [
    # Core
    "ASRSegment",
    "ChunkInfo",
    "TranscriptionResult",
    "Config",
    # Services
    "OpenAITranscriptionClient",
    "TranscriptionPipeline",
    # Infrastructure
    "AudioUtils",
    "AudioChunker",
    "CacheManager",
    "OutputWriter",
]
