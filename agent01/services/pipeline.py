#!/usr/bin/env python3
"""
Main transcription pipeline orchestrator.
"""
import os
import json
from typing import List, Optional, Dict, Any
from core.config import Config
from core.models import ChunkInfo, TranscriptionResult
from infrastructure.audio import AudioUtils, AudioChunker, AudioDiarizer, DiarizationSegment
from infrastructure.cache import CacheManager
from infrastructure.io import OutputWriter
from .api_client import OpenAITranscriptionClient


class TranscriptionPipeline:
    """Orchestrates the complete transcription workflow."""
    
    def __init__(self, config: Config):
        self.config = config
        self.current_file_workspace = None  # Workspace for current file
        
        # Initialize components
        self._init_chunker()
        self._init_diarizer()
        self._init_api_client()
        self._init_cache_manager()
        self._init_output_writer()
    
    def _init_chunker(self):
        """Initialize audio chunker if pre-splitting is enabled."""
        if self.config.get("pre_split") and not self.config.get("use_diarization"):
            ffmpeg = AudioUtils.which_or(self.config.get("ffmpeg_path"), "ffmpeg")
            ffprobe = AudioUtils.which_or(self.config.get("ffprobe_path"), "ffprobe")
            
            if ffmpeg and ffprobe:
                self.chunker = AudioChunker(ffmpeg, ffprobe)
            else:
                print("[WARN] ffmpeg/ffprobe not found; pre-splitting disabled.")
                self.chunker = None
        else:
            self.chunker = None
    
    def _init_diarizer(self):
        """Initialize audio diarizer if diarization is enabled."""
        if self.config.get("use_diarization"):
            self.diarizer = AudioDiarizer(
                huggingface_token=self.config.get("huggingface_token"),
                model_name=self.config.get("diarization_model")
            )
        else:
            self.diarizer = None
    
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
    
    def _create_file_workspace(self, file_path: str) -> Dict[str, str]:
        """
        Create workspace directories for processing a specific file.
        
        Args:
            file_path: Path to the audio file being processed
        
        Returns:
            Dictionary with paths to all workspace directories
        """
        # Get base name without extension
        base_name = os.path.splitext(os.path.basename(file_path))[0]
        
        # Create main workspace directory inside project
        workspace_root = self.config.get("workspace_root") or "processing_workspaces"
        file_workspace = os.path.join(workspace_root, base_name)
        
        # Create subdirectories
        workspace = {
            "root": file_workspace,
            "wav": os.path.join(file_workspace, "converted_wav"),
            "segments": os.path.join(file_workspace, "segments"),
            "intermediate": os.path.join(file_workspace, "intermediate"),
            "cache": os.path.join(file_workspace, "cache"),
            "output": os.path.join(file_workspace, "output"),
        }
        
        # Create all directories
        for dir_path in workspace.values():
            os.makedirs(dir_path, exist_ok=True)
        
        print(f"[INFO] Created workspace for '{base_name}': {file_workspace}")
        
        return workspace
    
    def _convert_to_wav_if_needed(self, file_path: str, workspace: Dict[str, str] = None) -> str:
        """
        Convert audio file to WAV format if enabled in config.
        
        Args:
            file_path: Path to input audio file
            workspace: Workspace dictionary for current file (optional)
        
        Returns:
            Path to WAV file (or original if conversion disabled/failed)
        """
        if not self.config.get("convert_to_wav"):
            return file_path
        
        # Only convert if file is m4a
        if not file_path.lower().endswith('.m4a'):
            print(f"[INFO] File is not m4a, skipping conversion: {file_path}")
            return file_path
        
        ffmpeg = AudioUtils.which_or(self.config.get("ffmpeg_path"), "ffmpeg")
        if not ffmpeg:
            print("[WARN] ffmpeg not found; skipping WAV conversion.")
            return file_path
        
        # Use workspace directory if available, otherwise use config/default
        if workspace:
            wav_dir = workspace["wav"]
        else:
            wav_dir = self.config.get("wav_output_dir") or "converted_wav"
        
        converted_path = AudioUtils.convert_to_wav(ffmpeg, file_path, wav_dir)
        
        return converted_path
    
    def _save_intermediate_result(
        self,
        result: TranscriptionResult,
        base_name: str,
        chunk_index: int,
        workspace: Dict[str, str] = None
    ):
        """
        Save intermediate transcription result to file.
        
        Args:
            result: Transcription result to save
            base_name: Base name of the audio file
            chunk_index: Index of the chunk
            workspace: Workspace dictionary for current file (optional)
        """
        if not self.config.get("save_intermediate_results"):
            return
        
        # Use workspace directory if available
        if workspace:
            intermediate_dir = workspace["intermediate"]
        else:
            intermediate_dir = self.config.get("intermediate_results_dir") or "intermediate_results"
            os.makedirs(intermediate_dir, exist_ok=True)
        
        # Create filename for intermediate result
        intermediate_filename = f"{base_name}_chunk_{chunk_index:03d}_result.json"
        intermediate_path = os.path.join(intermediate_dir, intermediate_filename)
        
        # Prepare data to save
        intermediate_data = {
            "chunk_basename": result.chunk_basename,
            "chunk_index": chunk_index,
            "offset": result.offset,
            "emit_guard": result.emit_guard,
            "segments": [
                {
                    "start": seg.start,
                    "end": seg.end,
                    "text": seg.text,
                    "speaker": seg.speaker
                }
                for seg in result.segments
            ],
            "raw_response": result.raw_response
        }
        
        # Save to file
        import json
        with open(intermediate_path, 'w', encoding='utf-8') as f:
            json.dump(intermediate_data, f, ensure_ascii=False, indent=2)
        
        print(f"[INFO] Saved intermediate result: {intermediate_path}")
    
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
        
        # === STAGE 1: Create workspace for this file ===
        print("\n[STAGE 1] Creating workspace...")
        self.current_file_workspace = self._create_file_workspace(file_path)
        
        # === STAGE 3: Convert to WAV if needed ===
        print("\n[STAGE 3] Converting to WAV if needed...")
        working_file_path = self._convert_to_wav_if_needed(file_path, self.current_file_workspace)
        if working_file_path != file_path:
            print(f"[INFO] Working with converted file: {working_file_path}")
        
        # Check if diarization mode is enabled
        if self.config.get("use_diarization"):
            return self._process_file_with_diarization(file_path, working_file_path)
        
        # Get output paths in workspace
        base_name = os.path.splitext(os.path.basename(file_path))[0]
        workspace = self.current_file_workspace
        
        # Use workspace output directory
        md_path = os.path.join(workspace["output"], f"{base_name}_transcript.md")
        json_path = os.path.join(workspace["output"], f"{base_name}_transcript.json")
        
        print(f"[OUT ] Markdown -> {md_path}")
        print(f"[OUT ] Raw JSON -> {json_path}")
        
        # Initialize output
        self.output_writer.initialize_markdown(md_path)
        self.output_writer.reset_speaker_map()
        
        # Load cache manifest from workspace cache directory
        manifest_path = os.path.join(workspace["cache"], f"{base_name}_manifest.json")
        manifest = self.cache_manager.load_manifest(manifest_path)
        print(f"[CACHE] Manifest -> {manifest_path}")
        
        # === STAGE 4: Chunk the file if needed ===
        print("\n[STAGE 4] Chunking audio file...")
        chunk_infos = self._prepare_chunks(working_file_path)
        
        # === STAGE 5-9: Process each chunk ===
        print(f"\n[STAGE 5-9] Processing {len(chunk_infos)} chunk(s)...")
        results: List[TranscriptionResult] = []
        for i, chunk_info in enumerate(chunk_infos):
            print(f"\n--- Processing chunk {i+1}/{len(chunk_infos)} ---")
            
            result = self._process_chunk(
                chunk_info, i, len(chunk_infos),
                manifest, manifest_path
            )
            results.append(result)
            
            # === STAGE 8: Save intermediate result ===
            self._save_intermediate_result(result, base_name, i, workspace)
            
            # Write to markdown incrementally
            self.output_writer.append_segments_to_markdown(
                md_path,
                result.segments,
                result.offset,
                result.emit_guard
            )
            print(f"[INFO] Appended {len(result.segments)} segments to Markdown (guard {result.emit_guard:.2f}s).")
        
        # === STAGE 10: Finalize outputs ===
        print("\n[STAGE 10] Finalizing outputs...")
        self.output_writer.finalize_markdown(md_path)
        self.output_writer.save_combined_json(json_path, results)
        
        print(f"\n[DONE] Processing complete!")
        print(f"  - Workspace: {workspace['root']}")
        print(f"  - Markdown: {md_path}")
        print(f"  - JSON: {json_path}")
        if self.config.get("save_intermediate_results"):
            print(f"  - Intermediate results: {workspace['intermediate']}/")
        
        return md_path, json_path
    
    def _process_file_with_diarization(self, original_file_path: str, wav_file_path: str) -> tuple:
        """
        Process file using diarization mode (v3.0+).
        
        Args:
            original_file_path: Original input file path
            wav_file_path: Converted WAV file path
        
        Returns:
            Tuple of (markdown_path, json_path)
        """
        base_name = os.path.splitext(os.path.basename(original_file_path))[0]
        workspace = self.current_file_workspace
        
        # === STAGE 4: Diarization ===
        print("\n[STAGE 4] Running speaker diarization...")
        diarization_segments = self.diarizer.diarize(wav_file_path)
        
        # Save diarization results in workspace
        diarization_json_path = os.path.join(workspace["output"], f"{base_name}_diarization.json")
        self.diarizer.save_segments_to_json(diarization_segments, diarization_json_path)
        
        # === STAGE 5-6: Extract and transcribe each segment ===
        print(f"\n[STAGE 5-6] Processing {len(diarization_segments)} diarized segments...")
        
        # Use workspace segments directory
        segments_dir = workspace["segments"]
        
        all_transcriptions = []
        
        for idx, dia_seg in enumerate(diarization_segments):
            print(f"\n--- Processing segment {idx+1}/{len(diarization_segments)} ---")
            print(f"    Speaker: {dia_seg.speaker}, Time: {dia_seg.start:.2f}s - {dia_seg.end:.2f}s")
            
            # Extract audio segment
            segment_filename = f"{base_name}_seg_{idx:04d}_{dia_seg.speaker}.wav"
            segment_path = os.path.join(segments_dir, segment_filename)
            
            self.diarizer.extract_audio_segment(
                wav_file_path,
                segment_path,
                dia_seg.start,
                dia_seg.end
            )
            
            # Transcribe with whisper-1
            print(f"[INFO] Transcribing segment with whisper-1...")
            transcription = self._transcribe_segment_whisper(segment_path)
            
            # Combine diarization + transcription
            result = {
                "speaker": dia_seg.speaker,
                "start": dia_seg.start,
                "end": dia_seg.end,
                "text": transcription.get("text", ""),
                "words": transcription.get("words", []),
                "language": transcription.get("language", "unknown")
            }
            
            all_transcriptions.append(result)
            
            # Save intermediate result in workspace
            if self.config.get("save_intermediate_results"):
                intermediate_path = os.path.join(
                    workspace["intermediate"],
                    f"{base_name}_seg_{idx:04d}_result.json"
                )
                with open(intermediate_path, 'w', encoding='utf-8') as f:
                    json.dump(result, f, ensure_ascii=False, indent=2)
                print(f"[INFO] Saved intermediate result: {intermediate_path}")
        
        # === STAGE 7: Sort by start time ===
        print("\n[STAGE 7] Sorting results by start time...")
        all_transcriptions.sort(key=lambda x: x["start"])
        
        # === STAGE 8: Save final results ===
        print("\n[STAGE 8] Saving final results...")
        
        # Save as JSON in workspace output directory
        json_path = os.path.join(workspace["output"], f"{base_name}_transcript.json")
        with open(json_path, 'w', encoding='utf-8') as f:
            json.dump(all_transcriptions, f, ensure_ascii=False, indent=2)
        print(f"[INFO] Saved final JSON: {json_path}")
        
        # Save as Markdown in workspace output directory
        md_path = os.path.join(workspace["output"], f"{base_name}_transcript.md")
        with open(md_path, 'w', encoding='utf-8') as f:
            f.write("# Transcription with Speaker Diarization\n\n")
            for item in all_transcriptions:
                f.write(f"**[{item['start']:.2f}s - {item['end']:.2f}s] {item['speaker']}:**\n")
                f.write(f"{item['text']}\n\n")
        print(f"[INFO] Saved Markdown: {md_path}")
        
        print(f"\n[DONE] Diarization-based processing complete!")
        print(f"  - Workspace: {workspace['root']}")
        print(f"  - JSON: {json_path}")
        print(f"  - Markdown: {md_path}")
        print(f"  - Segments: {segments_dir}")
        print(f"  - Total segments: {len(all_transcriptions)}")
        
        return md_path, json_path
    
    def _transcribe_segment_whisper(self, audio_path: str) -> Dict[str, Any]:
        """
        Transcribe audio segment using whisper-1 with verbose_json.
        
        Args:
            audio_path: Path to audio segment
        
        Returns:
            Transcription result dictionary
        """
        try:
            with open(audio_path, "rb") as f:
                response = self.api_client.client.audio.transcriptions.create(
                    file=f,
                    model="whisper-1",
                    response_format="verbose_json"
                    # Note: language not specified - let model auto-detect
                )
            
            # Convert to dict
            if hasattr(response, "model_dump"):
                return response.model_dump()
            elif hasattr(response, "model_dump_json"):
                return json.loads(response.model_dump_json())
            elif isinstance(response, dict):
                return response
            else:
                return json.loads(json.dumps(response, default=lambda o: getattr(o, "__dict__", str(o))))
        
        except Exception as e:
            print(f"[ERROR] Failed to transcribe segment: {e}")
            return {"text": "", "words": [], "language": "unknown"}
    
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

