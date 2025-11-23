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
    
    def __init__(self, total_chunks: int, parallel_workers: int = 1, time_format: str = "MMM:SSS.M"):
        """
        Initialize chunk progress tracker.
        
        Args:
            total_chunks: Total number of chunks to process
            parallel_workers: Number of parallel workers
            time_format: Format for time display (e.g., "MMM:SSS.M")
        """
        self.total = total_chunks
        self.current = 0
        self.parallel_workers = parallel_workers
        self.time_format = time_format
        self.start_time = time.time()
        self._completed_chunks = set()  # Track completed chunks
        self._chunk_start_times = {}  # Track start time for each active chunk
        self._lock = threading.Lock()
    
    def mark_started(self, chunk_idx: int):
        """Mark chunk as started."""
        with self._lock:
            self._chunk_start_times[chunk_idx] = time.time()
    
    def mark_completed(self, chunk_idx: int):
        """Mark chunk as completed."""
        with self._lock:
            self._completed_chunks.add(chunk_idx)
            self._chunk_start_times.pop(chunk_idx, None)
            self.current += 1
    
    def _format_time(self, seconds: float) -> str:
        """
        Format time according to the time_format mask.
        
        Args:
            seconds: Time in seconds
            
        Returns:
            Formatted time string (e.g., "001:045.3" for MMM:SSS.M format)
        """
        minutes = int(seconds // 60)
        remaining_seconds = seconds % 60
        sec_int = int(remaining_seconds)
        sec_decimal = int((remaining_seconds - sec_int) * 10)
        
        # Format according to mask: MMM:SSS.M
        return f"{minutes:03d}:{sec_int:03d}.{sec_decimal}"
    
    def update(self):
        """Update and display current progress in compact format."""
        with self._lock:
            completed = self._completed_chunks.copy()
            active_times = self._chunk_start_times.copy()
        
        # Build compact progress display
        chunks_display = []
        current_time = time.time()
        
        for i in range(self.total):
            if i in completed:
                # Completed chunk
                chunks_display.append(f"[{i+1}:✓]")
            elif i in active_times:
                # Active chunk - show elapsed time
                elapsed = current_time - active_times[i]
                time_str = self._format_time(elapsed)
                chunks_display.append(f"[{i+1}:{time_str}]")
            else:
                # Waiting chunk
                chunks_display.append(f"[{i+1}:---:--.-]")
        
        # Print compact progress line
        progress_line = "".join(chunks_display)
        print(f"\r{progress_line}", end="", flush=True)
    
    def complete(self):
        """Mark progress as complete."""
        # Final update to show all completed
        self.update()
        
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
        
        print(f"\n\n✓ All {self.total} chunks processed successfully! Total time: {time_str}\n")

