import { useEffect, useRef, useState } from 'react';
import {
  postJobChunkAction,
  type ChunkActionName,
  type PostChunkActionOptions,
} from '../api';
import type {
  ChunkVirtualModelEntry,
  JobProjectFiles,
  JobSnapshot,
} from '../types';
import { chunkHasBlockingOperatorSplitArtifacts } from '../utils/chunkArtifactGroups';
import { elapsedSeconds, formatMmSs } from '../utils/chunkVmFormat';

function labelForChunkState(state: string, t: (key: string) => string): string {
  const key = `chunkState${state}`;
  const v = t(key);
  return v === key ? state : v;
}

function chunkVmRowKey(row: ChunkVirtualModelEntry): string {
  if (row.isSubChunk === true)
    return `sub-${row.parentChunkIndex}-${row.subChunkIndex}`;
  return `main-${row.index}`;
}

function chunkVmRowLabel(row: ChunkVirtualModelEntry): string {
  if (row.isSubChunk === true)
    return `#${row.parentChunkIndex}.${row.subChunkIndex}`;
  return `#${row.index}`;
}

export interface ChunkControlPanelProps {
  jobId: string;
  job: JobSnapshot;
  t: (key: string) => string;
  /** Selected chunk for operator actions (shared with Chunk controls Stats). */
  chunkIndex: number;
  onChunkIndexChange: (index: number) => void;
  /** When set, project file lists can narrow to this chunk index. */
  fileFilterChunkIndex: number | null;
  onFileFilterChunkChange: (index: number | null) => void;
  /** From useJobProjectFiles; Retranscribe/Split gated when blocking operator-split artifacts exist (not merged-only). */
  projectFiles: JobProjectFiles | null;
}

export function ChunkControlPanel({
  jobId,
  job,
  t,
  chunkIndex,
  onChunkIndexChange,
  fileFilterChunkIndex,
  onFileFilterChunkChange,
  projectFiles,
}: ChunkControlPanelProps) {
  const total = job.chunks?.total ?? 0;
  const vm = job.chunks?.chunkVirtualModel;
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [nowTick, setNowTick] = useState(() => Date.now());

  const transcriberRunning =
    job.phase === 'transcriber' && job.status === 'running';
  const live =
    transcriberRunning &&
    total > 0 &&
    chunkIndex >= 0 &&
    chunkIndex < total;

  const hasRunningVm = vm?.some((r) => r.state === 'Running') ?? false;

  const readOnly =
    job.status === 'failed' && (vm?.length ?? 0) > 0;
  const viewOnly = !readOnly && !transcriberRunning;

  const prevTranscriberRunningRef = useRef(false);
  useEffect(() => {
    if (!hasRunningVm) return;
    const id = window.setInterval(() => setNowTick(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, [hasRunningVm]);

  useEffect(() => {
    if (prevTranscriberRunningRef.current && !transcriberRunning && !readOnly)
      onFileFilterChunkChange(null);
    prevTranscriberRunningRef.current = transcriberRunning;
  }, [transcriberRunning, readOnly, onFileFilterChunkChange]);

  useEffect(() => {
    if (fileFilterChunkIndex === null) return;
    onFileFilterChunkChange(chunkIndex);
  }, [chunkIndex, fileFilterChunkIndex, onFileFilterChunkChange]);

  const runAction = async (
    action: ChunkActionName,
    index?: number,
    options?: PostChunkActionOptions
  ) => {
    const idx = index ?? chunkIndex;
    setMessage(null);
    setBusy(true);
    try {
      const res = await postJobChunkAction(jobId, action, idx, options);
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

  const runSplit = async () => {
    const idx = chunkIndex;
    const raw = window.prompt(t('chunkSplitPartsPrompt'), '2');
    if (raw == null) return;
    const n = parseInt(raw, 10);
    if (Number.isNaN(n) || n < 2) {
      setMessage(t('chunkSplitPartsInvalid'));
      return;
    }
    await runAction('split', idx, { splitParts: n });
  };

  const operatorChunkHasSplitArtifacts = chunkHasBlockingOperatorSplitArtifacts(
    projectFiles,
    chunkIndex
  );

  const showVmTable = vm && vm.length > 0;
  const showVmPlaceholder =
    !showVmTable && total > 0 && (transcriberRunning || viewOnly);

  return (
    <section className="chunk-panel" aria-label={t('chunkOperatorTitle')}>
      <h4 className="chunk-panel__title">{t('chunkOperatorTitle')}</h4>
      <p className="chunk-panel__hint">{t('chunkOperatorHint')}</p>
      {readOnly && (
        <p className="chunk-panel__readonly" role="status">
          {t('chunkPanelReadOnlyAfterFailure')}
        </p>
      )}
      {viewOnly && (
        <p className="chunk-panel__viewonly" role="status">
          {t('chunkPanelViewOnlyDisk')}
        </p>
      )}

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
                  <th scope="col">{t('chunkVmColError')}</th>
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
                  const canCancelRow =
                    live && row.state === 'Running' && row.isSubChunk !== true;
                  return (
                    <tr key={chunkVmRowKey(row)}>
                      <td className="chunk-vm__num">{chunkVmRowLabel(row)}</td>
                      <td className="chunk-vm__time" title={row.startedAt ?? ''}>
                        {elapsed}
                      </td>
                      <td>{labelForChunkState(row.state, t)}</td>
                      <td
                        className="chunk-vm__err"
                        title={row.errorMessage ?? undefined}
                      >
                        {row.errorMessage?.trim()
                          ? row.errorMessage
                          : t('chunkVmNoStarted')}
                      </td>
                      <td className="chunk-vm__cancel">
                        {canCancelRow ? (
                          <button
                            type="button"
                            className="chunk-vm__x"
                            disabled={!live || busy}
                            aria-label={t('chunkVmCancelAria').replace(
                              '{n}',
                              chunkVmRowLabel(row)
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
        <div className="chunk-vm">
          <h5 className="chunk-vm__title">{t('chunkVmListTitle')}</h5>
          <p className="chunk-vm__note">{t('chunkVmSkeletonNote')}</p>
          <div className="chunk-vm__table-wrap">
            <table className="chunk-vm__table">
              <thead>
                <tr>
                  <th scope="col">{t('chunkVmColChunk')}</th>
                  <th scope="col">{t('chunkVmColElapsed')}</th>
                  <th scope="col">{t('chunkVmColState')}</th>
                  <th scope="col">{t('chunkVmColError')}</th>
                  <th scope="col" className="chunk-vm__th-cancel">
                    <span className="visually-hidden">{t('chunkCancelChunk')}</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {Array.from({ length: total }, (_, i) => (
                  <tr key={i}>
                    <td className="chunk-vm__num">#{i}</td>
                    <td className="chunk-vm__time">{t('chunkVmNoStarted')}</td>
                    <td>{labelForChunkState('Pending', t)}</td>
                    <td>{t('chunkVmNoStarted')}</td>
                    <td className="chunk-vm__cancel">
                      <span className="chunk-vm__x-placeholder" />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className="chunk-vm__waiting">{t('chunkVmWaitingVm')}</p>
        </div>
      )}

      <div className="chunk-panel__row">
        <label className="chunk-panel__label">
          {t('chunkIndexLabel')}
          <input
            type="number"
            min={0}
            max={total > 0 ? total - 1 : undefined}
            value={chunkIndex}
            disabled={total <= 0 || readOnly}
            onChange={(e) => {
              const v = parseInt(e.target.value, 10);
              if (Number.isNaN(v)) return;
              onChunkIndexChange(v);
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
            disabled={total <= 0 || readOnly}
          />
          {t('chunkFilterProjectFiles')}
        </label>
      </div>
      {fileFilterChunkIndex !== null && (
        <p className="chunk-panel__filter-note">
          {t('chunkFilterActive')}: #{fileFilterChunkIndex}
        </p>
      )}
      {live && (
        <div className="chunk-panel__actions">
          <button
            type="button"
            disabled={!live || busy}
            onClick={() => runAction('cancel')}
          >
            {t('chunkCancelChunk')}
          </button>
          <button
            type="button"
            disabled={!live || busy}
            onClick={() => runAction('skip')}
          >
            {t('chunkSkip')}
          </button>
          <button
            type="button"
            disabled={!live || busy || operatorChunkHasSplitArtifacts}
            title={
              operatorChunkHasSplitArtifacts
                ? t('chunkRetranscribeBlockedSplit')
                : undefined
            }
            onClick={() => runAction('retranscribe')}
          >
            {t('chunkRetranscribe')}
          </button>
          {!operatorChunkHasSplitArtifacts && (
            <button
              type="button"
              disabled={!live || busy}
              title={t('chunkSplitTitle')}
              onClick={() => void runSplit()}
            >
              {t('chunkSplit')}
            </button>
          )}
        </div>
      )}
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
        .chunk-panel__readonly,
        .chunk-panel__viewonly {
          margin: 0 0 0.75rem 0;
          padding: 0.5rem 0.65rem;
          font-size: 0.8125rem;
          border-radius: 6px;
          border: 1px solid var(--color-border-strong);
          background: var(--color-surface);
          color: var(--color-label);
        }
        .chunk-vm__err {
          max-width: 14rem;
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
          font-size: 0.75rem;
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
