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
        # Always overwrite to ensure clean start
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
                # BUT: if segment has zero timestamps (0.0, 0.0), it means it's a fallback from raw_response.text
                # In that case, we should include it regardless of emit_guard
                is_fallback_segment = (seg.start == 0.0 and seg.end == 0.0)
                
                if not is_fallback_segment and (seg.start + EPS) < emit_guard:
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
    
    def insert_split_result_into_transcript(
        self,
        md_path: str,
        segments: List[ASRSegment],
        chunk_offset: float,
        emit_guard: float = 0.0
    ) -> bool:
        """
        Insert split transcription results into the main transcript file
        at the correct position based on timestamps.
        
        Args:
            md_path: Path to the main transcript.md file
            segments: List of ASRSegment from the merged split result
            chunk_offset: The offset of the original chunk in the full audio
            emit_guard: Emit guard for filtering segments
        
        Returns:
            True if successful, False otherwise
        """
        import re
        
        if not os.path.exists(md_path):
            print(f"[ERROR] Transcript file not found: {md_path}")
            return False
        
        try:
            # Read existing content
            with open(md_path, 'r', encoding='utf-8') as f:
                content = f.read()
            
            lines = content.split('\n')
            
            # Parse existing lines to find insertion point
            # Format: "- 123.45 speaker_0: "text""
            timestamp_pattern = re.compile(r'^- (\d+\.?\d*) ')
            
            # Find where to insert based on chunk_offset
            insert_index = None
            for i, line in enumerate(lines):
                match = timestamp_pattern.match(line)
                if match:
                    line_timestamp = float(match.group(1))
                    if line_timestamp > chunk_offset:
                        insert_index = i
                        break
            
            # If no insertion point found, append before the closing marker
            if insert_index is None:
                # Find the closing marker
                for i, line in enumerate(lines):
                    if line.strip() == "<<<<<":
                        insert_index = i
                        break
                if insert_index is None:
                    insert_index = len(lines)
            
            # Format the new segments
            new_lines = []
            EPS = 1e-3
            
            for seg in segments:
                if (seg.start + EPS) < emit_guard:
                    continue
                
                speaker = self._normalize_speaker(seg.speaker)
                timestamp = f"{seg.start + chunk_offset:.2f}"
                text = (seg.text or "").replace('"', '\\"')
                new_lines.append(f"- {timestamp} {speaker}: \"{text}\"")
            
            if not new_lines:
                print(f"[WARN] No segments to insert for chunk at offset {chunk_offset}")
                return True
            
            # Insert the new lines
            lines = lines[:insert_index] + new_lines + lines[insert_index:]
            
            # Write back
            with open(md_path, 'w', encoding='utf-8') as f:
                f.write('\n'.join(lines))
            
            print(f"[INFO] Inserted {len(new_lines)} lines at position {insert_index} in {md_path}")
            return True
            
        except Exception as e:
            print(f"[ERROR] Failed to insert split result: {e}")
            import traceback
            traceback.print_exc()
            return False
    
    def insert_split_result_into_json(
        self,
        json_path: str,
        result: 'TranscriptionResult',
        chunk_index: int
    ) -> bool:
        """
        Insert split transcription result into the main response.json file.
        
        Args:
            json_path: Path to the main response.json file
            result: The merged TranscriptionResult
            chunk_index: The index of the original chunk
        
        Returns:
            True if successful, False otherwise
        """
        if not os.path.exists(json_path):
            print(f"[WARN] JSON file not found, creating new: {json_path}")
            data = {"chunks": []}
        else:
            try:
                with open(json_path, 'r', encoding='utf-8') as f:
                    data = json.load(f)
            except Exception as e:
                print(f"[ERROR] Failed to read JSON: {e}")
                return False
        
        if "chunks" not in data:
            data["chunks"] = []
        
        # Create chunk entry
        chunk_entry = {
            "chunk": result.chunk_basename,
            "chunk_index": chunk_index,
            "offset": result.offset,
            "emit_guard": result.emit_guard,
            "was_split": True,
            "segments": [
                {
                    "start": seg.start,
                    "end": seg.end,
                    "text": seg.text,
                    "speaker": seg.speaker
                }
                for seg in result.segments
            ]
        }
        
        # Find if this chunk index already exists and replace, or insert at right position
        inserted = False
        for i, existing in enumerate(data["chunks"]):
            existing_idx = existing.get("chunk_index", i)
            if existing_idx == chunk_index:
                data["chunks"][i] = chunk_entry
                inserted = True
                break
            elif existing_idx > chunk_index:
                data["chunks"].insert(i, chunk_entry)
                inserted = True
                break
        
        if not inserted:
            data["chunks"].append(chunk_entry)
        
        try:
            with open(json_path, 'w', encoding='utf-8') as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
            print(f"[INFO] Updated JSON with split result: {json_path}")
            return True
        except Exception as e:
            print(f"[ERROR] Failed to write JSON: {e}")
            return False

