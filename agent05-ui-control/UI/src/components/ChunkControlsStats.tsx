import { useEffect, useState } from 'react';
import { postJobChunkAction, type ChunkActionName } from '../api';
import type {
  ChunkVirtualModelEntry,
  JobProjectFile,
  JobProjectFiles,
  JobSnapshot,
} from '../types';
import {
  buildChunkGroups,
  chunkArtifactsTranscriptionComplete,
} from '../utils/chunkArtifactGroups';
import {
  elapsedSeconds,
  formatIsoDateTime,
  formatMmSs,
} from '../utils/chunkVmFormat';
import { FileRow, TextFileEditorModal } from './ProjectFilesPanel';

function labelForChunkState(state: string, t: (key: string) => string): string {
  const key = `chunkState${state}`;
  const v = t(key);
  return v === key ? state : v;
}

/** Show Agent04 VM block only when we have timing / Running / error — not bare Pending with no logs. */
function vmRowHasTelemetry(vm: ChunkVirtualModelEntry | null): boolean {
  if (!vm) return false;
  if (vm.state === 'Running') return true;
  if (vm.startedAt || vm.completedAt) return true;
  if (vm.errorMessage?.trim()) return true;
  return false;
}

export interface ChunkControlsStatsProps {
  jobId: string;
  job: JobSnapshot;
  /** From useJobProjectFiles; null while loading or missing. */
  files: JobProjectFiles | null;
  filesLoading: boolean;
  filesError: string | null;
  /** Bumps when job snapshot updates (same as ProjectFilesPanel). */
  filesRefreshKey: number;
  /** Same index as Chunk controls (operator row). */
  chunkOperatorIndex: number;
  locale: 'en' | 'ru' | 'es';
  t: (key: string) => string;
  /** After saving a file in the editor, refresh GET .../files (e.g. jobFiles.reload). */
  onProjectFilesChanged?: () => void;
}

const emptyFiles: JobProjectFiles = {
  original: [],
  transcripts: [],
  chunks: [],
  chunkJson: [],
  intermediate: [],
  converted: [],
  splitChunks: [],
};

export function ChunkControlsStats({
  jobId,
  job,
  files,
  filesLoading,
  filesError,
  filesRefreshKey,
  chunkOperatorIndex,
  locale,
  t,
  onProjectFilesChanged,
}: ChunkControlsStatsProps) {
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [nowTick, setNowTick] = useState(() => Date.now());
  const [editTarget, setEditTarget] = useState<{
    relativePath: string;
    name: string;
  } | null>(null);

  const openEditor = (f: JobProjectFile) =>
    setEditTarget({ relativePath: f.relativePath, name: f.name });

  const transcriberRunning =
    job.phase === 'transcriber' && job.status === 'running';
  const vmAll = job.chunks?.chunkVirtualModel;
  const hasRunningVm = vmAll?.some((r) => r.state === 'Running') ?? false;

  useEffect(() => {
    if (!hasRunningVm) return;
    const id = window.setInterval(() => setNowTick(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, [hasRunningVm]);

  const total = job.chunks?.total ?? 0;
  const readOnly =
    job.status === 'failed' && (vmAll?.length ?? 0) > 0;
  const live =
    transcriberRunning &&
    total > 0 &&
    chunkOperatorIndex >= 0 &&
    chunkOperatorIndex < total;

  const fileData = files ?? emptyFiles;
  const groups = buildChunkGroups(job, fileData);
  const showList = groups.length > 0;
  const anyVmTelemetry = groups.some((g) => vmRowHasTelemetry(g.vmRow));

  const runAction = async (
    action: ChunkActionName,
    chunkIndex: number,
    splitParts?: number
  ) => {
    setMessage(null);
    setBusy(true);
    try {
      const res = await postJobChunkAction(
        jobId,
        action,
        chunkIndex,
        splitParts
      );
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

  const runSplit = async (chunkIndex: number) => {
    const raw = window.prompt(t('chunkSplitPartsPrompt'), '2');
    if (raw == null) return;
    const n = parseInt(raw, 10);
    if (Number.isNaN(n) || n < 2) {
      setMessage(t('chunkSplitPartsInvalid'));
      return;
    }
    await runAction('split', chunkIndex, n);
  };

  return (
    <section
      className="chunk-stats"
      key={`${jobId}-${filesRefreshKey}`}
      aria-label={t('chunkStatsTitle')}
    >
      <h4 className="chunk-stats__title">{t('chunkStatsTitle')}</h4>
      {showList && anyVmTelemetry && (
        <p className="chunk-stats__hint">{t('chunkVmTimerNote')}</p>
      )}
      {filesLoading && !showList && (
        <p className="chunk-stats__status">{t('chunkStatsLoadingFiles')}</p>
      )}
      {filesError && (
        <p className="chunk-stats__status chunk-stats__status--err">
          {filesError}
        </p>
      )}
      {!filesLoading && !showList && (
        <p className="chunk-stats__status">{t('chunkStatsEmpty')}</p>
      )}
      {showList && (
        <ul className="chunk-stats__list">
          {groups.map((g) => {
            const vm = g.vmRow;
            const showVmBlock = vmRowHasTelemetry(vm);
            const artifactComplete = chunkArtifactsTranscriptionComplete(
              g.audioFiles,
              g.jsonFiles
            );
            const showArtifactDone = !showVmBlock && artifactComplete;

            const stateLabel = vm
              ? labelForChunkState(vm.state, t)
              : t('chunkStatePending');
            const sec = vm && showVmBlock ? elapsedSeconds(vm, nowTick) : null;
            const elapsed =
              sec === null ? t('chunkVmNoStarted') : formatMmSs(sec);
            const errFull = vm?.errorMessage?.trim() ?? '';
            const detailText = errFull ? errFull : t('chunkVmNoStarted');

            const isOperatorChunk = g.index === chunkOperatorIndex;
            const showFullOperator =
              showVmBlock && live && isOperatorChunk && !readOnly;
            const showRunningOnlyCancel =
              showVmBlock &&
              live &&
              vm?.state === 'Running' &&
              !(isOperatorChunk && !readOnly);

            return (
              <li key={g.index} className="chunk-stats__card">
                <div className="chunk-stats__card-head">
                  <span className="chunk-stats__chunk-label">
                    {t('chunkStatsChunkPrefix')} #{g.index}
                    {g.displayStem ? (
                      <span className="chunk-stats__stem" title={g.displayStem}>
                        {' '}
                        — {g.displayStem}
                      </span>
                    ) : null}
                  </span>
                  {isOperatorChunk && (
                    <span className="chunk-stats__op-badge" title={t('chunkStatsOperatorChunk')}>
                      {t('chunkStatsOperatorChunk')}
                    </span>
                  )}
                </div>

                {showVmBlock && vm && (
                  <div className="chunk-stats__vm">
                    <div className="chunk-stats__vm-title">
                      {t('chunkStatsVmBlockTitle')}
                    </div>
                    <dl className="chunk-stats__vm-grid">
                      <dt>{t('chunkVmColElapsed')}</dt>
                      <dd
                        className="chunk-stats__vm-mono"
                        title={vm.startedAt ?? ''}
                      >
                        {elapsed}
                      </dd>
                      <dt>{t('chunkVmColState')}</dt>
                      <dd>{stateLabel}</dd>
                      <dt>{t('chunkVmColError')}</dt>
                      <dd
                        className="chunk-stats__vm-err"
                        title={errFull || undefined}
                      >
                        {detailText}
                      </dd>
                    </dl>
                    {(vm.startedAt || vm.completedAt) && (
                      <div className="chunk-stats__vm-times">
                        {vm.startedAt && (
                          <span className="chunk-stats__vm-time">
                            <span className="chunk-stats__vm-time-label">
                              {t('chunkStatsStartedAt')}
                            </span>{' '}
                            {formatIsoDateTime(vm.startedAt, locale)}
                          </span>
                        )}
                        {vm.completedAt && (
                          <span className="chunk-stats__vm-time">
                            <span className="chunk-stats__vm-time-label">
                              {t('chunkStatsCompletedAt')}
                            </span>{' '}
                            {formatIsoDateTime(vm.completedAt, locale)}
                          </span>
                        )}
                      </div>
                    )}

                    {showRunningOnlyCancel && (
                      <div className="chunk-stats__vm-running">
                        <span className="chunk-stats__inflight">
                          {t('chunkStatsTranscriptionActive')}
                        </span>
                        <button
                          type="button"
                          className="chunk-stats__vm-x"
                          disabled={busy}
                          aria-label={t('chunkVmCancelAria').replace(
                            '{n}',
                            String(g.index)
                          )}
                          onClick={() => void runAction('cancel', g.index)}
                        >
                          ×
                        </button>
                      </div>
                    )}

                    {showFullOperator && (
                      <div className="chunk-stats__vm-actions">
                        <button
                          type="button"
                          disabled={busy}
                          onClick={() => void runAction('cancel', g.index)}
                        >
                          {t('chunkCancelChunk')}
                        </button>
                        <button
                          type="button"
                          disabled={busy}
                          onClick={() => void runAction('skip', g.index)}
                        >
                          {t('chunkSkip')}
                        </button>
                        <button
                          type="button"
                          disabled={busy}
                          onClick={() =>
                            void runAction('retranscribe', g.index)
                          }
                        >
                          {t('chunkRetranscribe')}
                        </button>
                        <button
                          type="button"
                          disabled={busy}
                          title={t('chunkSplitTitle')}
                          onClick={() => void runSplit(g.index)}
                        >
                          {t('chunkSplit')}
                        </button>
                      </div>
                    )}
                  </div>
                )}

                {showArtifactDone && (
                  <div className="chunk-stats__artifact-done">
                    <span className="chunk-stats__artifact-done-label">
                      {t('chunkStatsArtifactCompleted')}
                    </span>
                  </div>
                )}

                {g.audioFiles.length > 0 && (
                  <div className="chunk-stats__block">
                    <div className="chunk-stats__block-title">
                      {t('chunkStatsAudio')}
                    </div>
                    <ul className="pf-list chunk-stats__pf-list">
                      {g.audioFiles.map((f) => (
                        <FileRow
                          key={f.relativePath}
                          jobId={jobId}
                          f={f}
                          t={t}
                          onEditText={openEditor}
                        />
                      ))}
                    </ul>
                  </div>
                )}
                {g.jsonFiles.length > 0 && (
                  <div className="chunk-stats__block">
                    <div className="chunk-stats__block-title">
                      {t('chunkStatsJson')}
                    </div>
                    <ul className="pf-list chunk-stats__pf-list">
                      {g.jsonFiles.map((f) => (
                        <FileRow
                          key={f.relativePath}
                          jobId={jobId}
                          f={f}
                          t={t}
                          onEditText={openEditor}
                        />
                      ))}
                    </ul>
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}
      {message && <p className="chunk-stats__message">{message}</p>}
      {editTarget && (
        <TextFileEditorModal
          jobId={jobId}
          file={editTarget}
          onClose={() => setEditTarget(null)}
          onSaved={() => onProjectFilesChanged?.()}
          t={t}
        />
      )}
      <style>{`
        .chunk-stats {
          margin: 1rem 0;
          padding: 0.75rem 1rem;
          border: 1px solid var(--color-border);
          border-radius: 8px;
          background: var(--color-subtle-panel);
          color: var(--color-text);
        }
        .chunk-stats__title {
          margin: 0 0 0.65rem 0;
          font-size: 0.9rem;
          color: var(--color-heading);
        }
        .chunk-stats__hint {
          margin: -0.35rem 0 0.6rem 0;
          font-size: 0.72rem;
          line-height: 1.35;
          color: var(--color-text-secondary);
        }
        .chunk-stats__status {
          margin: 0;
          font-size: 0.8125rem;
          color: var(--color-text-secondary);
        }
        .chunk-stats__status--err {
          color: var(--color-error-muted, #c62828);
        }
        .chunk-stats__list {
          list-style: none;
          margin: 0;
          padding: 0;
          display: flex;
          flex-direction: column;
          gap: 0.65rem;
        }
        .chunk-stats__card {
          border: 1px solid var(--color-border);
          border-radius: 6px;
          padding: 0.5rem 0.65rem;
          background: var(--color-surface);
        }
        .chunk-stats__card-head {
          display: flex;
          flex-wrap: wrap;
          align-items: center;
          gap: 0.35rem 0.75rem;
          margin-bottom: 0.35rem;
        }
        .chunk-stats__chunk-label {
          font-weight: 600;
          font-variant-numeric: tabular-nums;
          color: var(--color-heading);
        }
        .chunk-stats__stem {
          font-weight: 500;
          color: var(--color-text-secondary);
        }
        .chunk-stats__op-badge {
          font-size: 0.65rem;
          text-transform: uppercase;
          letter-spacing: 0.04em;
          padding: 0.1rem 0.35rem;
          border-radius: 4px;
          border: 1px solid var(--color-border-strong);
          color: var(--color-info);
          background: var(--color-subtle-panel);
        }
        .chunk-stats__artifact-done {
          margin-bottom: 0.35rem;
          padding: 0.35rem 0.5rem;
          border-radius: 6px;
          border: 1px solid var(--color-border);
          background: color-mix(in srgb, var(--color-info) 12%, var(--color-surface));
          font-size: 0.8125rem;
          color: var(--color-heading);
        }
        .chunk-stats__artifact-done-label {
          font-weight: 600;
        }
        .chunk-stats__vm {
          margin-bottom: 0.35rem;
          padding: 0.4rem 0.5rem;
          border-radius: 6px;
          border: 1px solid var(--color-border);
          background: var(--color-subtle-panel);
        }
        .chunk-stats__vm-title {
          font-size: 0.75rem;
          font-weight: 600;
          color: var(--color-heading);
          margin-bottom: 0.35rem;
        }
        .chunk-stats__vm-grid {
          display: grid;
          grid-template-columns: auto 1fr;
          gap: 0.2rem 0.75rem;
          margin: 0;
          font-size: 0.8125rem;
        }
        .chunk-stats__vm-grid dt {
          margin: 0;
          color: var(--color-label);
          font-weight: 600;
        }
        .chunk-stats__vm-grid dd {
          margin: 0;
          min-width: 0;
        }
        .chunk-stats__vm-mono {
          font-variant-numeric: tabular-nums;
          font-family: ui-monospace, monospace;
        }
        .chunk-stats__vm-err {
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
          color: var(--color-text-secondary);
          font-size: 0.75rem;
        }
        .chunk-stats__vm-times {
          display: flex;
          flex-wrap: wrap;
          gap: 0.5rem 1rem;
          margin-top: 0.35rem;
          font-size: 0.72rem;
          color: var(--color-text-secondary);
        }
        .chunk-stats__vm-time-label {
          color: var(--color-label);
          font-weight: 600;
        }
        .chunk-stats__vm-running {
          display: flex;
          flex-wrap: wrap;
          align-items: center;
          gap: 0.35rem;
          margin-top: 0.4rem;
        }
        .chunk-stats__inflight {
          font-size: 0.7rem;
          color: var(--color-info);
          max-width: 16rem;
        }
        .chunk-stats__vm-x {
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
        .chunk-stats__vm-x:hover:not(:disabled) {
          background: var(--color-surface-hover);
        }
        .chunk-stats__vm-x:disabled {
          opacity: 0.4;
          cursor: not-allowed;
        }
        .chunk-stats__vm-actions {
          display: flex;
          flex-wrap: wrap;
          gap: 0.5rem;
          margin-top: 0.45rem;
        }
        .chunk-stats__vm-actions button {
          padding: 0.35rem 0.65rem;
          font-size: 0.8125rem;
          border: 1px solid var(--color-border-strong);
          border-radius: 4px;
          background: var(--color-surface);
          cursor: pointer;
          color: var(--color-heading);
        }
        .chunk-stats__vm-actions button:hover:not(:disabled) {
          background: var(--color-surface-hover);
        }
        .chunk-stats__vm-actions button:disabled {
          opacity: 0.5;
          cursor: not-allowed;
        }
        .chunk-stats__block { margin-top: 0.35rem; }
        .chunk-stats__block-title {
          font-size: 0.7rem;
          text-transform: uppercase;
          letter-spacing: 0.04em;
          color: var(--color-label);
          margin-bottom: 0.2rem;
        }
        .chunk-stats__pf-list {
          list-style: none;
          margin: 0;
          padding: 0;
        }
        .chunk-stats__pf-list .pf-file {
          border-bottom: 1px solid var(--color-border);
          padding: 0.35rem 0;
        }
        .chunk-stats__pf-list .pf-file:last-child {
          border-bottom: none;
        }
        .chunk-stats__muted {
          margin: 0.25rem 0 0 0;
          font-size: 0.75rem;
          color: var(--color-text-secondary);
          font-style: italic;
        }
        .chunk-stats__message {
          margin: 0.5rem 0 0 0;
          font-size: 0.8125rem;
          color: var(--color-label);
        }
      `}</style>
    </section>
  );
}
