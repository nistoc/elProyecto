#!/usr/bin/env python3
"""
Transcript fixer service - fixes Cyrillic Spanish words in transcripts.
"""
import os
import json
from typing import List, Optional, Tuple
from openai import OpenAI

from core.models import BatchInfo, BatchResult


class TranscriptFixer:
    """Service for fixing Spanish words in transcripts."""
    
    def __init__(
        self,
        api_key: str,
        model: str = "gpt-4o-mini",
        temperature: float = 0.0,
        base_url: Optional[str] = None,
        organization: Optional[str] = None,
        prompt_file: Optional[str] = None
    ):
        """
        Initialize TranscriptFixer.
        
        Args:
            api_key: OpenAI API key
            model: Model to use for fixing
            temperature: Sampling temperature
            base_url: Optional custom API base URL
            organization: Optional OpenAI organization ID
            prompt_file: Path to custom prompt file (optional)
        """
        client_kwargs = {"api_key": api_key}
        if base_url:
            client_kwargs["base_url"] = base_url
        if organization:
            client_kwargs["organization"] = organization
        
        self.client = OpenAI(**client_kwargs)
        self.model = model
        self.temperature = temperature
        self.prompt_template = self._load_prompt_template(prompt_file)
    
    def _load_prompt_template(self, prompt_file: Optional[str]) -> Optional[str]:
        """Load prompt template from file if provided."""
        if not prompt_file:
            return None
        
        try:
            if os.path.exists(prompt_file):
                with open(prompt_file, 'r', encoding='utf-8') as f:
                    template = f.read().strip()
                print(f"[INFO] Loaded custom prompt from {prompt_file}")
                return template
            else:
                print(f"[WARN] Prompt file not found: {prompt_file}, using default prompt")
                return None
        except Exception as e:
            print(f"[WARN] Failed to load prompt file: {e}, using default prompt")
            return None
    
    def _clean_intermediate_dir(self, intermediate_dir: str) -> None:
        """Clean intermediate directory before processing."""
        if not os.path.exists(intermediate_dir):
            return
        
        try:
            import shutil
            file_count = len([f for f in os.listdir(intermediate_dir) if os.path.isfile(os.path.join(intermediate_dir, f))])
            
            if file_count > 0:
                print(f"[INFO] Cleaning {file_count} old files from {intermediate_dir}/")
                shutil.rmtree(intermediate_dir)
        except Exception as e:
            print(f"[WARN] Failed to clean intermediate directory: {e}")
    
    def fix_transcript_file(
        self,
        input_path: str,
        output_path: str,
        batch_size: int = 10,
        context_lines: int = 3,
        save_intermediate: bool = True,
        intermediate_dir: str = "intermediate_fixes"
    ) -> None:
        """
        Fix Spanish words in transcript file.
        
        Args:
            input_path: Path to original transcript
            output_path: Path to save fixed transcript
            batch_size: Number of lines per batch
            context_lines: Number of context lines from previous batch
            save_intermediate: Whether to save intermediate results
            intermediate_dir: Directory for intermediate results
        """
        print(f"\n[INFO] Reading transcript from {input_path}")
        
        if not os.path.exists(input_path):
            raise FileNotFoundError(f"Input file not found: {input_path}")
        
        # Read file
        with open(input_path, 'r', encoding='utf-8') as f:
            lines = f.readlines()
        
        # Parse structure (find >>>>>>> and <<<<< markers)
        header, content, footer = self._parse_transcript_structure(lines)
        
        print(f"[INFO] Found {len(content)} content lines to process")
        
        if len(content) == 0:
            print("[WARN] No content to process!")
            return
        
        # Clean and create intermediate directory if needed
        if save_intermediate:
            self._clean_intermediate_dir(intermediate_dir)
            os.makedirs(intermediate_dir, exist_ok=True)
        
        # Process in batches
        batches = self._create_batches(content, batch_size)
        print(f"[INFO] Total batches: {len(batches)}")
        print()
        
        fixed_content = []
        previous_context = None
        
        # Get job directory from output path to check for pause flag
        job_dir = os.path.dirname(output_path)
        pause_flag_path = os.path.join(job_dir, "pause_agent.flag")
        
        for batch_info in batches:
            # Check for pause flag before processing batch
            while os.path.exists(pause_flag_path):
                try:
                    paused_agent = open(pause_flag_path, 'r').read().strip()
                    if paused_agent == "refiner":
                        print(f"[PAUSE] Agent paused, waiting... (batch {batch_info.index + 1}/{len(batches)} will start when resumed)")
                        import time
                        time.sleep(1.0)  # Wait 1 second and check again
                        continue
                except:
                    pass
                break  # If file doesn't exist or error, continue
            
            # Add context from previous batch
            if previous_context and context_lines > 0:
                batch_info.context = previous_context[-context_lines:]
            
            print(f"[BATCH {batch_info.index + 1}/{len(batches)}] Processing lines {batch_info.start_line + 1}-{batch_info.end_line}...")
            
            # Fix the batch
            result = self._fix_batch(batch_info)
            
            if result.success:
                print(f"[API] ✓ Fixed {len(result.fixed_lines)} lines")
                fixed_content.extend(result.fixed_lines)
                
                # Save intermediate result
                if save_intermediate:
                    self._save_intermediate_batch(
                        result, intermediate_dir, batch_info.index, len(batches)
                    )
                
                # Update context for next batch
                previous_context = result.fixed_lines
            else:
                print(f"[ERROR] ✗ Failed: {result.error}")
                print(f"[WARN] Using original lines for this batch")
                fixed_content.extend(batch_info.lines)
                previous_context = batch_info.lines
        
        # Write output file
        print()
        print(f"[INFO] Writing fixed transcript to {output_path}")
        
        with open(output_path, 'w', encoding='utf-8') as f:
            f.writelines(header)
            f.writelines(fixed_content)
            f.writelines(footer)
        
        print(f"[INFO] ✓ Fixed transcript saved to {output_path}")
        print()
        print("=" * 60)
        print("Processing complete! 🎉")
        print("=" * 60)
    
    def _parse_transcript_structure(
        self, lines: List[str]
    ) -> Tuple[List[str], List[str], List[str]]:
        """
        Parse transcript structure to find header, content, and footer.
        
        Returns:
            Tuple of (header_lines, content_lines, footer_lines)
        """
        content_start = None
        content_end = None
        
        for i, line in enumerate(lines):
            if line.strip() == '>>>>>>>':
                content_start = i + 1
            elif line.strip() == '<<<<<':
                content_end = i
                break
        
        if content_start is None or content_end is None:
            print("[WARN] No >>>>>>> or <<<<< markers found, processing entire file")
            return [], lines, []
        
        header = lines[:content_start]
        content = lines[content_start:content_end]
        footer = lines[content_end:]
        
        return header, content, footer
    
    def _create_batches(
        self, lines: List[str], batch_size: int
    ) -> List[BatchInfo]:
        """Create batches from lines."""
        batches = []
        
        for i in range(0, len(lines), batch_size):
            batch_lines = lines[i:i + batch_size]
            
            batches.append(BatchInfo(
                index=len(batches),
                start_line=i,
                end_line=min(i + batch_size, len(lines)),
                lines=batch_lines
            ))
        
        return batches
    
    def _fix_batch(self, batch_info: BatchInfo) -> BatchResult:
        """
        Fix Spanish words in a single batch.
        
        Args:
            batch_info: Batch information with lines and optional context
        
        Returns:
            BatchResult with fixed lines or error
        """
        # Build prompt
        prompt = self._build_fix_prompt(batch_info)
        
        try:
            response = self.client.chat.completions.create(
                model=self.model,
                messages=[
                    {
                        "role": "system",
                        "content": "You are a precise transcript editor. You fix Cyrillic transliterations of Spanish words back to Latin script. You preserve everything else exactly as is."
                    },
                    {
                        "role": "user",
                        "content": prompt
                    }
                ],
                temperature=self.temperature
            )
            
            fixed_text = response.choices[0].message.content.strip()
            
            # Parse fixed lines
            fixed_lines = []
            for line in fixed_text.split('\n'):
                if line.strip():  # Skip empty lines
                    fixed_lines.append(line + '\n')
            
            # Log line count change (e.g., when GPT merges lines)
            if len(fixed_lines) != len(batch_info.lines):
                diff = len(fixed_lines) - len(batch_info.lines)
                action = "merged" if diff < 0 else "split"
                print(f"[INFO] GPT {action} lines: {len(batch_info.lines)} → {len(fixed_lines)} (Δ{diff:+d})")
            
            return BatchResult(
                batch_index=batch_info.index,
                fixed_lines=fixed_lines,
                success=True
            )
            
        except Exception as e:
            return BatchResult(
                batch_index=batch_info.index,
                fixed_lines=batch_info.lines,
                success=False,
                error=str(e)
            )
    
    def _build_fix_prompt(self, batch_info: BatchInfo) -> str:
        """Build prompt for fixing a batch."""
        
        # Use custom prompt template if loaded
        if self.prompt_template:
            return self._build_prompt_from_template(batch_info)
        
        # Fallback to default prompt
        return self._build_default_prompt(batch_info)
    
    def _build_prompt_from_template(self, batch_info: BatchInfo) -> str:
        """Build prompt using custom template with placeholders."""
        
        # Format context
        context_text = ""
        if batch_info.context:
            context_lines = ["Context from previous batch (for continuity):", "```"]
            for line in batch_info.context:
                context_lines.append(line.rstrip())
            context_lines.append("```")
            context_text = "\n".join(context_lines)
        else:
            context_text = "No previous context available."
        
        # Format batch
        batch_lines = ["```"]
        for line in batch_info.lines:
            batch_lines.append(line.rstrip())
        batch_lines.append("```")
        batch_text = "\n".join(batch_lines)
        
        # Replace placeholders
        prompt = self.prompt_template.replace("{context}", context_text)
        prompt = prompt.replace("{batch}", batch_text)
        
        return prompt
    
    def _build_default_prompt(self, batch_info: BatchInfo) -> str:
        """Build default prompt."""
        
        prompt_parts = [
            "You are fixing a Russian-Spanish language learning transcript.",
            "",
            "The transcript contains Russian speech where Spanish words/phrases were transcribed phonetically in Cyrillic.",
            "",
            "Your task:",
            "1. Find Spanish words written in Cyrillic (e.g. 'вале' should be 'vale', 'пор фавор' should be 'por favor')",
            "2. Replace them with correct Spanish spelling in Latin script",
            "3. Keep everything else EXACTLY as is (timestamps, speaker labels, Russian text)",
            "4. Preserve ALL formatting and line structure",
            "",
            "IMPORTANT:",
            "- Only fix Spanish words that are clearly Spanish (not Russian words)",
            "- Keep the line format: - TIME speaker_N: \"text\"",
            "- Do NOT translate, only transliterate Cyrillic Spanish back to Latin",
            "- Return the EXACT same number of lines as input",
            ""
        ]
        
        # Add context if available
        if batch_info.context:
            prompt_parts.append("Context from previous batch (for continuity):")
            prompt_parts.append("```")
            for line in batch_info.context:
                prompt_parts.append(line.rstrip())
            prompt_parts.append("```")
            prompt_parts.append("")
        
        # Add current batch
        prompt_parts.append("Current batch to fix:")
        prompt_parts.append("```")
        for line in batch_info.lines:
            prompt_parts.append(line.rstrip())
        prompt_parts.append("```")
        prompt_parts.append("")
        prompt_parts.append("Return ONLY the fixed lines (same count as input), no explanations, no markdown code blocks.")
        
        return "\n".join(prompt_parts)
    
    def _save_intermediate_batch(
        self,
        result: BatchResult,
        intermediate_dir: str,
        batch_index: int,
        total_batches: int
    ) -> None:
        """Save intermediate batch result to JSON."""
        filename = f"batch_{batch_index + 1:04d}_of_{total_batches:04d}.json"
        filepath = os.path.join(intermediate_dir, filename)
        
        data = {
            "batch_index": batch_index,
            "success": result.success,
            "error": result.error,
            "fixed_lines": [line.rstrip() for line in result.fixed_lines]
        }
        
        with open(filepath, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=2)

