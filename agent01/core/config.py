#!/usr/bin/env python3
"""
Configuration management for transcription system.
"""
import json
import os
from typing import Dict, Any, List


class Config:
    """Manages configuration loading and validation."""
    
    def __init__(self, config_dict: Dict[str, Any]):
        self._config = config_dict
        self._apply_defaults()
        self._resolve_env_vars()
    
    @classmethod
    def from_file(cls, path: str) -> 'Config':
        """Load configuration from JSON file."""
        with open(path, 'r', encoding='utf-8') as f:
            config_dict = json.load(f)
        return cls(config_dict)
    
    def _resolve_env_vars(self):
        """Resolve environment variable references (env:VAR_NAME)."""
        def resolve(v):
            if isinstance(v, str) and v.startswith("env:"):
                var_name = v.split(":", 1)[1]
                return os.getenv(var_name, None)
            return v
        
        for key in list(self._config.keys()):
            self._config[key] = resolve(self._config[key])
    
    def _apply_defaults(self):
        """Apply default values for missing configuration keys."""
        defaults = {
            "model": "gpt-4o-transcribe-diarize",
            "md_output_path": "{base}.md",
            "raw_json_output_path": "{base}.json",
            "pre_split": True,
            "target_chunk_mb": 24.5,
            "split_workdir": "chunks",
            "ffmpeg_path": "ffmpeg",
            "ffprobe_path": "ffprobe",
            "reencode_if_needed": True,
            "reencode_bitrate_kbps": 64,
            "chunk_naming": "{base}_part_%03d.m4a",
            "cache_dir": "cache",
            "chunk_overlap_sec": 2.0,
            "temperature": 0.0,
        }
        for key, value in defaults.items():
            self._config.setdefault(key, value)
    
    def get(self, key: str, default=None):
        """Get configuration value by key."""
        return self._config.get(key, default)
    
    def get_files(self) -> List[str]:
        """Get list of input files from configuration."""
        files = []
        if isinstance(self._config.get("files"), list) and self._config["files"]:
            files = self._config["files"]
        elif self._config.get("file"):
            files = [self._config["file"]]
        return files
    
    def get_sanitized(self) -> Dict[str, Any]:
        """Get sanitized config dict (masks sensitive data)."""
        sanitized = dict(self._config)
        for key in ["openai_api_key"]:
            if sanitized.get(key):
                sanitized[key] = "*** (set) ***"
        return sanitized
    
    def to_dict(self) -> Dict[str, Any]:
        """Get full configuration as dictionary."""
        return dict(self._config)
    
    def print_plan(self, config_path: str):
        """Print execution plan with configuration details."""
        plan_lines = [
            "Execution plan:",
            f"- Load config from: {config_path}",
            f"- Model: {self.get('model')}",
            f"- Files: {self.get_files()}",
            f"- Language: {self.get('language')} (null => auto / multi-language)",
            f"- Temperature: {self.get('temperature')}",
            f"- Prompt provided: {'yes' if self.get('prompt') else 'no'}",
            f"- Request speaker labels from API: {bool(self.get('openai_speaker_diarization'))}",
            f"- Pre-splitting: {bool(self.get('pre_split'))} (target <= {self.get('target_chunk_mb')} MB)",
            f"- Chunk overlap: {float(self.get('chunk_overlap_sec') or 0.0)} sec",
            f"- Split workdir: {self.get('split_workdir')} | ffmpeg: {self.get('ffmpeg_path')} | ffprobe: {self.get('ffprobe_path')}",
            f"- Cache dir: {self.get('cache_dir')}",
            f"- Save per-chunk JSON: {bool(self.get('save_per_chunk_json'))} -> dir: {self.get('per_chunk_json_dir')}",
            "",
            "Sanitized config (effective):",
            json.dumps(self.get_sanitized(), ensure_ascii=False, indent=2)
        ]
        print("\n".join(plan_lines))

