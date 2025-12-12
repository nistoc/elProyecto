#!/usr/bin/env python3
"""
File-based cancellation signals for chunk processing.

The Node server writes small flag files into a shared directory
to request cancellation for specific chunk indices. The pipeline
periodically polls this directory to honor user-initiated cancels.
"""
import os
import re
import threading
from typing import Set, Iterable


class CancellationManager:
    """Tracks chunk cancellation requests communicated via flag files."""

    def __init__(self, cancel_dir: str):
        self.cancel_dir = cancel_dir or "cancel_signals"
        os.makedirs(self.cancel_dir, exist_ok=True)
        self._seen: Set[int] = set()
        self._lock = threading.Lock()
        self._pattern = re.compile(r"cancel_(\d+)\.flag$")

    def _scan_dir(self) -> Set[int]:
        """Scan cancellation directory for new requests."""
        found = set()
        for name in os.listdir(self.cancel_dir):
            match = self._pattern.match(name)
            if not match:
                continue
            idx = int(match.group(1))
            found.add(idx)
        return found

    def poll(self) -> Set[int]:
        """
        Return newly observed cancellation requests.

        Returns:
            Set of chunk indices requested for cancellation.
        """
        with self._lock:
            discovered = self._scan_dir()
            new_requests = discovered - self._seen
            self._seen.update(new_requests)
            return new_requests

    def is_cancelled(self, idx: int) -> bool:
        """Check whether a chunk index has been cancelled."""
        with self._lock:
            if idx in self._seen:
                return True
        # check filesystem lazily
        return idx in self.poll()

    def mark_cancelled(self, idx: int):
        """Persist a cancellation flag and mark it as seen."""
        path = self._flag_path(idx)
        os.makedirs(self.cancel_dir, exist_ok=True)
        try:
            with open(path, "w", encoding="utf-8") as f:
                f.write("cancelled")
        except OSError:
            # Best-effort; ignore filesystem errors here.
            pass
        with self._lock:
            self._seen.add(idx)

    def _flag_path(self, idx: int) -> str:
        return os.path.join(self.cancel_dir, f"cancel_{idx}.flag")

    def merge_external(self, indices: Iterable[int]):
        """Allow callers to seed known cancellations from other sources."""
        with self._lock:
            self._seen.update(indices)

