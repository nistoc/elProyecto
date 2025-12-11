import React from "react";

export type StepStatus = "waiting" | "running" | "done" | "failed";

type Props = {
  title: string;
  description?: string;
  status: StepStatus;
  active?: boolean;
  onSelect?: () => void;
  badge?: string;
};

const statusColor: Record<StepStatus, string> = {
  waiting: "#cbd5e1",
  running: "#3b82f6",
  done: "#10b981",
  failed: "#ef4444",
};

export function StepCard({
  title,
  description,
  status,
  active,
  onSelect,
  badge,
}: Props) {
  return (
    <button
      type="button"
      onClick={onSelect}
      className={`step-card ${active ? "active" : ""}`}
    >
      <div className="step-card__header">
        <div
          className="step-card__dot"
          style={{ backgroundColor: statusColor[status] }}
        />
        <div className="step-card__titles">
          <div className="step-card__title">{title}</div>
          {badge && <span className="step-card__badge">{badge}</span>}
        </div>
      </div>
      {description && <div className="step-card__desc">{description}</div>}
    </button>
  );
}

