import { useEffect, useState } from 'react';
import { postJobChunkAction, type ChunkActionName } from '../api';
import type { JobSnapshot } from '../types';

export interface ChunkControlPanelProps {
  jobId: string;
  job: JobSnapshot;
  t: (key: string) => string;
  /** When set, project file lists can narrow to this chunk index. */
  fileFilterChunkIndex: number | null;
  onFileFilterChunkChange: (index: number | null) => void;
}

export function ChunkControlPanel({
  jobId,
  job,
  t,
  fileFilterChunkIndex,
  onFileFilterChunkChange,
}: ChunkControlPanelProps) {
  const total = job.chunks?.total ?? 0;
  const [chunkIndex, setChunkIndex] = useState(0);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const canOperate =
    job.phase === 'transcriber' &&
    job.status === 'running' &&
    total > 0 &&
    chunkIndex >= 0 &&
    chunkIndex < total;

  useEffect(() => {
    if (total <= 0) return;
    setChunkIndex((i) => Math.min(Math.max(0, i), total - 1));
  }, [total]);

  useEffect(() => {
    if (job.phase !== 'transcriber' || job.status !== 'running') {
      onFileFilterChunkChange(null);
    }
  }, [job.phase, job.status, onFileFilterChunkChange]);

  useEffect(() => {
    if (fileFilterChunkIndex === null) return;
    onFileFilterChunkChange(chunkIndex);
  }, [chunkIndex, fileFilterChunkIndex, onFileFilterChunkChange]);

  const runAction = async (action: ChunkActionName) => {
    setMessage(null);
    setBusy(true);
    try {
      const res = await postJobChunkAction(jobId, action, chunkIndex);
      setMessage(
        res.ok
          ? res.message || t('chunkActionOk')
          : `${t('chunkActionRejected')}: ${res.message}`
      );
    } catch (e) {
      setMessage(e instanceof Error ? e.message : t('chunkActionFailed'));
    } finally {
      setBusy(false);
    }
  };

  if (job.phase !== 'transcriber' || job.status !== 'running') {
    return null;
  }

  return (
    <section className="chunk-panel" aria-label={t('chunkOperatorTitle')}>
      <h4 className="chunk-panel__title">{t('chunkOperatorTitle')}</h4>
      <p className="chunk-panel__hint">{t('chunkOperatorHint')}</p>
      <div className="chunk-panel__row">
        <label className="chunk-panel__label">
          {t('chunkIndexLabel')}
          <input
            type="number"
            min={0}
            max={total > 0 ? total - 1 : undefined}
            value={chunkIndex}
            disabled={total <= 0}
            onChange={(e) => {
              const v = parseInt(e.target.value, 10);
              if (Number.isNaN(v)) return;
              setChunkIndex(v);
            }}
            className="chunk-panel__input"
          />
          {total > 0 ? (
            <span className="chunk-panel__total">
              / {total} ({t('chunkZeroBased')})
            </span>
          ) : (
            <span className="chunk-panel__total chunk-panel__total--muted">
              ({t('chunkTotalUnknown')})
            </span>
          )}
        </label>
        <label className="chunk-panel__filter">
          <input
            type="checkbox"
            checked={fileFilterChunkIndex !== null}
            onChange={(e) => {
              if (e.target.checked) onFileFilterChunkChange(chunkIndex);
              else onFileFilterChunkChange(null);
            }}
            disabled={total <= 0}
          />
          {t('chunkFilterProjectFiles')}
        </label>
      </div>
      {fileFilterChunkIndex !== null && (
        <p className="chunk-panel__filter-note">
          {t('chunkFilterActive')}: #{fileFilterChunkIndex}
        </p>
      )}
      <div className="chunk-panel__actions">
        <button
          type="button"
          disabled={!canOperate || busy}
          onClick={() => runAction('cancel')}
        >
          {t('chunkCancelChunk')}
        </button>
        <button
          type="button"
          disabled={!canOperate || busy}
          onClick={() => runAction('skip')}
        >
          {t('chunkSkip')}
        </button>
        <button
          type="button"
          disabled={!canOperate || busy}
          onClick={() => runAction('retranscribe')}
        >
          {t('chunkRetranscribe')}
        </button>
        <button
          type="button"
          disabled={!canOperate || busy}
          onClick={() => runAction('split')}
        >
          {t('chunkSplit')}
        </button>
      </div>
      {message && <p className="chunk-panel__message">{message}</p>}
      <style>{`
        .chunk-panel {
          margin: 1rem 0;
          padding: 0.75rem 1rem;
          border: 1px solid var(--color-border);
          border-radius: 8px;
          background: var(--color-subtle-panel);
          color: var(--color-text);
        }
        .chunk-panel__title {
          margin: 0 0 0.35rem 0;
          font-size: 0.9rem;
          color: var(--color-heading);
        }
        .chunk-panel__hint {
          margin: 0 0 0.75rem 0;
          font-size: 0.8125rem;
          color: var(--color-text-secondary);
        }
        .chunk-panel__row {
          display: flex;
          flex-wrap: wrap;
          align-items: center;
          gap: 1rem 1.5rem;
          margin-bottom: 0.75rem;
        }
        .chunk-panel__label {
          display: flex;
          flex-wrap: wrap;
          align-items: center;
          gap: 0.5rem;
          font-size: 0.8125rem;
          color: var(--color-label);
        }
        .chunk-panel__input {
          width: 4.5rem;
          padding: 0.25rem 0.5rem;
          border: 1px solid var(--color-border-strong);
          border-radius: 4px;
          background: var(--color-surface);
          color: var(--color-text);
        }
        .chunk-panel__total { font-size: 0.75rem; color: var(--color-text-secondary); }
        .chunk-panel__total--muted { font-style: italic; }
        .chunk-panel__filter {
          display: flex;
          align-items: center;
          gap: 0.35rem;
          font-size: 0.8125rem;
          color: var(--color-label);
          cursor: pointer;
        }
        .chunk-panel__filter-note {
          margin: 0 0 0.5rem 0;
          font-size: 0.75rem;
          color: var(--color-info);
        }
        .chunk-panel__actions {
          display: flex;
          flex-wrap: wrap;
          gap: 0.5rem;
        }
        .chunk-panel__actions button {
          padding: 0.35rem 0.65rem;
          font-size: 0.8125rem;
          border: 1px solid var(--color-border-strong);
          border-radius: 4px;
          background: var(--color-surface);
          cursor: pointer;
          color: var(--color-heading);
        }
        .chunk-panel__actions button:hover:not(:disabled) {
          background: var(--color-surface-hover);
        }
        .chunk-panel__actions button:disabled {
          opacity: 0.5;
          cursor: not-allowed;
        }
        .chunk-panel__message {
          margin: 0.75rem 0 0 0;
          font-size: 0.8125rem;
          color: var(--color-label);
        }
      `}</style>
    </section>
  );
}
