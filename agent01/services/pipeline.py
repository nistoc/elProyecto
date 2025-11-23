#!/usr/bin/env python3
"""
Main transcription pipeline orchestrator.
"""
import os
import shutil
import time
import threading
from concurrent.futures import ThreadPoolExecutor, as_completed
from typing import List, Optional, Tuple
from core.config import Config
from core.models import ChunkInfo, TranscriptionResult
from infrastructure.audio import AudioUtils, AudioChunker
from infrastructure.cache import CacheManager
from infrastructure.io import OutputWriter
from infrastructure.progress import ChunkProgress
from .api_client import OpenAITranscriptionClient, RetryableAPIError


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
    
    def _clean_folders(self):
        """Clean cache and intermediate result folders if enabled in config."""
        if not self.config.get("clean_before_run", True):
            return
        
        print("\n[CLEANUP] Cleaning cache and intermediate folders...")
        
        folders_to_clean = [
            self.config.get("cache_dir", "cache"),
            self.config.get("split_workdir", "chunks"),
            self.config.get("per_chunk_json_dir", "chunks_json"),
            self.config.get("intermediate_results_dir", "intermediate_results"),
            self.config.get("wav_output_dir", "converted_wav"),
        ]
        
        cleaned_count = 0
        for folder in folders_to_clean:
            try:
                # Create folder if it doesn't exist
                os.makedirs(folder, exist_ok=True)
                
                # Remove all contents but keep the folder
                items_removed = 0
                for item in os.listdir(folder):
                    item_path = os.path.join(folder, item)
                    if os.path.isfile(item_path):
                        os.remove(item_path)
                        items_removed += 1
                    elif os.path.isdir(item_path):
                        shutil.rmtree(item_path)
                        items_removed += 1
                
                if items_removed > 0:
                    print(f"[CLEANUP] OK Cleaned: {folder} ({items_removed} item(s))")
                    cleaned_count += 1
                else:
                    print(f"[CLEANUP] - Empty: {folder}")
                    
            except Exception as e:
                print(f"[CLEANUP] ERROR Failed to clean {folder}: {e}")
        
        if cleaned_count > 0:
            print(f"[CLEANUP] Cleaned {cleaned_count} folder(s)\n")
        else:
            print(f"[CLEANUP] All folders are empty\n")
    
    def _convert_to_wav_if_needed(self, file_path: str) -> str:
        """
        Convert audio file to WAV format if enabled in config.
        
        Args:
            file_path: Path to input audio file
        
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
        
        wav_dir = self.config.get("wav_output_dir") or "converted_wav"
        converted_path = AudioUtils.convert_to_wav(ffmpeg, file_path, wav_dir)
        
        return converted_path
    
    def _save_intermediate_result(
        self,
        result: TranscriptionResult,
        base_name: str,
        chunk_index: int
    ):
        """
        Save intermediate transcription result to file.
        
        Args:
            result: Transcription result to save
            base_name: Base name of the audio file
            chunk_index: Index of the chunk
        """
        if not self.config.get("save_intermediate_results"):
            return
        
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
        
        # Clean folders if enabled
        self._clean_folders()
        
        # === STAGE 3: Convert to WAV if needed ===
        print("\n[STAGE 3] Converting to WAV if needed...")
        working_file_path = self._convert_to_wav_if_needed(file_path)
        if working_file_path != file_path:
            print(f"[INFO] Working with converted file: {working_file_path}")
        
        # Get output paths (use original file basename for consistency)
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
        
        # === STAGE 4: Chunk the file if needed ===
        print("\n[STAGE 4] Chunking audio file...")
        chunk_infos = self._prepare_chunks(working_file_path)
        
        # === STAGE 5-9: Process each chunk ===
        print(f"\n[STAGE 5-9] Processing {len(chunk_infos)} chunk(s)...")
        
        # Get parallel workers setting
        max_workers = max(1, int(self.config.get("parallel_transcription_workers", 3)))
        if max_workers > 1 and len(chunk_infos) > 1:
            print(f"[INFO] Using {max_workers} parallel workers for transcription")
        
        results: List[Tuple[int, TranscriptionResult]] = []  # (index, result) pairs
        failed_chunks: List[Tuple[int, ChunkInfo]] = []  # (index, chunk_info) pairs for retry
        time_format = self.config.get("progress_time_format", "MMM:SSS.M")
        chunk_progress = ChunkProgress(len(chunk_infos), parallel_workers=max_workers, time_format=time_format)
        
        # Use parallel processing if workers > 1
        if max_workers > 1 and len(chunk_infos) > 1:
            results, failed_chunks = self._process_chunks_parallel(
                chunk_infos, manifest, manifest_path, base_name,
                md_path, chunk_progress, max_workers
            )
        else:
            results, failed_chunks = self._process_chunks_sequential(
                chunk_infos, manifest, manifest_path, base_name,
                md_path, chunk_progress
            )
        
        # Show completion
        chunk_progress.complete()
        
        # Retry failed chunks if any
        if failed_chunks:
            print(f"\n[RETRY] Found {len(failed_chunks)} chunk(s) that failed with Server 500 errors")
            print(f"[RETRY] Attempting to reprocess these chunks...")
            
            retry_results = self._retry_failed_chunks(
                failed_chunks, manifest, manifest_path, base_name, md_path
            )
            
            # Merge retry results with main results
            results.extend(retry_results)
            print(f"[RETRY] Successfully recovered {len(retry_results)}/{len(failed_chunks)} chunk(s)")
        
        # === STAGE 10: Finalize outputs ===
        print("\n[STAGE 10] Finalizing outputs...")
        self.output_writer.finalize_markdown(md_path)
        
        # Extract just the results (without indices) for saving
        sorted_results = [result for _, result in sorted(results, key=lambda x: x[0])]
        self.output_writer.save_combined_json(json_path, sorted_results)
        
        print(f"\n[DONE] Processing complete!")
        print(f"  - Markdown: {md_path}")
        print(f"  - JSON: {json_path}")
        if self.config.get("save_intermediate_results"):
            print(f"  - Intermediate results: {self.config.get('intermediate_results_dir')}/")
        
        return md_path, json_path
    
    def _prepare_chunks(self, file_path: str) -> List[ChunkInfo]:
        """Prepare chunks from file (split if needed)."""
        if not self.chunker or not self.config.get("pre_split"):
            return [ChunkInfo(path=file_path, offset=0.0, emit_guard=0.0)]
        
        # Adjust naming pattern based on file extension
        naming_pattern = self.config.get("chunk_naming")
        if file_path.lower().endswith('.wav'):
            # Change extension to .wav for WAV files
            naming_pattern = naming_pattern.replace('.m4a', '.wav')
        
        return self.chunker.process_chunks_for_file(
            source_path=file_path,
            target_mb=float(self.config.get("target_chunk_mb")),
            workdir=self.config.get("split_workdir"),
            naming_pattern=naming_pattern,
            overlap_sec=float(self.config.get("chunk_overlap_sec")),
            reencode=bool(self.config.get("reencode_if_needed")),
            reencode_bitrate=int(self.config.get("reencode_bitrate_kbps")),
            max_duration_minutes=float(self.config.get("max_duration_minutes", 0))
        )
    
    def _process_chunks_sequential(
        self,
        chunk_infos: List[ChunkInfo],
        manifest: dict,
        manifest_path: str,
        base_name: str,
        md_path: str,
        chunk_progress: ChunkProgress
    ) -> Tuple[List[Tuple[int, TranscriptionResult]], List[Tuple[int, ChunkInfo]]]:
        """Process chunks sequentially (single-threaded)."""
        results = []
        failed_chunks = []
        
        # Initial progress display
        chunk_progress.update()
        print()  # New line after progress
        
        for i, chunk_info in enumerate(chunk_infos):
            # Mark as started
            chunk_progress.mark_started(i)
            chunk_progress.update()
            print(f"\n--- Processing chunk {i+1}/{len(chunk_infos)} ---")
            
            try:
                result = self._process_chunk(
                    chunk_info, i, len(chunk_infos),
                    manifest, manifest_path
                )
                results.append((i, result))
                
                # Mark as completed
                chunk_progress.mark_completed(i)
                
                # Save intermediate result
                self._save_intermediate_result(result, base_name, i)
                
                # Write to markdown incrementally
                self.output_writer.append_segments_to_markdown(
                    md_path,
                    result.segments,
                    result.offset,
                    result.emit_guard
                )
                print(f"[INFO] OK Appended {len(result.segments)} segments to Markdown (guard {result.emit_guard:.2f}s).")
                
                # Update progress
                chunk_progress.update()
                
            except RetryableAPIError as e:
                # Server 500 error - save for retry later
                print(f"\n[WARN] Chunk {i+1}/{len(chunk_infos)} failed with Server 500, will retry later")
                chunk_progress.mark_completed(i)
                failed_chunks.append((i, chunk_infos[i]))
                continue  # Continue with next chunk
                
            except Exception as e:
                error_msg = str(e)
                print(f"\n[ERROR] Failed processing chunk {i+1}/{len(chunk_infos)}: {e}")
                chunk_progress.mark_completed(i)
                
                # Check if this is a fatal error
                fatal_errors = [
                    "authentication",
                    "api_key",
                    "invalid_api_key",
                    "insufficient_quota",
                    "rate_limit_exceeded",
                    "permission_denied",
                    "invalid_request_error"
                ]
                
                if any(err in error_msg.lower() for err in fatal_errors):
                    print(f"[FATAL] Critical API error detected - stopping all processing")
                    raise RuntimeError(f"Critical API error: {e}")
                else:
                    # Non-fatal error - could continue but in sequential mode better to stop
                    print(f"[ERROR] Cannot continue processing remaining chunks")
                    raise
        
        return results, failed_chunks
    
    def _process_chunks_parallel(
        self,
        chunk_infos: List[ChunkInfo],
        manifest: dict,
        manifest_path: str,
        base_name: str,
        md_path: str,
        chunk_progress: ChunkProgress,
        max_workers: int
    ) -> Tuple[List[Tuple[int, TranscriptionResult]], List[Tuple[int, ChunkInfo]]]:
        """Process chunks in parallel using ThreadPoolExecutor."""
        results = []
        failed_chunks = []
        completed_count = 0
        stop_progress_update = threading.Event()
        
        def progress_updater():
            """Background thread to update progress display."""
            while not stop_progress_update.is_set():
                chunk_progress.update()
                stop_progress_update.wait(0.5)  # Update every 0.5 seconds
        
        # Start progress updater thread
        progress_thread = threading.Thread(target=progress_updater, daemon=True)
        progress_thread.start()
        
        # Initial display
        print()  # New line for progress
        
        try:
            with ThreadPoolExecutor(max_workers=max_workers) as executor:
                # Submit all chunks
                future_to_index = {
                    executor.submit(
                        self._process_chunk,
                        chunk_info, i, len(chunk_infos),
                        manifest, manifest_path
                    ): i
                    for i, chunk_info in enumerate(chunk_infos)
                }
                
                # Mark chunks as started when they begin processing
                for idx in range(min(max_workers, len(chunk_infos))):
                    chunk_progress.mark_started(idx)
                
                # Process results as they complete
                # Set timeout based on number of chunks (5 min per chunk)
                fatal_error = None
                timeout_seconds = len(chunk_infos) * 300  # 5 minutes per chunk
                print(f"[INFO] Total timeout: {timeout_seconds//60} minutes")
                
                try:
                    for future in as_completed(future_to_index, timeout=timeout_seconds):
                        idx = future_to_index[future]
                        try:
                            result = future.result(timeout=5)  # 5 seconds to get ready result
                            results.append((idx, result))
                            
                            # Mark as completed
                            chunk_progress.mark_completed(idx)
                            completed_count += 1
                            
                            print(f"\n[INFO] OK Chunk {idx+1}/{len(chunk_infos)} completed ({completed_count}/{len(chunk_infos)} total)")
                            
                            # Save intermediate result
                            self._save_intermediate_result(result, base_name, idx)
                            
                            # Start next chunk if available
                            next_idx = completed_count + max_workers - 1
                            if next_idx < len(chunk_infos):
                                chunk_progress.mark_started(next_idx)
                            
                        except TimeoutError as e:
                            print(f"\n[ERROR] Chunk {idx+1} timed out getting result: {e}")
                            chunk_progress.mark_completed(idx)
                            completed_count += 1
                            # Timeout is not fatal - continue with other chunks
                            
                        except RetryableAPIError as e:
                            # Server 500 error - save for retry later
                            print(f"\n[WARN] Chunk {idx+1} failed with Server 500, will retry later")
                            chunk_progress.mark_completed(idx)
                            failed_chunks.append((idx, chunk_infos[idx]))
                            completed_count += 1
                            # Continue with other chunks
                            
                        except Exception as e:
                            error_msg = str(e)
                            print(f"\n[ERROR] Chunk {idx+1} failed: {e}")
                            chunk_progress.mark_completed(idx)
                            
                            # Check if this is a fatal error that should stop all processing
                            fatal_errors = [
                                "authentication",
                                "api_key",
                                "invalid_api_key", 
                                "insufficient_quota",
                                "rate_limit_exceeded",
                                "permission_denied",
                                "invalid_request_error"
                            ]
                            
                            if any(err in error_msg.lower() for err in fatal_errors):
                                print(f"[FATAL] Critical API error detected - stopping all processing")
                                fatal_error = e
                                break  # Stop processing more chunks
                            else:
                                # Non-fatal error - continue with other chunks
                                completed_count += 1
                                print(f"[WARN] Continuing with remaining chunks...")
                
                except TimeoutError:
                    print(f"\n[ERROR] Timeout waiting for chunks to complete")
                    print(f"[INFO] {completed_count}/{len(chunk_infos)} chunks completed before timeout")
                    print(f"[WARN] Some chunks are taking too long - they will be skipped")
                    # Note: We can't actually stop threads that are already running
                    # They will continue but we won't wait for their results
            
        finally:
            # Stop progress updater
            stop_progress_update.set()
            if progress_thread.is_alive():
                progress_thread.join(timeout=2.0)
                if progress_thread.is_alive():
                    print("\n[WARN] Progress updater thread did not stop cleanly")
        
        # Check for fatal error
        if fatal_error:
            print(f"\n[FATAL] Processing stopped due to critical error: {fatal_error}")
            print(f"[INFO] {completed_count}/{len(chunk_infos)} chunks completed before error")
            raise RuntimeError(f"Transcription failed with critical error: {fatal_error}")
        
        # Check if all chunks completed
        if len(results) < len(chunk_infos):
            missing_chunks = set(range(len(chunk_infos))) - {idx for idx, _ in results}
            print(f"\n[WARN] Not all chunks completed! Missing: {sorted(missing_chunks)}")
            print(f"[INFO] Processed {len(results)}/{len(chunk_infos)} chunks")
        
        # Sort results by index to maintain order
        results.sort(key=lambda x: x[0])
        
        # Write all segments to markdown in correct order
        print("\n[INFO] Writing all segments to Markdown in order...")
        for idx, result in results:
            self.output_writer.append_segments_to_markdown(
                md_path,
                result.segments,
                result.offset,
                result.emit_guard
            )
        print(f"[INFO] OK All segments written to Markdown")
        
        return results, failed_chunks
    
    def _retry_failed_chunks(
        self,
        failed_chunks: List[Tuple[int, ChunkInfo]],
        manifest: dict,
        manifest_path: str,
        base_name: str,
        md_path: str
    ) -> List[Tuple[int, TranscriptionResult]]:
        """Retry processing failed chunks (one at a time to avoid overwhelming the server)."""
        results = []
        
        for idx, chunk_info in failed_chunks:
            try:
                print(f"\n[RETRY] Processing chunk {idx+1} (attempt 2/2)...")
                # Add delay before retry to give server time to recover
                time.sleep(2)
                
                result = self._process_chunk(
                    chunk_info, idx, len(failed_chunks),
                    manifest, manifest_path, base_name
                )
                
                # Append segments to markdown
                self.output_writer.append_segments_to_markdown(
                    md_path,
                    result.segments,
                    result.offset,
                    result.emit_guard
                )
                
                results.append((idx, result))
                print(f"[RETRY] ✓ Chunk {idx+1} successfully recovered")
                
            except Exception as e:
                print(f"[RETRY] ✗ Chunk {idx+1} failed again: {e}")
                print(f"[RETRY] Skipping this chunk...")
                continue
        
        return results
    
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
            print(f"[API] Sending chunk {index+1}/{total} to API...")
            try:
                raw_response = self.api_client.transcribe(
                    audio_path=chunk_info.path,
                    language=self.config.get("language"),
                    prompt=self.config.get("prompt"),
                    temperature=self.config.get("temperature"),
                    response_format=self.config.get("response_format"),
                    timestamp_granularities=self.config.get("timestamp_granularities"),
                    chunk_label=f"chunk {index+1}/{total}"
                )
                print(f"[API] Chunk {index+1}/{total} API response received")
            except Exception as e:
                print(f"[ERROR] API call failed for chunk {index+1}/{total}: {e}")
                raise
            
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

