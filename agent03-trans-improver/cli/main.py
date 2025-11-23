#!/usr/bin/env python3
"""
CLI entry point for Agent03: Transcript Improver.
"""
import sys
import os
from datetime import datetime

from core.config import Config
from services.fixer import TranscriptFixer


def add_timestamp_to_filename(filename: str) -> str:
    """
    Add timestamp to filename before extension.
    
    Example: 'transcript_fixed.md' -> 'transcript_fixed_2024-11-23_15-30-45.md'
    """
    timestamp = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
    
    # Split filename and extension
    if '.' in filename:
        name, ext = filename.rsplit('.', 1)
        return f"{name}_{timestamp}.{ext}"
    else:
        return f"{filename}_{timestamp}"


def main():
    """Main entry point for transcript improver."""
    
    # Determine config path
    config_path = os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        "config",
        "default.json"
    )
    
    # Load configuration
    try:
        config = Config.from_file(config_path)
    except FileNotFoundError:
        print(f"[ERROR] Configuration file not found: {config_path}")
        sys.exit(2)
    except Exception as e:
        print(f"[ERROR] Failed to load configuration: {e}")
        sys.exit(1)
    
    # Print execution plan
    config.print_plan(config_path)
    
    # Validate API key
    api_key = config.get("openai_api_key")
    if not api_key:
        print("[ERROR] OPENAI_API_KEY not set!")
        print("Please set it in environment or config file:")
        print("  export OPENAI_API_KEY='sk-...'")
        sys.exit(1)
    
    # Validate input file
    input_file = config.get("input_file")
    if not os.path.exists(input_file):
        print(f"[ERROR] Input file not found: {input_file}")
        print()
        print("Please copy your transcript file to this location:")
        print(f"  cp /path/to/transcript.md {input_file}")
        sys.exit(1)
    
    # Prepare output filename
    output_file = config.get("output_file")
    
    # Add timestamp if configured
    if config.get("add_timestamp_to_output", False):
        output_file = add_timestamp_to_filename(output_file)
        print(f"[INFO] Output will be saved to: {output_file}")
    
    # Check if output exists and skip_if_exists is set
    if config.get("skip_if_exists") and os.path.exists(output_file):
        print(f"[INFO] Output file already exists, skipping: {output_file}")
        sys.exit(0)
    
    # Initialize fixer
    try:
        fixer = TranscriptFixer(
            api_key=api_key,
            model=config.get("model"),
            temperature=config.get("temperature"),
            base_url=config.get("openai_base_url"),
            organization=config.get("openai_organization"),
            prompt_file=config.get("prompt_file")
        )
    except Exception as e:
        print(f"[ERROR] Failed to initialize fixer: {e}")
        sys.exit(1)
    
    # Process transcript
    try:
        fixer.fix_transcript_file(
            input_path=input_file,
            output_path=output_file,
            batch_size=config.get("batch_size"),
            context_lines=config.get("context_lines"),
            save_intermediate=config.get("save_intermediate"),
            intermediate_dir=config.get("intermediate_dir")
        )
    except KeyboardInterrupt:
        print()
        print("[WARN] Interrupted by user!")
        print("[INFO] Partial results may be saved in intermediate directory.")
        sys.exit(130)
    except Exception as e:
        print(f"[ERROR] Processing failed: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)


if __name__ == "__main__":
    main()

