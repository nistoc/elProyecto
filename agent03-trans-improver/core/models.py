#!/usr/bin/env python3
"""
Data models for transcript improver.
"""
from dataclasses import dataclass
from typing import List, Optional


@dataclass
class BatchInfo:
    """Information about a batch to process."""
    index: int  # Batch index (0-based)
    start_line: int  # Start line number (0-based)
    end_line: int  # End line number (exclusive)
    lines: List[str]  # Actual lines to process
    context: Optional[List[str]] = None  # Context from previous batch


@dataclass
class BatchResult:
    """Result of processing a single batch."""
    batch_index: int
    fixed_lines: List[str]
    success: bool
    error: Optional[str] = None

