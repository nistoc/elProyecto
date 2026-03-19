import { useEffect, useState } from 'react';
import { postJobChunkAction, type ChunkActionName } from '../api';
import type { ChunkVirtualModelEntry, JobSnapshot } from '../types';

function formatMmSs(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(totalSeconds));
  const m = Math.floor(s / 60);
  const sec = s % 60;
  return `${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}`;
}

function elapsedSeconds(
  row: ChunkVirtualModelEntry,
  nowMs: number
): number | null {
  if (!row.startedAt) return null;
  const start = Date.parse(row.startedAt);
  if (Number.isNaN(start)) return null;
  const end = row.completedAt ? Date.parse(row.completedAt) : nowMs;
  if (Number.isNaN(end)) return null;
  return (end - start) / 1000;
}

function labelForChunkState(state: string, t: (key: string) => string): string {
  const key = `chunkState${state}`;
  const v = t(key);
  return v === key ? state : v;
}

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
  const vm = job.chunks?.chunkVirtualModel;
  const [chunkIndex, setChunkIndex] = useState(0);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [nowTick, setNowTick] = useState(() => Date.now());

  const canOperate =
    job.phase === 'transcriber' &&
    job.status === 'running' &&
    total > 0 &&
    chunkIndex >= 0 &&
    chunkIndex < total;

  const hasRunningVm = vm?.some((r) => r.state === 'Running') ?? false;

  useEffect(() => {
    if (!hasRunningVm) return;
    const id = window.setInterval(() => setNowTick(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, [hasRunningVm]);

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

  const runAction = async (action: ChunkActionName, index?: number) => {
    const idx = index ?? chunkIndex;
    setMessage(null);
    setBusy(true);
    try {
      const res = await postJobChunkAction(jobId, action, idx);
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

  const showVmTable = vm && vm.length > 0;
  const showVmPlaceholder = !showVmTable && total > 0;

  return (
    <section className="chunk-panel" aria-label={t('chunkOperatorTitle')}>
      <h4 className="chunk-panel__title">{t('chunkOperatorTitle')}</h4>
      <p className="chunk-panel__hint">{t('chunkOperatorHint')}</p>

      {showVmTable && (
        <div className="chunk-vm">
          <h5 className="chunk-vm__title">{t('chunkVmListTitle')}</h5>
          <p className="chunk-vm__note">{t('chunkVmTimerNote')}</p>
          <div className="chunk-vm__table-wrap">
            <table className="chunk-vm__table">
              <thead>
                <tr>
                  <th scope="col">{t('chunkVmColChunk')}</th>
                  <th scope="col">{t('chunkVmColElapsed')}</th>
                  <th scope="col">{t('chunkVmColState')}</th>
                  <th scope="col" className="chunk-vm__th-cancel">
                    <span className="visually-hidden">{t('chunkCancelChunk')}</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {vm!.map((row) => {
                  const sec = elapsedSeconds(row, nowTick);
                  const elapsed =
                    sec === null ? t('chunkVmNoStarted') : formatMmSs(sec);
                  return (
                    <tr key={row.index}>
                      <td className="chunk-vm__num">#{row.index}</td>
                      <td className="chunk-vm__time" title={row.startedAt ?? ''}>
                        {elapsed}
                      </td>
                      <td>{labelForChunkState(row.state, t)}</td>
                      <td className="chunk-vm__cancel">
                        {row.state === 'Running' ? (
                          <button
                            type="button"
                            className="chunk-vm__x"
                            disabled={!canOperate || busy}
                            aria-label={t('chunkVmCancelAria').replace(
                              '{n}',
                              String(row.index)
                            )}
                            onClick={() => runAction('cancel', row.index)}
                          >
                            ×
                          </button>
                        ) : (
                          <span className="chunk-vm__x-placeholder" />
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {showVmPlaceholder && (
        <p className="chunk-vm__waiting">{t('chunkVmWaitingVm')}</p>
      )}

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
        .visually-hidden {
          position: absolute;
          width: 1px;
          height: 1px;
          padding: 0;
          margin: -1px;
          overflow: hidden;
          clip: rect(0, 0, 0, 0);
          white-space: nowrap;
          border: 0;
        }
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
        .chunk-vm {
          margin-bottom: 1rem;
          padding-bottom: 0.75rem;
          border-bottom: 1px solid var(--color-border);
        }
        .chunk-vm__title {
          margin: 0 0 0.25rem 0;
          font-size: 0.85rem;
          color: var(--color-heading);
        }
        .chunk-vm__note {
          margin: 0 0 0.5rem 0;
          font-size: 0.75rem;
          color: var(--color-text-secondary);
          line-height: 1.35;
        }
        .chunk-vm__waiting {
          margin: 0 0 0.75rem 0;
          font-size: 0.8125rem;
          color: var(--color-info);
          font-style: italic;
        }
        .chunk-vm__table-wrap {
          overflow-x: auto;
        }
        .chunk-vm__table {
          width: 100%;
          border-collapse: collapse;
          font-size: 0.8125rem;
        }
        .chunk-vm__table th,
        .chunk-vm__table td {
          padding: 0.35rem 0.5rem;
          text-align: left;
          border-bottom: 1px solid var(--color-border);
        }
        .chunk-vm__table th {
          color: var(--color-label);
          font-weight: 600;
        }
        .chunk-vm__th-cancel {
          width: 2.5rem;
        }
        .chunk-vm__num {
          font-variant-numeric: tabular-nums;
          font-weight: 600;
          color: var(--color-heading);
        }
        .chunk-vm__time {
          font-variant-numeric: tabular-nums;
          font-family: ui-monospace, monospace;
        }
        .chunk-vm__cancel {
          text-align: center;
          vertical-align: middle;
        }
        .chunk-vm__x {
          width: 1.75rem;
          height: 1.75rem;
          padding: 0;
          line-height: 1.65rem;
          font-size: 1.25rem;
          border-radius: 4px;
          border: 1px solid var(--color-border-strong);
          background: var(--color-surface);
          color: var(--color-danger, #c62828);
          cursor: pointer;
        }
        .chunk-vm__x:hover:not(:disabled) {
          background: var(--color-surface-hover);
        }
        .chunk-vm__x:disabled {
          opacity: 0.4;
          cursor: not-allowed;
        }
        .chunk-vm__x-placeholder {
          display: inline-block;
          width: 1.75rem;
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
