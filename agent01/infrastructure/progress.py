#!/usr/bin/env python3
"""
Progress indicator utilities for long-running operations.
"""
import sys
import time
import threading
from typing import Optional


class ProgressIndicator:
    """Thread-safe progress indicator with elapsed time display."""
    
    def __init__(self, message: str = "Processing", show_spinner: bool = True):
        """
        Initialize progress indicator.
        
        Args:
            message: Message to display
            show_spinner: Whether to show animated spinner
        """
        self.message = message
        self.show_spinner = show_spinner
        self._stop_event = threading.Event()
        self._thread: Optional[threading.Thread] = None
        self._start_time: Optional[float] = None
        self._spinner_chars = ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏']
        self._spinner_idx = 0
    
    def start(self):
        """Start the progress indicator in a background thread."""
        if self._thread is not None:
            return  # Already started
        
        self._start_time = time.time()
        self._stop_event.clear()
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()
    
    def stop(self, final_message: Optional[str] = None):
        """
        Stop the progress indicator.
        
        Args:
            final_message: Optional message to display when stopped
        """
        if self._thread is None:
            return  # Not started
        
        self._stop_event.set()
        self._thread.join(timeout=1.0)
        self._thread = None
        
        # Clear the line
        sys.stdout.write('\r' + ' ' * 80 + '\r')
        sys.stdout.flush()
        
        # Print final message if provided
        if final_message:
            print(final_message)
    
    def _run(self):
        """Background thread that updates the progress indicator."""
        while not self._stop_event.is_set():
            elapsed = time.time() - self._start_time
            
            # Format elapsed time
            if elapsed < 60:
                time_str = f"{elapsed:.1f}s"
            elif elapsed < 3600:
                mins = int(elapsed // 60)
                secs = int(elapsed % 60)
                time_str = f"{mins}m {secs}s"
            else:
                hours = int(elapsed // 3600)
                mins = int((elapsed % 3600) // 60)
                time_str = f"{hours}h {mins}m"
            
            # Build status line
            if self.show_spinner:
                spinner = self._spinner_chars[self._spinner_idx % len(self._spinner_chars)]
                self._spinner_idx += 1
                status = f"\r{spinner} {self.message}... [{time_str}]"
            else:
                status = f"\r{self.message}... [{time_str}]"
            
            # Print without newline
            sys.stdout.write(status)
            sys.stdout.flush()
            
            # Sleep briefly
            self._stop_event.wait(0.1)
    
    def __enter__(self):
        """Context manager entry."""
        self.start()
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        """Context manager exit."""
        if exc_type is KeyboardInterrupt:
            self.stop(final_message="[INFO] Operation interrupted by user (Ctrl+C)")
            return False  # Re-raise the exception
        else:
            self.stop()
            return False


class ChunkProgress:
    """Simple progress tracker for chunk processing."""
    
    def __init__(self, total_chunks: int):
        """
        Initialize chunk progress tracker.
        
        Args:
            total_chunks: Total number of chunks to process
        """
        self.total = total_chunks
        self.current = 0
        self.start_time = time.time()
    
    def update(self, chunk_idx: int):
        """
        Update progress to specific chunk.
        
        Args:
            chunk_idx: Current chunk index (0-based)
        """
        self.current = chunk_idx + 1
        elapsed = time.time() - self.start_time
        
        # Calculate ETA
        if self.current > 0:
            avg_time_per_chunk = elapsed / self.current
            remaining_chunks = self.total - self.current
            eta_seconds = avg_time_per_chunk * remaining_chunks
            
            if eta_seconds < 60:
                eta_str = f"{eta_seconds:.0f}s"
            elif eta_seconds < 3600:
                mins = int(eta_seconds // 60)
                eta_str = f"{mins}m"
            else:
                hours = int(eta_seconds // 3600)
                mins = int((eta_seconds % 3600) // 60)
                eta_str = f"{hours}h {mins}m"
        else:
            eta_str = "calculating..."
        
        # Print progress
        percent = (self.current / self.total) * 100
        bar_length = 30
        filled = int(bar_length * self.current / self.total)
        bar = '█' * filled + '░' * (bar_length - filled)
        
        print(f"\n{'='*60}")
        print(f"Progress: [{bar}] {percent:.1f}% ({self.current}/{self.total})")
        print(f"Estimated time remaining: {eta_str}")
        print(f"{'='*60}")
    
    def complete(self):
        """Mark progress as complete."""
        elapsed = time.time() - self.start_time
        
        if elapsed < 60:
            time_str = f"{elapsed:.1f}s"
        elif elapsed < 3600:
            mins = int(elapsed // 60)
            secs = int(elapsed % 60)
            time_str = f"{mins}m {secs}s"
        else:
            hours = int(elapsed // 3600)
            mins = int((elapsed % 3600) // 60)
            time_str = f"{hours}h {mins}m"
        
        print(f"\n{'='*60}")
        print(f"✓ All {self.total} chunks processed successfully!")
        print(f"Total time: {time_str}")
        print(f"{'='*60}\n")

