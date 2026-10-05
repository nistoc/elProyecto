import type { RefineProgressState } from '../types';
import type { useI18n } from '../contexts/I18nContext';

type Translate = ReturnType<typeof useI18n>['t'];

interface Props {
  progress: RefineProgressState | null;
  t: Translate;
}

/**
 * Live refiner progress: the prompt in use, batch progress, and a before/after
 * table showing how each fragment was improved. Fed by SSE 'refine_progress'
 * events accumulated in useJob.
 */
export function RefinerProgress({ progress, t }: Props) {
  if (!progress || (progress.batches.length === 0 && !progress.promptPreview)) {
    return null;
  }

  const { promptPreview, currentBatch, totalBatches, batches, fromDisk } = progress;
  // Live run: the batch currently at OpenAI has no result yet — show it as an in-flight row.
  const lastDone = batches.length > 0 ? batches[batches.length - 1].index : 0;
  const inFlight =
    !fromDisk && totalBatches > 0 && lastDone < totalBatches ? lastDone + 1 : null;
  const changedCount = batches.filter((b) => b.original !== b.refined).length;
  const pct =
    totalBatches > 0
      ? Math.min(100, Math.round((currentBatch / totalBatches) * 100))
      : progress.progressPercent;

  return (
    <div className="refine-progress">
      <div className="refine-progress__head">
        <span className="refine-progress__title">
          {t('refineProgressTitle')}
          {fromDisk && (
            <span className="refine-progress__saved"> · {t('refineProgressSaved')}</span>
          )}
        </span>
        {totalBatches > 0 && (
          <span className="refine-progress__counter">
            {t('refineProgressBatch')} {currentBatch}/{totalBatches}
            {changedCount > 0 && (
              <span className="refine-progress__changed">
                {' '}
                · {changedCount} {t('refineProgressChanged')}
              </span>
            )}
          </span>
        )}
      </div>

      {totalBatches > 0 && (
        <div className="refine-progress__bar">
          <div className="refine-progress__bar-fill" style={{ width: `${pct}%` }} />
        </div>
      )}

      {promptPreview && (
        <details className="refine-progress__prompt">
          <summary>{t('refineProgressPrompt')}</summary>
          <pre>{promptPreview}</pre>
        </details>
      )}

      {batches.length > 0 && (
        <div className="refine-progress__table-wrap">
          <table className="refine-progress__table">
            <thead>
              <tr>
                <th className="refine-progress__col-idx">{t('refineProgressOrder')}</th>
                <th>{t('refineProgressSent')}</th>
                <th>{t('refineProgressReceived')}</th>
              </tr>
            </thead>
            <tbody>
              {batches.map((b) => {
                const changed = b.original !== b.refined;
                return (
                  <tr
                    key={b.index}
                    className={
                      !b.ok
                        ? 'refine-progress__row--failed'
                        : changed
                          ? 'refine-progress__row--changed'
                          : undefined
                    }
                  >
                    <td className="refine-progress__col-idx">
                      <div className="refine-progress__n">{b.index}</div>
                      {b.startLine != null && b.startLine > 0 && (
                        <div className="refine-progress__lines">
                          {b.startLine}–{b.endLine}
                        </div>
                      )}
                      {!b.ok && (
                        <span
                          className="refine-progress__flag"
                          title={b.error ?? t('refineProgressFailed')}
                        >
                          {' '}
                          ⚠
                        </span>
                      )}
                    </td>
                    <td>
                      {b.context && (
                        <div className="refine-progress__context">
                          <div className="refine-progress__context-label">
                            ↳ {t('refineProgressContext')} #{b.index - 1}
                          </div>
                          <pre>{b.context}</pre>
                        </div>
                      )}
                      <pre>{b.original}</pre>
                    </td>
                    <td>
                      <pre>{b.refined}</pre>
                    </td>
                  </tr>
                );
              })}
              {inFlight != null && (
                <tr className="refine-progress__row--inflight">
                  <td className="refine-progress__col-idx">
                    <div className="refine-progress__n">{inFlight}</div>
                  </td>
                  <td colSpan={2}>{t('refineProgressInFlight')}</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      <style>{`
        .refine-progress {
          margin: 0 0 1rem 0;
          padding: 0.85rem 1rem;
          border: 1px solid var(--color-border);
          border-radius: 8px;
          background: var(--color-subtle-panel, var(--color-surface));
        }
        .refine-progress__head {
          display: flex;
          align-items: baseline;
          justify-content: space-between;
          gap: 0.75rem;
          margin-bottom: 0.55rem;
          flex-wrap: wrap;
        }
        .refine-progress__title { font-size: 0.9rem; font-weight: 500; color: var(--color-heading); }
        .refine-progress__counter { font-size: 0.8rem; color: var(--color-text-secondary); }
        .refine-progress__changed { color: var(--color-text-muted); }
        .refine-progress__bar {
          height: 6px;
          border-radius: 999px;
          background: var(--color-border);
          overflow: hidden;
          margin-bottom: 0.7rem;
        }
        .refine-progress__bar-fill {
          height: 100%;
          background: var(--color-accent, #2d7ff9);
          transition: width 0.3s ease;
        }
        .refine-progress__prompt {
          margin-bottom: 0.7rem;
          font-size: 0.8rem;
        }
        .refine-progress__prompt summary {
          cursor: pointer;
          color: var(--color-text-secondary);
        }
        .refine-progress__prompt pre {
          margin: 0.4rem 0 0 0;
          padding: 0.5rem 0.65rem;
          border-radius: 6px;
          background: var(--color-surface);
          border: 1px solid var(--color-border);
          font-family: var(--font-mono, monospace);
          font-size: 0.72rem;
          line-height: 1.45;
          white-space: pre-wrap;
          max-height: 220px;
          overflow: auto;
          color: var(--color-text);
        }
        .refine-progress__table-wrap {
          max-height: 460px;
          overflow: auto;
          border: 1px solid var(--color-border);
          border-radius: 6px;
        }
        .refine-progress__table {
          width: 100%;
          border-collapse: collapse;
          table-layout: fixed;
          font-size: 0.76rem;
        }
        .refine-progress__table th {
          position: sticky;
          top: 0;
          background: var(--color-surface);
          text-align: left;
          padding: 0.4rem 0.55rem;
          font-weight: 500;
          color: var(--color-text-secondary);
          border-bottom: 1px solid var(--color-border);
          z-index: 1;
        }
        .refine-progress__col-idx { width: 4.5rem; text-align: right; color: var(--color-text-muted); }
        .refine-progress__n { font-weight: 500; color: var(--color-text-secondary); }
        .refine-progress__lines { font-size: 0.68rem; white-space: nowrap; }
        .refine-progress__saved { font-weight: 400; font-size: 0.78rem; color: var(--color-text-muted); }
        .refine-progress__context {
          margin-bottom: 0.35rem;
          padding: 0.25rem 0.4rem;
          border-left: 2px dashed var(--color-border);
          opacity: 0.6;
        }
        .refine-progress__context-label { font-size: 0.66rem; color: var(--color-text-muted); margin-bottom: 0.15rem; }
        .refine-progress__row--inflight td { color: var(--color-text-muted); font-style: italic; }
        .refine-progress__table td {
          padding: 0.35rem 0.55rem;
          vertical-align: top;
          border-bottom: 1px solid var(--color-border);
        }
        .refine-progress__table pre {
          margin: 0;
          font-family: var(--font-mono, monospace);
          font-size: 0.72rem;
          line-height: 1.45;
          white-space: pre-wrap;
          word-break: break-word;
          color: var(--color-text);
        }
        .refine-progress__row--changed td:nth-child(3) pre { color: var(--color-accent, #2d7ff9); }
        .refine-progress__row--failed { background: var(--color-danger-bg, rgba(220, 60, 60, 0.08)); }
        .refine-progress__flag { color: var(--color-warning, #d08700); }
      `}</style>
    </div>
  );
}
