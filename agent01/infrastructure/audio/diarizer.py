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
        self.pipeline = Pipeline.from_pretrained(
            self.model_name,
            use_auth_token=self.huggingface_token
        )
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
        
        # Run diarization
        diarization = self.pipeline(audio_path)
        
        # Convert to list of segments
        segments = []
        for turn, _, speaker in diarization.itertracks(yield_label=True):
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
        Extract audio segment using pydub.
        
        Args:
            source_audio_path: Path to source audio file
            output_path: Path for output segment
            start: Start time in seconds
            end: End time in seconds
        
        Returns:
            Path to extracted segment
        """
        try:
            from pydub import AudioSegment
        except ImportError:
            raise ImportError(
                "pydub is not installed. "
                "Install it with: pip install pydub"
            )
        
        # Load audio
        audio = AudioSegment.from_wav(source_audio_path)
        
        # Extract segment (pydub uses milliseconds)
        start_ms = int(start * 1000)
        end_ms = int(end * 1000)
        segment = audio[start_ms:end_ms]
        
        # Create output directory if needed
        os.makedirs(os.path.dirname(output_path) or ".", exist_ok=True)
        
        # Export segment
        segment.export(output_path, format="wav")
        
        return output_path

