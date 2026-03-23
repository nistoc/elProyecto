import type { StepStatus } from '../types';

interface StepCardProps {
  title: string;
  description?: string;
  status: StepStatus;
  active: boolean;
  onSelect: () => void;
  badge?: string;
}

const statusColors: Record<StepStatus, string> = {
  waiting: '#94a3b8',
  running: '#3b82f6',
  done: '#22c55e',
  failed: '#ef4444',
};

export function StepCard({
  title,
  description,
  status,
  active,
  onSelect,
  badge,
}: StepCardProps) {
  const color = statusColors[status] ?? statusColors.waiting;
  return (
    <button
      type="button"
      onClick={onSelect}
      className={`step-card ${active ? 'active' : ''}`}
      style={{ borderLeftColor: active ? color : undefined }}
    >
      <span className="step-card__dot" style={{ backgroundColor: color }} />
      <div className="step-card__body">
        <span className="step-card__title">{title}</span>
        {description && <span className="step-card__desc">{description}</span>}
        {badge != null && badge !== '' && (
          <span className="step-card__badge">{badge}</span>
        )}
      </div>
      <style>{`
        .step-card {
          display: flex;
          align-items: center;
          gap: 0.5rem;
          width: 100%;
          padding: 0.75rem 1rem;
          border: 1px solid var(--color-border);
          border-left: 3px solid transparent;
          border-radius: 6px;
          background: var(--color-surface);
          color: var(--color-text);
          cursor: pointer;
          text-align: left;
        }
        .step-card:hover { background: var(--color-surface-hover); }
        .step-card.active { background: var(--color-surface-active); }
        .step-card__dot {
          width: 10px;
          height: 10px;
          border-radius: 50%;
          flex-shrink: 0;
        }
        .step-card__body { flex: 1; min-width: 0; }
        .step-card__title { font-weight: 600; display: block; }
        .step-card__desc { font-size: 0.875rem; color: var(--color-text-secondary); display: block; }
        .step-card__badge { font-size: 0.75rem; color: var(--color-text-secondary); margin-left: 0.25rem; }
      `}</style>
    </button>
  );
}
