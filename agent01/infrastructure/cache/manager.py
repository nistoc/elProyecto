#!/usr/bin/env python3
"""
Cache management for transcription results.
"""
import os
import json
import hashlib
from typing import Dict, Any, Optional


class CacheManager:
    """Manages caching of transcription results by file fingerprint."""
    
    def __init__(self, cache_dir: str):
        self.cache_dir = cache_dir
        os.makedirs(cache_dir, exist_ok=True)
    
    def get_manifest_path(self, base_name: str) -> str:
        """Get path to manifest file for given base name."""
        return os.path.join(self.cache_dir, f"{base_name}.manifest.json")
    
    def load_manifest(self, manifest_path: str) -> Dict[str, Any]:
        """Load manifest from file or return empty structure."""
        if os.path.isfile(manifest_path):
            try:
                with open(manifest_path, 'r', encoding='utf-8') as f:
                    return json.load(f)
            except Exception as e:
                print(f"[WARN] Failed to load manifest {manifest_path}: {e}")
        return {"chunks": {}}
    
    def save_manifest(self, manifest_path: str, manifest: Dict[str, Any]):
        """Save manifest to file."""
        try:
            with open(manifest_path, 'w', encoding='utf-8') as f:
                json.dump(manifest, f, ensure_ascii=False, indent=2)
        except Exception as e:
            print(f"[ERROR] Failed to save manifest {manifest_path}: {e}")
    
    def get_file_fingerprint(self, file_path: str) -> str:
        """Calculate SHA256 fingerprint of file."""
        h = hashlib.sha256()
        with open(file_path, 'rb') as f:
            for chunk in iter(lambda: f.read(1024*1024), b''):
                h.update(chunk)
        return h.hexdigest()
    
    def get_cached_response(
        self,
        manifest: Dict[str, Any],
        chunk_basename: str,
        fingerprint: str
    ) -> Optional[Dict[str, Any]]:
        """
        Get cached response for chunk if fingerprint matches.
        
        Args:
            manifest: Loaded manifest dictionary
            chunk_basename: Basename of the chunk file
            fingerprint: Current file fingerprint
        
        Returns:
            Cached response dict or None if not found/outdated
        """
        cached = manifest.get("chunks", {}).get(chunk_basename)
        if cached and cached.get("fingerprint") == fingerprint:
            if "response" in cached:
                return cached["response"]
        return None
    
    def cache_response(
        self,
        manifest: Dict[str, Any],
        manifest_path: str,
        chunk_basename: str,
        fingerprint: str,
        response: Dict[str, Any]
    ):
        """
        Cache a response in the manifest.
        
        Args:
            manifest: Manifest dictionary to update
            manifest_path: Path to save manifest
            chunk_basename: Basename of the chunk file
            fingerprint: File fingerprint
            response: API response to cache
        """
        manifest.setdefault("chunks", {})[chunk_basename] = {
            "fingerprint": fingerprint,
            "response": response
        }
        self.save_manifest(manifest_path, manifest)

