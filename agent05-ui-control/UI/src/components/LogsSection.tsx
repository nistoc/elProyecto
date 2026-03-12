import type { LogEntry } from '../types';

interface LogsSectionProps {
  title: string;
  logs: LogEntry[];
  emptyLabel?: string;
  paused?: boolean;
  bufferedCount?: number;
  onTogglePause?: () => void;
  onClearLogs?: () => void;
}

export function LogsSection({
  title,
  logs,
  emptyLabel = 'No logs',
  paused = false,
  bufferedCount = 0,
  onTogglePause,
  onClearLogs,
}: LogsSectionProps) {
  return (
    <div className="logs-section">
      <div className="logs-section__header">
        <h3 className="logs-section__title">{title}</h3>
        <div className="logs-section__actions">
          {onTogglePause && (
            <button type="button" onClick={onTogglePause}>
              {paused ? 'Resume' : 'Pause'}
              {bufferedCount > 0 && ` (+${bufferedCount})`}
            </button>
          )}
          {onClearLogs && (
            <button type="button" onClick={onClearLogs}>
              Clear
            </button>
          )}
        </div>
      </div>
      <div className="logs-section__panel">
        {logs.length === 0 ? (
          <p className="logs-section__empty">{emptyLabel}</p>
        ) : (
          logs.map((e, i) => (
            <div key={i} className="logs-section__line" data-level={e.level}>
              <span className="logs-section__ts">
                {new Date(e.ts).toISOString()}
              </span>
              <span className="logs-section__msg">{e.message}</span>
            </div>
          ))
        )}
      </div>
      <style>{`
        .logs-section { margin-top: 1rem; }
        .logs-section__header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.5rem; }
        .logs-section__title { margin: 0; font-size: 0.875rem; }
        .logs-section__actions { display: flex; gap: 0.5rem; }
        .logs-section__actions button { font-size: 0.75rem; padding: 0.25rem 0.5rem; }
        .logs-section__panel {
          max-height: 200px;
          overflow-y: auto;
          border: 1px solid #e2e8f0;
          border-radius: 4px;
          padding: 0.5rem;
          font-family: monospace;
          font-size: 0.75rem;
          background: #f8fafc;
        }
        .logs-section__line { margin-bottom: 0.25rem; word-break: break-all; }
        .logs-section__line[data-level="error"] { color: #dc2626; }
        .logs-section__ts { color: #64748b; margin-right: 0.5rem; }
        .logs-section__empty { color: #94a3b8; margin: 0; }
      `}</style>
    </div>
  );
}
