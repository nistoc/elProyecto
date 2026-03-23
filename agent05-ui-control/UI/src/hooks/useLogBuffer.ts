import { useState, useCallback } from 'react';
import type { LogEntry } from '../types';

export interface UseLogBufferResult {
  logsPaused: boolean;
  bufferedCount: number;
  handleLog: (log: LogEntry) => void;
  togglePause: () => void;
  reset: () => void;
  flushTo: (setLogs: (logs: LogEntry[]) => void) => void;
}

export function useLogBuffer(): UseLogBufferResult {
  const [logsPaused, setLogsPaused] = useState(false);
  const [buffer, setBuffer] = useState<LogEntry[]>([]);

  const handleLog = useCallback((log: LogEntry) => {
    setBuffer((prev) =>
      prev.length >= 500 ? [...prev.slice(-499), log] : [...prev, log]
    );
  }, []);

  const togglePause = useCallback(() => {
    setLogsPaused((p) => !p);
  }, []);

  const reset = useCallback(() => {
    setBuffer([]);
  }, []);

  const flushTo = useCallback((setLogs: (logs: LogEntry[]) => void) => {
    setBuffer((prev) => {
      setLogs(prev);
      return [];
    });
  }, []);

  return {
    logsPaused,
    bufferedCount: buffer.length,
    handleLog,
    togglePause,
    reset,
    flushTo,
  };
}
