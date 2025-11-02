#!/usr/bin/env python3
"""
Main transcription pipeline orchestrator.
"""
import os
from typing import List, Optional
from core.config import Config
from core.models import ChunkInfo, TranscriptionResult
from infrastructure.audio import AudioUtils, AudioChunker
from infrastructure.cache import CacheManager
from infrastructure.io import OutputWriter
from .api_client import OpenAITranscriptionClient


class TranscriptionPipeline:
    """Orchestrates the complete transcription workflow."""
    
    def __init__(self, config: Config):
        self.config = config
        
        # Initialize components
        self._init_chunker()
        self._init_api_client()
        self._init_cache_manager()
        self._init_output_writer()
    
    def _init_chunker(self):
        """Initialize audio chunker if pre-splitting is enabled."""
        if self.config.get("pre_split"):
            ffmpeg = AudioUtils.which_or(self.config.get("ffmpeg_path"), "ffmpeg")
            ffprobe = AudioUtils.which_or(self.config.get("ffprobe_path"), "ffprobe")
            
            if ffmpeg and ffprobe:
                self.chunker = AudioChunker(ffmpeg, ffprobe)
            else:
                print("[WARN] ffmpeg/ffprobe not found; pre-splitting disabled.")
                self.chunker = None
        else:
            self.chunker = None
    
    def _init_api_client(self):
        """Initialize OpenAI API client."""
        self.api_client = OpenAITranscriptionClient(
            api_key=self.config.get("openai_api_key"),
            base_url=self.config.get("openai_base_url"),
            organization=self.config.get("openai_organization"),
            model=self.config.get("model"),
            fallback_models=self.config.get("fallback_models")
        )
    
    def _init_cache_manager(self):
        """Initialize cache manager."""
        cache_dir = self.config.get("cache_dir") or "cache"
        self.cache_manager = CacheManager(cache_dir)
    
    def _init_output_writer(self):
        """Initialize output writer."""
        self.output_writer = OutputWriter()
    
    def process_file(self, file_path: str) -> tuple:
        """
        Process a single audio file through the complete pipeline.
        
        Args:
            file_path: Path to audio file
        
        Returns:
            Tuple of (markdown_path, json_path)
        """
        if not os.path.isfile(file_path):
            raise FileNotFoundError(f"Audio file not found: {file_path}")
        
        print(f"\n[FILE] {file_path}")
        
        # Get output paths
        base_name = os.path.splitext(os.path.basename(file_path))[0]
        md_path, json_path = self.output_writer.get_output_paths(
            base_name,
            self.config.get("md_output_path"),
            self.config.get("raw_json_output_path")
        )
        
        print(f"[OUT ] Markdown -> {md_path}")
        print(f"[OUT ] Raw JSON -> {json_path}")
        
        # Initialize output
        self.output_writer.initialize_markdown(md_path)
        self.output_writer.reset_speaker_map()
        
        # Load cache manifest
        manifest_path = self.cache_manager.get_manifest_path(base_name)
        manifest = self.cache_manager.load_manifest(manifest_path)
        print(f"[CACHE] Manifest -> {manifest_path}")
        
        # Chunk the file if needed
        chunk_infos = self._prepare_chunks(file_path)
        
        # Process each chunk
        results: List[TranscriptionResult] = []
        for i, chunk_info in enumerate(chunk_infos):
            result = self._process_chunk(
                chunk_info, i, len(chunk_infos),
                manifest, manifest_path
            )
            results.append(result)
            
            # Write to markdown incrementally
            self.output_writer.append_segments_to_markdown(
                md_path,
                result.segments,
                result.offset,
                result.emit_guard
            )
            print(f"[INFO] Appended {len(result.segments)} segments to Markdown (guard {result.emit_guard:.2f}s).")
        
        # Finalize outputs
        self.output_writer.finalize_markdown(md_path)
        self.output_writer.save_combined_json(json_path, results)
        
        return md_path, json_path
    
    def _prepare_chunks(self, file_path: str) -> List[ChunkInfo]:
        """Prepare chunks from file (split if needed)."""
        if not self.chunker or not self.config.get("pre_split"):
            return [ChunkInfo(path=file_path, offset=0.0, emit_guard=0.0)]
        
        return self.chunker.process_chunks_for_file(
            source_path=file_path,
            target_mb=float(self.config.get("target_chunk_mb")),
            workdir=self.config.get("split_workdir"),
            naming_pattern=self.config.get("chunk_naming"),
            overlap_sec=float(self.config.get("chunk_overlap_sec")),
            reencode=bool(self.config.get("reencode_if_needed")),
            reencode_bitrate=int(self.config.get("reencode_bitrate_kbps"))
        )
    
    def _process_chunk(
        self,
        chunk_info: ChunkInfo,
        index: int,
        total: int,
        manifest: dict,
        manifest_path: str
    ) -> TranscriptionResult:
        """Process a single chunk with caching."""
        chunk_basename = os.path.basename(chunk_info.path)
        print(f"[INFO] Processing chunk {index+1}/{total}: {chunk_basename}")
        
        # Calculate fingerprint
        fingerprint = self.cache_manager.get_file_fingerprint(chunk_info.path)
        
        # Check cache
        cached_response = self.cache_manager.get_cached_response(
            manifest, chunk_basename, fingerprint
        )
        
        if cached_response:
            print("[CACHE] Using cached response for chunk.")
            raw_response = cached_response
        else:
            # Transcribe with API
            raw_response = self.api_client.transcribe(
                audio_path=chunk_info.path,
                language=self.config.get("language"),
                prompt=self.config.get("prompt"),
                temperature=self.config.get("temperature"),
                response_format=self.config.get("response_format"),
                timestamp_granularities=self.config.get("timestamp_granularities")
            )
            
            # Cache the response
            self.cache_manager.cache_response(
                manifest, manifest_path,
                chunk_basename, fingerprint, raw_response
            )
        
        # Save per-chunk JSON if requested
        if self.config.get("save_per_chunk_json"):
            per_chunk_dir = self.config.get("per_chunk_json_dir") or "chunks_json"
            self.output_writer.save_per_chunk_json(
                chunk_basename, raw_response, per_chunk_dir
            )
        
        # Parse segments
        segments = self.api_client.parse_segments(raw_response)
        
        return TranscriptionResult(
            chunk_basename=chunk_basename,
            offset=chunk_info.offset,
            emit_guard=chunk_info.emit_guard,
            segments=segments,
            raw_response=raw_response
        )
    
    def process_all_files(self) -> List[tuple]:
        """
        Process all files specified in configuration.
        
        Returns:
            List of (markdown_path, json_path) tuples for each file
        """
        files = self.config.get_files()
        if not files:
            raise ValueError("No input files specified in configuration")
        
        results = []
        for file_path in files:
            result = self.process_file(file_path)
            results.append(result)
        
        return results

