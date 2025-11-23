#!/usr/bin/env python3
"""
Output formatting and writing for transcription results.
"""
import os
import json
from typing import List, Dict, Any, Optional
from core.models import ASRSegment, TranscriptionResult


class OutputWriter:
    """Handles writing transcription results to various formats."""
    
    def __init__(self, output_dir: Optional[str] = None):
        self.output_dir = output_dir or "."
        self.speaker_map: Dict[str, str] = {}
    
    def get_output_paths(self, base_name: str, md_pattern: str, json_pattern: str) -> tuple:
        """Get output paths for Markdown and JSON files."""
        md_path = md_pattern.format(base=base_name)
        json_path = json_pattern.format(base=base_name)
        return md_path, json_path
    
    def initialize_markdown(self, md_path: str):
        """Initialize markdown file with header."""
        if not os.path.isfile(md_path):
            with open(md_path, 'w', encoding='utf-8') as f:
                f.write(">>>>>>>\n")
    
    def append_segments_to_markdown(
        self,
        md_path: str,
        segments: List[ASRSegment],
        offset: float,
        emit_guard: float
    ):
        """
        Append segments to markdown file with speaker labels.
        
        Args:
            md_path: Path to markdown file
            segments: List of ASRSegment objects
            offset: Global time offset to add
            emit_guard: Local time threshold - skip segments before this
        """
        EPS = 1e-3
        
        with open(md_path, 'a', encoding='utf-8') as f:
            for seg in segments:
                # Skip segments in overlap region (emit guard)
                if (seg.start + EPS) < emit_guard:
                    continue
                
                # Normalize speaker label
                speaker = self._normalize_speaker(seg.speaker)
                
                # Format timestamp and text
                timestamp = f"{seg.start + offset:.2f}"
                text = (seg.text or "").replace('"', '\\"')
                
                f.write(f"- {timestamp} {speaker}: \"{text}\"\n")
    
    def finalize_markdown(self, md_path: str):
        """Add closing marker to markdown file."""
        with open(md_path, 'a', encoding='utf-8') as f:
            f.write("<<<<<\n")
        print(f"[INFO] Finalized Markdown: {md_path}")
    
    def save_combined_json(
        self,
        json_path: str,
        results: List[TranscriptionResult]
    ):
        """
        Save combined JSON with all chunk results.
        
        Args:
            json_path: Output JSON path
            results: List of TranscriptionResult objects
        """
        combined = {
            "chunks": [
                {
                    "chunk": r.chunk_basename,
                    "offset": r.offset,
                    "emit_guard": r.emit_guard,
                    "response": r.raw_response
                }
                for r in results
            ]
        }
        
        try:
            with open(json_path, 'w', encoding='utf-8') as f:
                json.dump(combined, f, ensure_ascii=False, indent=2)
            print(f"[INFO] Saved combined raw JSON to: {json_path}")
        except Exception as e:
            print(f"[WARN] Failed to save combined raw JSON: {e}")
    
    def save_per_chunk_json(
        self,
        chunk_basename: str,
        response: Dict[str, Any],
        output_dir: str
    ):
        """
        Save individual chunk JSON response.
        
        Args:
            chunk_basename: Basename of the chunk file
            response: Raw API response
            output_dir: Directory to save chunk JSONs
        """
        os.makedirs(output_dir, exist_ok=True)
        safe_base = os.path.splitext(os.path.basename(chunk_basename))[0]
        out_path = os.path.join(output_dir, f"{safe_base}.json")
        
        try:
            with open(out_path, "w", encoding="utf-8") as f:
                json.dump(response, f, ensure_ascii=False, indent=2)
            print(f"[INFO] Saved per-chunk JSON: {out_path}")
        except Exception as e:
            print(f"[WARN] Failed to save per-chunk JSON for {chunk_basename}: {e}")
    
    def _normalize_speaker(self, speaker_label: Optional[str]) -> str:
        """Normalize speaker label to consistent format."""
        if not speaker_label:
            return "speaker_0"
        
        if speaker_label not in self.speaker_map:
            self.speaker_map[speaker_label] = f"speaker_{len(self.speaker_map)}"
        
        return self.speaker_map[speaker_label]
    
    def reset_speaker_map(self):
        """Reset speaker mapping (for new file processing)."""
        self.speaker_map.clear()

