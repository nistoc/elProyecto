#!/usr/bin/env python3
"""
Speaker diarization using pyannote.audio.
"""
import os
from typing import List, Optional, Dict, Any
from dataclasses import dataclass


@dataclass
class DiarizationSegment:
    """Single diarization segment with speaker label and timing."""
    speaker: str
    start: float
    end: float


class AudioDiarizer:
    """Handles speaker diarization using pyannote.audio."""
    
    def __init__(self, huggingface_token: Optional[str] = None, model_name: str = "pyannote/speaker-diarization-3.1"):
        """
        Initialize diarizer.
        
        Args:
            huggingface_token: HuggingFace authentication token
            model_name: Name of the diarization model to use
        """
        self.huggingface_token = huggingface_token or os.getenv("HUGGINGFACE_TOKEN")
        self.model_name = model_name
        self.pipeline = None
    
    def _init_pipeline(self):
        """Lazy initialization of pyannote pipeline."""
        if self.pipeline is not None:
            return
        
        try:
            from pyannote.audio import Pipeline
        except ImportError:
            raise ImportError(
                "pyannote.audio is not installed. "
                "Install it with: pip install pyannote.audio"
            )
        
        if not self.huggingface_token:
            raise ValueError(
                "HuggingFace token is required for pyannote.audio. "
                "Set HUGGINGFACE_TOKEN environment variable or pass it in config. "
                "Get your token at: https://huggingface.co/settings/tokens"
            )
        
        print(f"[INFO] Loading diarization model: {self.model_name}")
        
        # Try new API (token) first, fallback to old API (use_auth_token)
        try:
            try:
                self.pipeline = Pipeline.from_pretrained(
                    self.model_name,
                    token=self.huggingface_token  # New API
                )
            except TypeError:
                # Fallback for older versions
                self.pipeline = Pipeline.from_pretrained(
                    self.model_name,
                    use_auth_token=self.huggingface_token  # Old API
                )
            
            # Try to enable progress tracking if available
            try:
                # Some versions of pyannote support progress hooks
                if hasattr(self.pipeline, 'progress_hook'):
                    def progress_callback(step, total):
                        if total > 0:
                            percent = (step / total) * 100
                            print(f"[PROGRESS] {percent:.1f}% ({step}/{total})")
                    self.pipeline.progress_hook = progress_callback
            except Exception:
                pass  # Progress hooks not available in this version
        except Exception as e:
            error_msg = str(e)
            if "403" in error_msg or "gated" in error_msg.lower() or "restricted" in error_msg.lower():
                print("\n" + "="*70)
                print("❌ ERROR: Access to diarization model is restricted")
                print("="*70)
                
                # Determine which model is missing
                if "segmentation" in error_msg.lower():
                    print("\nMissing access to: pyannote/segmentation-3.0")
                    print("\n📋 STEPS TO FIX:")
                    print("1. Visit: https://huggingface.co/pyannote/segmentation-3.0")
                    print("2. Click 'Agree and access repository' button")
                    print("3. Wait a few seconds for approval")
                    print("4. Run the script again")
                elif "community" in error_msg.lower():
                    print("\nMissing access to: pyannote/speaker-diarization-community-1")
                    print("\n📋 STEPS TO FIX:")
                    print("1. Visit: https://huggingface.co/pyannote/speaker-diarization-community-1")
                    print("2. Click 'Agree and access repository' button")
                    print("3. Wait a few seconds for approval")
                    print("4. Run the script again")
                elif "speaker-diarization" in error_msg.lower():
                    print("\nMissing access to: pyannote/speaker-diarization-3.1")
                    print("\n📋 STEPS TO FIX:")
                    print("1. Visit: https://huggingface.co/pyannote/speaker-diarization-3.1")
                    print("2. Click 'Agree and access repository' button")
                    print("3. Wait a few seconds for approval")
                    print("4. Run the script again")
                else:
                    print(f"\nModel '{self.model_name}' requires explicit user approval.")
                    print("\n📋 STEPS TO FIX:")
                    print(f"1. Visit: https://huggingface.co/{self.model_name}")
                    print("2. Click 'Agree and access repository' button")
                    print("3. Wait a few seconds for approval")
                    print("4. Run the script again")
                
                print("\n⚠️  NOTE: You need access to THREE models:")
                print("   - https://huggingface.co/pyannote/speaker-diarization-3.1")
                print("   - https://huggingface.co/pyannote/speaker-diarization-community-1")
                print("   - https://huggingface.co/pyannote/segmentation-3.0")
                print("\nYour HuggingFace token must have 'Read' access.")
                print("="*70 + "\n")
            raise
        
        print("[INFO] Diarization model loaded successfully")
    
    def diarize(self, audio_path: str) -> List[DiarizationSegment]:
        """
        Perform speaker diarization on audio file.
        
        Args:
            audio_path: Path to audio file (preferably WAV)
        
        Returns:
            List of DiarizationSegment objects with speaker labels and timing
        """
        if not os.path.isfile(audio_path):
            raise FileNotFoundError(f"Audio file not found: {audio_path}")
        
        # Initialize pipeline if needed
        self._init_pipeline()
        
        print(f"[INFO] Starting diarization: {audio_path}")
        
        # Use preloaded audio by default to avoid torchcodec issues on Windows
        # This is more reliable than trying to use torchcodec first
        try:
            diarization = self._diarize_with_preloaded_audio(audio_path)
        except Exception as e:
            error_msg = str(e).lower()
            # If preloading fails, try direct path as fallback
            if "torchaudio" in error_msg or "load" in error_msg:
                print("[WARN] Audio preloading failed, trying direct path...")
                try:
                    diarization = self.pipeline(audio_path)
                except Exception as e2:
                    print(f"[ERROR] Both methods failed. Original error: {e}")
                    raise e
            else:
                raise
        
        # Convert to list of segments
        segments = []
        
        # Handle different pyannote API versions
        # New API: DiarizeOutput object with .speaker_diarization attribute
        # Old API: Annotation object with itertracks() method
        if hasattr(diarization, 'speaker_diarization'):
            # New API (pyannote 3.1+) - DiarizeOutput dataclass
            print("[DEBUG] Using new pyannote API (DiarizeOutput.speaker_diarization)")
            diarization_annotation = diarization.speaker_diarization
        elif hasattr(diarization, 'itertracks'):
            # Old API (pyannote < 3.1) - direct Annotation object
            print("[DEBUG] Using old pyannote API (Annotation)")
            diarization_annotation = diarization
        else:
            # Unknown format, try to inspect
            print(f"[WARN] Unknown diarization format: {type(diarization)}")
            print(f"[DEBUG] Available attributes: {dir(diarization)}")
            raise TypeError(
                f"Unknown diarization output format: {type(diarization)}. "
                f"Expected Annotation or DiarizeOutput with speaker_diarization attribute."
            )
        
        # Iterate through segments
        for turn, _, speaker in diarization_annotation.itertracks(yield_label=True):
            segments.append(DiarizationSegment(
                speaker=speaker,
                start=turn.start,
                end=turn.end
            ))
        
        # Sort by start time
        segments.sort(key=lambda x: x.start)
        
        print(f"[INFO] Diarization complete: {len(segments)} segments found")
        
        # Print summary
        speakers = set(seg.speaker for seg in segments)
        print(f"[INFO] Detected speakers: {', '.join(sorted(speakers))}")
        
        return segments
    
    def _diarize_with_preloaded_audio(self, audio_path: str):
        """
        Run diarization with pre-loaded audio (workaround for torchcodec issues).
        
        This method is used by default on Windows to avoid torchcodec/FFmpeg issues.
        
        Args:
            audio_path: Path to audio file
        
        Returns:
            Diarization result
        """
        try:
            import torch
            import torchaudio
        except ImportError as e:
            raise ImportError(
                "torchaudio is required for audio loading. "
                f"Install it with: pip install torchaudio\nError: {e}"
            )
        
        # Check if soundfile is available
        try:
            import soundfile
            print(f"[INFO] soundfile is available (version {soundfile.__version__})")
        except ImportError:
            raise ImportError(
                "soundfile is required for audio loading on Windows. "
                "Install it with: pip install soundfile"
            )
        
        print("[INFO] Loading audio with soundfile backend (bypassing torchcodec)...")
        
        # Try multiple methods to load audio with soundfile backend
        waveform = None
        sample_rate = None
        
        # Method 1: Try direct soundfile load and convert to torch
        try:
            print("[DEBUG] Method 1: Using soundfile directly...")
            data, sample_rate = soundfile.read(audio_path, dtype='float32')
            # Convert to torch tensor: (channels, samples)
            waveform = torch.from_numpy(data.T if len(data.shape) > 1 else data.reshape(1, -1))
            print(f"[INFO] Audio loaded successfully with soundfile: {waveform.shape}")
        except Exception as e:
            print(f"[WARN] Method 1 failed: {e}")
            
            # Method 2: Try torchaudio with soundfile backend
            try:
                print("[DEBUG] Method 2: Using torchaudio.backend.soundfile_backend...")
                waveform, sample_rate = torchaudio.backend.soundfile_backend.load(audio_path)
                print(f"[INFO] Audio loaded successfully with torchaudio soundfile backend: {waveform.shape}")
            except (AttributeError, Exception) as e2:
                print(f"[WARN] Method 2 failed: {e2}")
                
                # Method 3: Try setting backend globally (older API)
                try:
                    print("[DEBUG] Method 3: Setting torchaudio backend globally...")
                    torchaudio.set_audio_backend("soundfile")
                    waveform, sample_rate = torchaudio.load(audio_path)
                    print(f"[INFO] Audio loaded successfully with global backend: {waveform.shape}")
                except Exception as e3:
                    print(f"[ERROR] All methods failed!")
                    print(f"  Method 1 error: {e}")
                    print(f"  Method 2 error: {e2}")
                    print(f"  Method 3 error: {e3}")
                    raise RuntimeError(
                        f"Failed to load audio file using all available methods.\n"
                        f"File: {audio_path}\n"
                        f"Last error: {e3}"
                    )
        
        if waveform is None or sample_rate is None:
            raise RuntimeError(f"Failed to load audio: {audio_path}")
        
        # Ensure mono audio (average channels if stereo)
        if waveform.shape[0] > 1:
            print(f"[INFO] Converting stereo to mono (averaging {waveform.shape[0]} channels)")
            waveform = torch.mean(waveform, dim=0, keepdim=True)
        
        # Prepare audio dict for pyannote
        audio_dict = {
            "waveform": waveform,
            "sample_rate": sample_rate
        }
        
        print(f"[INFO] Audio loaded: sample_rate={sample_rate}, shape={waveform.shape}")
        
        # Calculate audio duration
        duration_seconds = waveform.shape[1] / sample_rate
        duration_minutes = duration_seconds / 60
        print(f"[INFO] Audio duration: {duration_minutes:.1f} minutes ({duration_seconds:.1f} seconds)")
        print("[INFO] Running diarization on preloaded audio...")
        print("[INFO] ⏳ This may take 10-60 minutes on CPU depending on audio length and system performance...")
        print("[INFO] 💡 The process is running even if it appears frozen. Monitor CPU usage to confirm.")
        
        import time
        start_time = time.time()
        
        # Run diarization with preloaded audio
        result = self.pipeline(audio_dict)
        
        elapsed = time.time() - start_time
        print(f"[INFO] ✅ Diarization completed in {elapsed:.1f} seconds ({elapsed/60:.1f} minutes)")
        
        return result
    
    def save_segments_to_json(self, segments: List[DiarizationSegment], output_path: str):
        """
        Save diarization segments to JSON file.
        
        Args:
            segments: List of DiarizationSegment objects
            output_path: Path to output JSON file
        """
        import json
        
        data = [
            {
                "speaker": seg.speaker,
                "start": seg.start,
                "end": seg.end
            }
            for seg in segments
        ]
        
        os.makedirs(os.path.dirname(output_path) or ".", exist_ok=True)
        
        with open(output_path, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        
        print(f"[INFO] Saved diarization segments: {output_path}")
    
    def extract_audio_segment(
        self,
        source_audio_path: str,
        output_path: str,
        start: float,
        end: float
    ) -> str:
        """
        Extract audio segment using soundfile (Python 3.13 compatible).
        
        Args:
            source_audio_path: Path to source audio file
            output_path: Path for output segment
            start: Start time in seconds
            end: End time in seconds
        
        Returns:
            Path to extracted segment
        """
        try:
            import soundfile
            import numpy as np
        except ImportError as e:
            raise ImportError(
                f"soundfile and numpy are required for audio extraction. "
                f"Install them with: pip install soundfile numpy\n"
                f"Error: {e}"
            )
        
        # Load audio file
        data, sample_rate = soundfile.read(source_audio_path, dtype='float32')
        
        # Calculate sample indices
        start_sample = int(start * sample_rate)
        end_sample = int(end * sample_rate)
        
        # Ensure indices are within bounds
        start_sample = max(0, start_sample)
        end_sample = min(len(data), end_sample)
        
        # Extract segment
        segment = data[start_sample:end_sample]
        
        # Handle empty segments
        if len(segment) == 0:
            print(f"[WARN] Empty segment: {start}s - {end}s, creating silence")
            # Create 0.1 second of silence
            segment = np.zeros(int(0.1 * sample_rate), dtype='float32')
        
        # Create output directory if needed
        os.makedirs(os.path.dirname(output_path) or ".", exist_ok=True)
        
        # Save segment as WAV
        soundfile.write(output_path, segment, sample_rate)
        
        return output_path

