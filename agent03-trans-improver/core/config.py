#!/usr/bin/env python3
"""
Configuration management for transcript improver.
"""
import json
import os
from typing import Dict, Any


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
            "input_file": "transcript.md",
            "output_file": "transcript_fixed.md",
            "model": "gpt-4o-mini",
            "temperature": 0.0,
            "batch_size": 10,
            "context_lines": 3,
            "save_intermediate": True,
            "intermediate_dir": "intermediate_fixes",
            "skip_if_exists": False,
            "verbose": True,
        }
        for key, value in defaults.items():
            self._config.setdefault(key, value)
    
    def get(self, key: str, default=None):
        """Get configuration value by key."""
        return self._config.get(key, default)
    
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
            "",
            "=" * 60,
            "Agent03: Transcript Improver",
            "=" * 60,
            "Execution plan:",
            f"- Config: {config_path}",
            f"- Input file: {self.get('input_file')}",
            f"- Output file: {self.get('output_file')}",
            f"- Model: {self.get('model')}",
            f"- Batch size: {self.get('batch_size')} lines",
            f"- Context lines: {self.get('context_lines')}",
            f"- Temperature: {self.get('temperature')}",
            f"- Save intermediate: {self.get('save_intermediate')}",
            f"- Intermediate dir: {self.get('intermediate_dir')}",
            "",
            "Sanitized config:",
            json.dumps(self.get_sanitized(), ensure_ascii=False, indent=2),
            "=" * 60,
            ""
        ]
        print("\n".join(plan_lines))

