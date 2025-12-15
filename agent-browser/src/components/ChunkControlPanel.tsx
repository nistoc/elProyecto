import React from "react";
import type { ChunkState } from "../types";

type Props = {
  state?: ChunkState;
  onCancel: (idx: number) => void;
  disabled?: boolean;
};

export function ChunkControlPanel({ state, onCancel, disabled }: Props) {
  if (!state) return null;

  const active = state.active || [];
  const cancelled = state.cancelled || [];
  const completed = state.completed || [];
  const failed = state.failed || [];

  return (
    <div className="card chunk-card">
      <div className="card__header">
        <h3>Chunks</h3>
        <span className="muted">
          total: {state.total ?? "—"} • done: {completed.length} • cancelled:{" "}
          {cancelled.length}
        </span>
      </div>

      <div className="chunk-section">
        <div className="chunk-section__title">Active</div>
        <div className="chunk-chips">
          {active.length === 0 && (
            <span className="muted">No active chunks</span>
          )}
          {active.map((idx) => (
            <div key={idx} className="chip chip--action">
              <span>Chunk #{idx + 1}</span>
              <button
                className="ghost"
                onClick={() => onCancel(idx)}
                disabled={disabled}
                title="Cancel this chunk"
              >
                cancel
              </button>
            </div>
          ))}
        </div>
      </div>

      <div className="chunk-section">
        <div className="chunk-section__title">Cancelled</div>
        <div className="chunk-chips">
          {cancelled.length === 0 && (
            <span className="muted">None cancelled</span>
          )}
          {cancelled.map((idx) => (
            <span key={idx} className="chip chip--muted">
              #{idx + 1}
            </span>
          ))}
        </div>
      </div>

      <div className="chunk-section">
        <div className="chunk-section__title">Completed</div>
        <div className="chunk-chips">
          {completed.length === 0 && (
            <span className="muted">Not completed yet</span>
          )}
          {completed.map((idx) => (
            <span key={idx} className="chip chip--ok">
              #{idx + 1}
            </span>
          ))}
        </div>
      </div>

      {failed.length > 0 && (
        <div className="chunk-section">
          <div className="chunk-section__title">Failed</div>
          <div className="chunk-chips">
            {failed.map((idx) => (
              <span key={idx} className="chip chip--warn">
                #{idx + 1}
              </span>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

