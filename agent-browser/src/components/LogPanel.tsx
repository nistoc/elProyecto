import React, { useEffect, useRef } from "react";
import type { LogEntry } from "../types";

type Props = {
  logs: LogEntry[];
  emptyLabel: string;
  autoScroll?: boolean;
};

export function LogPanel({ logs, emptyLabel, autoScroll = true }: Props) {
  const endRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    // Scroll to the latest log when list changes.
    if (autoScroll) {
      endRef.current?.scrollIntoView({ behavior: "smooth", block: "end" });
    }
  }, [logs, autoScroll]);

  if (!logs.length) {
    return <div className="log-empty">{emptyLabel}</div>;
  }

  return (
    <div className="log-panel">
      {logs.map((log, idx) => (
        <div key={`${log.ts}-${idx}`} className={`log-row log-${log.level}`}>
          <span className="log-time">
            {new Date(log.ts).toLocaleTimeString([], { hour12: false })}
          </span>
          <span className="log-message">{log.message}</span>
        </div>
      ))}
      <div ref={endRef} />
    </div>
  );
}

