import React from "react";
import { LogEntry } from "../types";

type Props = {
  logs: LogEntry[];
  emptyLabel: string;
};

export function LogPanel({ logs, emptyLabel }: Props) {
  if (!logs.length) {
    return <div className="log-empty">{emptyLabel}</div>;
  }

  return (
    <div className="log-panel">
      {logs.map((log) => (
        <div key={log.ts + log.message} className={`log-row log-${log.level}`}>
          <span className="log-time">
            {new Date(log.ts).toLocaleTimeString([], { hour12: false })}
          </span>
          <span className="log-message">{log.message}</span>
        </div>
      ))}
    </div>
  );
}

