#!/usr/bin/env python3
"""
OpenAI API client for transcription.
"""
import os
import sys
import json
import time
from typing import Dict, Any, List, Optional
from core.models import ASRSegment

# Load environment variables from .env file
try:
    from dotenv import load_dotenv
    load_dotenv()
except ImportError:
    # python-dotenv not installed, skip
    pass


class OpenAITranscriptionClient:
    """Client for OpenAI transcription API with fallback support."""
    
    def __init__(
        self,
        api_key: Optional[str] = None,
        base_url: Optional[str] = None,
        organization: Optional[str] = None,
        model: str = "gpt-4o-transcribe-diarize",
        fallback_models: Optional[List[str]] = None
    ):
        self.api_key = api_key or os.getenv("OPENAI_API_KEY")
        if not self.api_key:
            raise ValueError("OPENAI_API_KEY not provided")
        
        try:
            from openai import OpenAI
        except ImportError:
            print("[FATAL] Missing 'openai' package. Install: pip install openai", file=sys.stderr)
            raise
        
        self.client = OpenAI(
            api_key=self.api_key,
            base_url=base_url,
            organization=organization
        )
        self.model = model
        self.fallback_models = fallback_models or ["gpt-4o-mini-transcribe", "whisper-1"]
    
    def transcribe(
        self,
        audio_path: str,
        language: Optional[str] = None,
        prompt: Optional[str] = None,
        temperature: Optional[float] = None,
        response_format: Optional[str] = None,
        timestamp_granularities: Optional[List[str]] = None
    ) -> Dict[str, Any]:
        """
        Transcribe audio file using OpenAI API with retry and fallback logic.
        
        Args:
            audio_path: Path to audio file
            language: Language code (None for auto-detect)
            prompt: Optional prompt for the model
            temperature: Sampling temperature
            response_format: Response format (e.g., "json")
            timestamp_granularities: List of granularities (e.g., ["segment"])
        
        Returns:
            Raw API response as dictionary
        """
        # Build base kwargs
        base_kwargs: Dict[str, Any] = {
            "prompt": prompt,
            "temperature": temperature,
            "language": language,
            "response_format": response_format,
            "timestamp_granularities": timestamp_granularities,
        }
        base_kwargs = {k: v for k, v in base_kwargs.items() if v is not None}
        
        # Adjust for diarization models
        if "diarize" in self.model.lower():
            if "prompt" in base_kwargs:
                print("[WARN] Prompt is not supported for diarization models; dropping 'prompt'.")
                base_kwargs.pop("prompt", None)
            if "chunking_strategy" not in base_kwargs:
                base_kwargs["chunking_strategy"] = "auto"
        
        # Try primary model and fallbacks
        models_to_try = [self.model] + [m for m in self.fallback_models if m != self.model]
        
        last_err = None
        for model in models_to_try:
            # Generate variations of parameters to try
            variations = self._generate_param_variations(base_kwargs)
            
            for idx, kwargs in enumerate(variations, 1):
                for attempt_no in range(3):
                    try:
                        print(f"[INFO] Sending file to OpenAI transcription API… (model={model}, variant={idx}, attempt={attempt_no})")
                        with open(audio_path, "rb") as f:
                            resp = self.client.audio.transcriptions.create(
                                file=f,
                                model=model,
                                **kwargs
                            )
                        
                        # Convert response to dict
                        if hasattr(resp, "model_dump_json"):
                            return json.loads(resp.model_dump_json())
                        if isinstance(resp, dict):
                            return resp
                        return json.loads(json.dumps(resp, default=lambda o: getattr(o, "__dict__", str(o))))
                    
                    except Exception as e:
                        last_err = e
                        msg = str(e)
                        
                        # Retry on server errors
                        if "InternalServerError" in msg or "status_code=500" in msg or "Error code: 500" in msg:
                            sleep_s = 2 ** attempt_no
                            print(f"[WARN] Server 500 on model {model}, variant {idx}. Retrying in {sleep_s}s…")
                            time.sleep(sleep_s)
                            continue
                        
                        print(f"[WARN] Non-retryable error on model {model}, variant {idx}: {msg}")
                        break
            
            print(f"[INFO] Model {model} failed; trying next fallback if any…")
        
        # All attempts failed
        print("[FATAL] All models/variations failed.", file=sys.stderr)
        if last_err:
            print(f"[DETAILS] Last error: {last_err}", file=sys.stderr)
        raise RuntimeError("Transcription failed for all models")
    
    def _generate_param_variations(self, base_kwargs: Dict[str, Any]) -> List[Dict[str, Any]]:
        """Generate parameter variations to try in case of API errors."""
        variations = [dict(base_kwargs)]
        
        # Try without timestamp_granularities
        if "timestamp_granularities" in base_kwargs:
            v2 = dict(base_kwargs)
            v2.pop("timestamp_granularities", None)
            variations.append(v2)
        
        # Try without response_format
        if "response_format" in base_kwargs:
            v3 = dict(base_kwargs)
            v3.pop("response_format", None)
            variations.append(v3)
        
        # Try different chunking strategies for diarization
        if "diarize" in self.model.lower():
            for cs in ("auto", "none"):
                vv = dict(base_kwargs)
                vv["chunking_strategy"] = cs
                variations.append(vv)
                
                vvr = dict(vv)
                vvr.pop("response_format", None)
                variations.append(vvr)
        
        # Minimal parameters
        variations.append({
            k: v for k, v in base_kwargs.items()
            if k in ("prompt", "temperature", "language")
        })
        
        return variations
    
    @staticmethod
    def parse_segments(raw_response: Dict[str, Any]) -> List[ASRSegment]:
        """
        Parse ASR segments from API response.
        
        Args:
            raw_response: Raw API response dictionary
        
        Returns:
            List of ASRSegment objects
        """
        segments: List[ASRSegment] = []
        
        if isinstance(raw_response, dict) and "segments" in raw_response:
            if isinstance(raw_response["segments"], list):
                for s in raw_response["segments"]:
                    start = max(0.0, float(s.get("start", 0.0)))
                    end = max(start, float(s.get("end", start)))
                    text = (s.get("text") or "").strip()
                    speaker = s.get("speaker") or s.get("speaker_label")
                    segments.append(ASRSegment(start, end, text, speaker))
        
        elif "text" in raw_response:
            txt = (raw_response.get("text") or "").strip()
            segments.append(ASRSegment(0.0, 0.0, txt, None))
        
        else:
            # Fallback: dump entire response as text
            txt = json.dumps(raw_response, ensure_ascii=False)
            segments.append(ASRSegment(0.0, 0.0, txt, None))
        
        return segments

