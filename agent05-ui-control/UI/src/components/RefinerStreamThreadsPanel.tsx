import { useEffect, useMemo, useRef } from 'react';
import type { RefinerThreadBatchSnapshot } from '../types';

export interface RefinerStreamThreadsPanelProps {
  /** From job snapshot (Agent06 gRPC fields merged on BFF). */
  batches: RefinerThreadBatchSnapshot[] | null | undefined;
  snapshotRevision: number;
  t: (key: string) => string;
}

function sortBatches(rows: RefinerThreadBatchSnapshot[]): RefinerThreadBatchSnapshot[] {
  return [...rows].sort((a, b) => a.batchIndex - b.batchIndex);
}

/**
 * Batch before/after columns driven only by snapshot fields updated from the refiner gRPC stream.
 */
export function RefinerStreamThreadsPanel({
  batches,
  snapshotRevision,
  t,
}: RefinerStreamThreadsPanelProps) {
  const scrollRef = useRef<HTMLDivElement>(null);

  const rows = useMemo(
    () => sortBatches(batches ?? []),
    [batches]
  );

  useEffect(() => {
    const el = scrollRef.current;
    if (!el || rows.length === 0) return;
    el.scrollTop = el.scrollHeight;
  }, [snapshotRevision, rows.length]);

  return (
    <div className="refiner-stream-threads">
      <h3 className="refiner-stream-threads__title">{t('refinerThreadsTitle')}</h3>
      <p className="refiner-stream-threads__source-hint">{t('refinerStreamThreadsHint')}</p>

      {rows.length === 0 ? (
        <p className="refiner-stream-threads__empty">{t('refinerThreadsEmpty')}</p>
      ) : (
        <div className="refiner-stream-threads__scroll" ref={scrollRef}>
          <table className="refiner-stream-threads__table">
            <thead>
              <tr>
                <th className="refiner-stream-threads__th refiner-stream-threads__th--idx">
                  {t('refinerThreadsBatch')}
                </th>
                <th className="refiner-stream-threads__th">{t('refinerThreadsBefore')}</th>
                <th className="refiner-stream-threads__th">{t('refinerThreadsAfter')}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((b, rowIdx) => {
                const label = `${b.batchIndex + 1} / ${b.totalBatches}`;
                return (
                  <tr key={`${b.batchIndex}-${b.totalBatches}-${rowIdx}`}>
                    <td className="refiner-stream-threads__td refiner-stream-threads__td--idx">
                      {label}
                    </td>
                    <td className="refiner-stream-threads__td refiner-stream-threads__td--mono">
                      <pre className="refiner-stream-threads__cell">{b.beforeText ?? ''}</pre>
                    </td>
                    <td className="refiner-stream-threads__td refiner-stream-threads__td--mono">
                      <pre className="refiner-stream-threads__cell">
                        {b.afterText == null
                          ? t('refinerThreadsAfterPending')
                          : b.afterText}
                      </pre>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      <style>{`
        .refiner-stream-threads {
          margin: 0;
          padding: 0;
          color: var(--color-text);
        }
        .refiner-stream-threads__title {
          margin: 0 0 0.35rem 0;
          font-size: 1rem;
          color: var(--color-heading);
        }
        .refiner-stream-threads__source-hint {
          margin: 0 0 0.75rem 0;
          font-size: 0.72rem;
          color: var(--color-text-secondary);
          line-height: 1.35;
        }
        .refiner-stream-threads__empty {
          margin: 0;
          font-size: 0.875rem;
          color: var(--color-text-secondary);
        }
        .refiner-stream-threads__scroll {
          max-height: min(75vh);
          overflow: auto;
          border: 1px solid var(--color-border);
          border-radius: 6px;
          background: var(--color-surface);
        }
        .refiner-stream-threads__table {
          width: 100%;
          border-collapse: collapse;
          font-size: 0.78rem;
        }
        .refiner-stream-threads__th {
          position: sticky;
          top: 0;
          z-index: 1;
          padding: 0.4rem 0.5rem;
          text-align: left;
          font-weight: 600;
          text-transform: uppercase;
          letter-spacing: 0.04em;
          font-size: 0.7rem;
          color: var(--color-text-secondary);
          background: var(--color-surface-sunken);
          border-bottom: 1px solid var(--color-border);
        }
        .refiner-stream-threads__th--idx { width: 5rem; white-space: nowrap; }
        .refiner-stream-threads__td {
          vertical-align: top;
          padding: 0.35rem 0.5rem;
          border-bottom: 1px solid var(--color-border);
        }
        .refiner-stream-threads__td--idx {
          font-weight: 600;
          color: var(--color-text-secondary);
          white-space: nowrap;
        }
        .refiner-stream-threads__td--mono { max-width: 0; width: 50%; }
        .refiner-stream-threads__cell {
          margin: 0;
          overflow: auto;
          font-family: ui-monospace, 'Cascadia Code', 'Consolas', monospace;
          line-height: 1.45;
          white-space: pre-wrap;
          word-break: break-word;
        }
        @media (max-width: 720px) {
          .refiner-stream-threads__th--idx, .refiner-stream-threads__td--idx { display: none; }
        }
      `}</style>
    </div>
  );
}
