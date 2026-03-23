import { useEffect, useMemo, useRef } from 'react';
import type { LogEntry } from '../types';

export interface RefinerStreamLogPanelProps {
  /** Job log lines from SSE snapshot; refiner lines are filtered client-side. */
  logs: LogEntry[] | null | undefined;
  /** Increments on each SSE snapshot so the view scrolls to newest lines. */
  snapshotRevision: number;
  t: (key: string) => string;
}

function formatRefinerLine(e: LogEntry): string {
  const ts =
    typeof e.ts === 'number' && e.ts > 0
      ? new Date(e.ts).toISOString()
      : '';
  const lvl = (e.level ?? 'info').trim();
  const msg = e.message ?? '';
  return ts ? `[${ts}] [${lvl}] ${msg}` : `[${lvl}] ${msg}`;
}

/**
 * Live refiner activity from the API job snapshot (Agent06 gRPC mirrored by BFF into logs).
 */
export function RefinerStreamLogPanel({
  logs,
  snapshotRevision,
  t,
}: RefinerStreamLogPanelProps) {
  const preRef = useRef<HTMLPreElement>(null);

  const lines = useMemo(() => {
    const list = logs ?? [];
    return list.filter((e) => (e.message ?? '').startsWith('Refiner:'));
  }, [logs]);

  useEffect(() => {
    const el = preRef.current;
    if (!el) return;
    el.scrollTop = el.scrollHeight;
  }, [snapshotRevision, lines.length]);

  return (
    <section className="refiner-stream-log" aria-label={t('refinerStreamLogTitle')}>
      <h3 className="refiner-stream-log__title">{t('refinerStreamLogTitle')}</h3>
      <p className="refiner-stream-log__hint">{t('refinerStreamLogHint')}</p>
      <pre ref={preRef} className="refiner-stream-log__pre">
        {lines.length === 0 ? (
          <span className="refiner-stream-log__empty">{t('refinerStreamLogEmpty')}</span>
        ) : (
          lines.map((e, i) => (
            <span key={`refiner-log-${i}-${e.ts}`} className="refiner-stream-log__line">
              {formatRefinerLine(e)}
              {'\n'}
            </span>
          ))
        )}
      </pre>
      <style>{`
        .refiner-stream-log { min-height: 0; display: flex; flex-direction: column; }
        .refiner-stream-log__title {
          margin: 0 0 0.35rem 0;
          font-size: 0.875rem;
          color: var(--color-text);
        }
        .refiner-stream-log__hint {
          margin: 0 0 0.5rem 0;
          font-size: 0.75rem;
          color: var(--color-text-muted);
        }
        .refiner-stream-log__pre {
          flex: 1;
          min-height: 2rem;
          max-height: min(8vh);
          margin: 0;
          overflow: auto;
          padding: 0.5rem;
          border: 1px solid var(--color-border-strong);
          border-radius: 6px;
          font-family: ui-monospace, 'Cascadia Code', 'Consolas', monospace;
          font-size: 0.72rem;
          line-height: 1.45;
          white-space: pre-wrap;
          word-break: break-word;
          background: var(--color-surface);
          color: var(--color-text);
        }
        .refiner-stream-log__empty { color: var(--color-text-muted); }
        .refiner-stream-log__line { display: block; }
      `}</style>
    </section>
  );
}
