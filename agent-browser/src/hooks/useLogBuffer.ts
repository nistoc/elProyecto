import { useCallback, useRef, useState } from "react";
import type { LogEntry, JobSnapshot } from "../types";

/**
 * Hook for managing log pause/resume with buffering.
 */
export function useLogBuffer(
  setJob: React.Dispatch<React.SetStateAction<JobSnapshot | null>>
) {
  const [logsPaused, setLogsPaused] = useState(false);
  const [bufferedLogs, setBufferedLogs] = useState<LogEntry[]>([]);
  const pauseRef = useRef(false);

  const isPaused = useCallback(() => pauseRef.current, []);

  const bufferLog = useCallback((log: LogEntry) => {
    setBufferedLogs((prev) => [...prev, log]);
  }, []);

  const appendLog = useCallback(
    (log: LogEntry) => {
      setJob((prev) =>
        prev ? { ...prev, logs: [...prev.logs, log] } : prev
      );
    },
    [setJob]
  );

  const handleLog = useCallback(
    (log: LogEntry) => {
      if (pauseRef.current) {
        bufferLog(log);
      } else {
        appendLog(log);
      }
    },
    [bufferLog, appendLog]
  );

  const togglePause = useCallback(() => {
    if (logsPaused) {
      // Resume: flush buffered logs
      pauseRef.current = false;
      setLogsPaused(false);
      setJob((prev) =>
        prev ? { ...prev, logs: [...(prev.logs || []), ...bufferedLogs] } : prev
      );
      setBufferedLogs([]);
    } else {
      // Pause
      pauseRef.current = true;
      setLogsPaused(true);
    }
  }, [logsPaused, bufferedLogs, setJob]);

  const reset = useCallback(() => {
    setLogsPaused(false);
    setBufferedLogs([]);
    pauseRef.current = false;
  }, []);

  return {
    logsPaused,
    bufferedCount: bufferedLogs.length,
    isPaused,
    handleLog,
    togglePause,
    reset,
  };
}

