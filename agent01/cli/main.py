#!/usr/bin/env python3
"""
Main entry point for agent01 transcription system.

Usage:
    python -m cli.main --config config/default.json
"""
import argparse
import sys
import os

# Load environment variables from .env file
try:
    from dotenv import load_dotenv
    load_dotenv()
except ImportError:
    # python-dotenv not installed, skip
    pass

# Add parent directory to path if running as script
if __name__ == "__main__" and __package__ is None:
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from core import Config
from services import TranscriptionPipeline


def main():
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Transcribe audio with OpenAI API using modular pipeline"
    )
    parser.add_argument(
        "--config",
        default="config/default.json",
        help="Path to JSON config file"
    )
    args = parser.parse_args()
    
    # Check config exists
    if not os.path.isfile(args.config):
        print(f"[FATAL] Config not found: {args.config}", file=sys.stderr)
        sys.exit(2)
    
    try:
        # Load configuration
        config = Config.from_file(args.config)
        
        # Print execution plan
        config.print_plan(args.config)
        
        # Create and run pipeline
        pipeline = TranscriptionPipeline(config)
        results = pipeline.process_all_files()
        
        # Print summary
        print("\n" + "="*60)
        print("TRANSCRIPTION COMPLETE")
        print("="*60)
        for md_path, json_path in results:
            print(f"OK Markdown: {md_path}")
            print(f"OK JSON: {json_path}")
        
        sys.stdout.flush()
        sys.stderr.flush()
        # Force exit to avoid waiting for non-daemon threads
        os._exit(0)
        
    except Exception as e:
        print(f"[FATAL] {e}", file=sys.stderr)
        import traceback
        traceback.print_exc()
        sys.stdout.flush()
        sys.stderr.flush()
        os._exit(1)


if __name__ == "__main__":
    main()

