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
  // Agent pause/resume controls (for transcriber agent)
  agentPaused?: "transcriber" | "refiner" | null;
  onPauseAgent?: () => void;
  onResumeAgent?: () => void;
  showAgentControls?: boolean;
};

export function LogsSection({
  title,
  logs,
  emptyLabel,
  paused,
  bufferedCount,
  onTogglePause,
  agentPaused,
  onPauseAgent,
  onResumeAgent,
  showAgentControls = false,
}: Props) {
  const lastLine = logs.length ? logs[logs.length - 1].message : "";

  return (
    <div className="card log-card">
      <div className="card__header">
        <h3>{title}</h3>
        <div className="log-toolbar" style={{ display: "flex", gap: "8px", alignItems: "center" }}>
          {showAgentControls && onPauseAgent && onResumeAgent && (
            <button
              className="btn"
              onClick={agentPaused ? onResumeAgent : onPauseAgent}
              style={{ display: "flex", alignItems: "center", gap: "4px", fontSize: "14px" }}
            >
              {agentPaused ? "▶ Возобновить" : "⏸ Пауза"}
            </button>
          )}
          {onTogglePause && (
            <button className="ghost" onClick={onTogglePause}>
              {paused ? "Возобновить логи" : "Пауза логов"}
            </button>
          )}
          {paused && bufferedCount ? (
            <span className="badge">{bufferedCount} queued</span>
          ) : null}
        </div>
      </div>
      {showAgentControls && agentPaused && (
        <div style={{ padding: "12px", color: "#facc15", fontSize: "14px", borderTop: "1px solid #1f2937" }}>
          ⚠ Агент на паузе: текущие запросы дорабатываются, новые запросы не отправляются
        </div>
      )}
      <LogPanel logs={logs} emptyLabel={emptyLabel} autoScroll={false} />
      <div className="log-latest">
        <span className="log-latest__label">Latest:</span>
        <span className="log-latest__text">{lastLine || "—"}</span>
      </div>
    </div>
  );
}

