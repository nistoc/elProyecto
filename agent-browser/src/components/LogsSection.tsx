import React from "react";
import type { LogEntry } from "../types";
import { LogPanel } from "./LogPanel";

type Props = {
  title: string;
  logs: LogEntry[];
  emptyLabel: string;
  paused?: boolean;
  bufferedCount?: number;
  onTogglePause?: () => void;
};

export function LogsSection({
  title,
  logs,
  emptyLabel,
  paused,
  bufferedCount,
  onTogglePause,
}: Props) {
  const lastLine = logs.length ? logs[logs.length - 1].message : "";

  return (
    <div className="card log-card">
      <div className="card__header">
        <h3>{title}</h3>
        {onTogglePause && (
          <div className="log-toolbar">
            <button className="ghost" onClick={onTogglePause}>
              {paused ? "Resume" : "Pause"}
            </button>
            {paused && bufferedCount ? (
              <span className="badge">{bufferedCount} queued</span>
            ) : null}
          </div>
        )}
      </div>
      <LogPanel logs={logs} emptyLabel={emptyLabel} autoScroll={!paused} />
      <div className="log-latest">
        <span className="log-latest__label">Latest:</span>
        <span className="log-latest__text">{lastLine || "—"}</span>
      </div>
    </div>
  );
}

