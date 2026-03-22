import { useEffect, useState } from 'react';
import {
  deleteJobSubChunk,
  postJobChunkAction,
  type ChunkActionName,
  type PostChunkActionOptions,
} from '../api';
import type {
  ChunkVirtualModelEntry,
  JobProjectFile,
  JobProjectFiles,
  JobSnapshot,
} from '../types';
import {
  buildChunkGroups,
  chunkArtifactsTranscriptionComplete,
  chunkHasBlockingOperatorSplitArtifacts,
} from '../utils/chunkArtifactGroups';
import {
  elapsedSeconds,
  formatIsoDateTime,
  formatMmSs,
} from '../utils/chunkVmFormat';
import { FileRow, TextFileEditorModal } from './ProjectFilesPanel';

function labelForChunkState(state: string, t: (key: string) => string): string {
  const s = (state || '').trim();
  const pascal =
    s.length === 0
      ? 'Pending'
      : s.charAt(0).toUpperCase() + s.slice(1).toLowerCase();
  const key = `chunkState${pascal}`;
  const v = t(key);
  return v === key ? (s || 'Pending') : v;
}

function vmStateNorm(state: string | undefined | null): string {
  return (state || '').trim().toLowerCase();
}

function vmIsRunning(state: string | undefined | null): boolean {
  return vmStateNorm(state) === 'running';
}

function vmIsCancelled(state: string | undefined | null): boolean {
  return vmStateNorm(state) === 'cancelled';
}

/** Show Agent04 VM block when we have timing, Running, terminal state, or error — not bare Pending with no logs. */
function vmRowHasTelemetry(vm: ChunkVirtualModelEntry | null): boolean {
  if (!vm) return false;
  const s = vmStateNorm(vm.state);
  if (s === 'running') return true;
  if (vm.startedAt || vm.completedAt) return true;
  if (vm.errorMessage?.trim()) return true;
  if (vm.transcriptActivityLog?.trim()) return true;
  if (s === 'completed' || s === 'failed' || s === 'cancelled') return true;
  return false;
}

function VmActivityLogCol({
  transcriptActivityLog,
  t,
}: {
  transcriptActivityLog: string | null | undefined;
  t: (key: string) => string;
}) {
  const raw = transcriptActivityLog?.trim() ?? '';
  const lines = raw
    ? raw.split(/\r?\n/).filter((line) => line.trim().length > 0)
    : [];
  return (
    <div className="chunk-stats__vm-log-col">
      <div className="chunk-stats__vm-log-header">{t('chunkVmActivityLog')}</div>
      {lines.length > 0 ? (
        <div className="chunk-stats__vm-log-body" title={raw}>
          {lines.map((line, i) => (
            <div key={i} className="chunk-stats__vm-log-line">
              {line}
            </div>
          ))}
        </div>
      ) : (
        <div className="chunk-stats__vm-log-body chunk-stats__vm-log-body--empty chunk-stats__muted">
          {t('chunkVmActivityLogEmpty')}
        </div>
      )}
    </div>
  );
}

function SubChunkVmTelemetry({
  vm,
  locale,
  t,
  nowTick,
  busy,
  parentChunkIndex,
  subIndex,
  readOnly,
  cancelEnabled,
  retranscribeEnabled,
  onCancelSub,
  onRetranscribeSub,
  onSkipSub,
  onSplitParent,
}: {
  vm: ChunkVirtualModelEntry;
  locale: 'en' | 'ru' | 'es';
  t: (key: string) => string;
  nowTick: number;
  busy: boolean;
  parentChunkIndex: number;
  subIndex: number;
  readOnly: boolean;
  cancelEnabled: boolean;
  retranscribeEnabled: boolean;
  onCancelSub: () => void;
  onRetranscribeSub: () => void;
  onSkipSub: () => void;
  onSplitParent: () => void;
}) {
  const hasTelemetry = vmRowHasTelemetry(vm);
  const stateLabel = labelForChunkState(vm.state, t);
  const sec = hasTelemetry ? elapsedSeconds(vm, nowTick) : null;
  const elapsed = sec === null ? t('chunkVmNoStarted') : formatMmSs(sec);
  const errFull = vm.errorMessage?.trim() ?? '';
  const detailText = errFull ? errFull : t('chunkVmNoStarted');
  const subSkipDisabled = true;
  const subSplitDisabled = true;
  return (
    <div className="chunk-stats__vm chunk-stats__vm--sub">
      <div className="chunk-stats__vm-title">{t('chunkStatsVmBlockTitle')}</div>
      <div className="chunk-stats__vm-body">
        <div className="chunk-stats__vm-telemetry-col">
          <dl className="chunk-stats__vm-grid">
            <dt>{t('chunkVmColElapsed')}</dt>
            <dd className="chunk-stats__vm-mono" title={vm.startedAt ?? ''}>
              {elapsed}
            </dd>
            <dt>{t('chunkVmColState')}</dt>
            <dd>{stateLabel}</dd>
            <dt>{t('chunkVmColError')}</dt>
            <dd className="chunk-stats__vm-err" title={errFull || undefined}>
              {detailText}
            </dd>
          </dl>
          {hasTelemetry && (vm.startedAt || vm.completedAt) && (
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
          {vmIsRunning(vm.state) && (
            <div className="chunk-stats__vm-running">
              <span className="chunk-stats__inflight">
                {cancelEnabled
                  ? t('chunkStatsSubChunkRunning')
                  : t('chunkStatsSubChunkRunningNoCancel')}
              </span>
            </div>
          )}
        </div>
        <div className="chunk-stats__vm-actions-col">
          <div className="chunk-stats__vm-actions chunk-stats__vm-actions-stack">
            <button
              type="button"
              disabled={busy || readOnly || !cancelEnabled}
              title={t('chunkCancelChunk')}
              aria-label={t('chunkCancelSubAria')
                .replace('{p}', String(parentChunkIndex))
                .replace('{s}', String(subIndex))}
              onClick={onCancelSub}
            >
              {t('chunkCancelChunk')}
            </button>
            <button
              type="button"
              disabled={busy || readOnly || subSkipDisabled}
              title={t('chunkSkip')}
              onClick={onSkipSub}
            >
              {t('chunkSkip')}
            </button>
            <button
              type="button"
              disabled={busy || readOnly || !retranscribeEnabled}
              title={t('chunkRetranscribe')}
              onClick={onRetranscribeSub}
            >
              {t('chunkRetranscribe')}
            </button>
            <button
              type="button"
              disabled={busy || readOnly || subSplitDisabled}
              title={t('chunkSplitTitle')}
              onClick={onSplitParent}
            >
              {t('chunkSplit')}
            </button>
          </div>
        </div>
        <VmActivityLogCol
          transcriptActivityLog={vm.transcriptActivityLog}
          t={t}
        />
      </div>
    </div>
  );
}

export interface ChunkControlsStatsProps {
  jobId: string;
  job: JobSnapshot;
  /** From useJobProjectFiles; null while loading or missing. */
  files: JobProjectFiles | null;
  filesLoading: boolean;
  filesError: string | null;
  /** Same index as Chunk controls (operator row). */
  chunkOperatorIndex: number;
  locale: 'en' | 'ru' | 'es';
  t: (key: string) => string;
  /** After saving a file in the editor, refresh GET .../files (e.g. jobFiles.reload). */
  onProjectFilesChanged?: () => void | Promise<void>;
  /** Poll GET /api/jobs/:id while any VM row is Running (retranscribe / HTTP in flight). */
  refreshJobSnapshot?: () => Promise<void>;
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
  chunkOperatorIndex,
  locale,
  t,
  onProjectFilesChanged,
  refreshJobSnapshot,
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
  const jobStatusAllowsPostDoneChunkOps = (() => {
    const s = (job.status || '').toLowerCase();
    return s === 'done' || s === 'completed';
  })();
  const canTranscribeSubChunk =
    Boolean(job.agent04JobId?.trim()) &&
    (transcriberRunning || jobStatusAllowsPostDoneChunkOps);
  const canPostDoneChunkOps = canTranscribeSubChunk;
  const canCancelRunningSubChunk =
    canPostDoneChunkOps && Boolean(job.agent04JobId?.trim());
  const vmAll = job.chunks?.chunkVirtualModel;
  const hasRunningVm = vmAll?.some((r) => vmIsRunning(r.state)) ?? false;

  useEffect(() => {
    if (!hasRunningVm) return;
    const id = window.setInterval(() => setNowTick(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, [hasRunningVm]);

  useEffect(() => {
    if (!hasRunningVm || !refreshJobSnapshot) return;
    const id = window.setInterval(() => {
      void refreshJobSnapshot();
    }, 2800);
    return () => window.clearInterval(id);
  }, [hasRunningVm, refreshJobSnapshot]);

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
    options?: PostChunkActionOptions
  ) => {
    setMessage(null);
    setBusy(true);
    try {
      const res = await postJobChunkAction(jobId, action, chunkIndex, options);
      setMessage(
        res.ok
          ? res.message || t('chunkActionOk')
          : `${t('chunkActionRejected')}: ${res.message}`
      );
      if (res.ok) await Promise.resolve(onProjectFilesChanged?.());
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
    await runAction('split', chunkIndex, { splitParts: n });
  };

  return (
    <section className="chunk-stats" aria-label={t('chunkStatsTitle')}>
      <h4 className="chunk-stats__title">{t('chunkStatsTitle')}</h4>
      {showList && canPostDoneChunkOps && !readOnly && (
        <div className="chunk-stats__global-actions">
          <button
            type="button"
            className="chunk-stats__rebuild-combined"
            disabled={busy}
            title={t('chunkRebuildCombinedHint')}
            onClick={() => void runAction('rebuild_combined', 0)}
          >
            {t('chunkRebuildCombined')}
          </button>
        </div>
      )}
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
            const hasVmTelemetry = vmRowHasTelemetry(vm);
            const artifactComplete = chunkArtifactsTranscriptionComplete(
              g.audioFiles,
              g.jsonFiles
            );
            const chunkSnapshotCompleted =
              job.chunks?.completed?.includes(g.index) ?? false;
            const diskCompleteSignal =
              artifactComplete || chunkSnapshotCompleted;
            const showArtifactDone = !hasVmTelemetry && diskCompleteSignal;

            const chunkMainRunning = vmIsRunning(vm?.state);
            const stateLabel = (() => {
              if (vm) {
                if (hasVmTelemetry)
                  return labelForChunkState(vm.state, t);
                if (
                  diskCompleteSignal &&
                  vmStateNorm(vm.state) === 'pending' &&
                  !chunkMainRunning
                )
                  return t('chunkStateCompleted');
                return labelForChunkState(vm.state, t);
              }
              if (diskCompleteSignal) return t('chunkStateCompleted');
              return t('chunkStatePending');
            })();
            const sec =
              vm && hasVmTelemetry ? elapsedSeconds(vm, nowTick) : null;
            const elapsed =
              sec === null ? t('chunkVmNoStarted') : formatMmSs(sec);
            const errFull = vm?.errorMessage?.trim() ?? '';
            const detailText = errFull ? errFull : t('chunkVmNoStarted');

            const isOperatorChunk = g.index === chunkOperatorIndex;
            const chunkIsCancelled =
              vmIsCancelled(vm?.state) ||
              (job.chunks?.cancelled?.includes(g.index) ?? false);
            const showCancelledSplitRetranscribe =
              chunkIsCancelled && canPostDoneChunkOps && !readOnly;
            const showFullOperator =
              live && isOperatorChunk && !readOnly;
            const chunkVmPending =
              !vm ||
              vmStateNorm(vm.state) === '' ||
              vmStateNorm(vm.state) === 'pending';
            const showRetryMainRetranscribe =
              canPostDoneChunkOps &&
              !readOnly &&
              !chunkMainRunning &&
              !showCancelledSplitRetranscribe &&
              !showFullOperator &&
              (hasVmTelemetry || showArtifactDone || chunkVmPending);
            const showRunningOnlyCancel =
              live &&
              vmIsRunning(vm?.state) &&
              !(isOperatorChunk && !readOnly);
            const hasSplitArtifacts = chunkHasBlockingOperatorSplitArtifacts(
              fileData,
              g.index
            );
            const mainVmActionsInPanel = true;
            const mainCancelEnabled =
              live && chunkMainRunning && !readOnly;
            const mainSkipEnabled =
              live && isOperatorChunk && !readOnly;
            const mainRetranscribeEnabled =
              !readOnly &&
              !hasSplitArtifacts &&
              (showFullOperator ||
                showCancelledSplitRetranscribe ||
                showRetryMainRetranscribe);
            const mainSplitEnabled =
              !readOnly &&
              !hasSplitArtifacts &&
              canPostDoneChunkOps &&
              (showFullOperator || showCancelledSplitRetranscribe);

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

                {showCancelledSplitRetranscribe && !mainVmActionsInPanel && (
                  <div className="chunk-stats__cancelled-actions">
                    <button
                      type="button"
                      disabled={busy}
                      onClick={() => void runAction('retranscribe', g.index)}
                    >
                      {t('chunkRetranscribe')}
                    </button>
                    {!hasSplitArtifacts && (
                      <button
                        type="button"
                        disabled={busy}
                        title={t('chunkSplitTitle')}
                        onClick={() => void runSplit(g.index)}
                      >
                        {t('chunkSplit')}
                      </button>
                    )}
                  </div>
                )}

                {showRetryMainRetranscribe && !mainVmActionsInPanel && (
                  <div className="chunk-stats__cancelled-actions">
                    <button
                      type="button"
                      disabled={busy}
                      onClick={() => void runAction('retranscribe', g.index)}
                    >
                      {t('chunkRetranscribe')}
                    </button>
                  </div>
                )}

                <div className="chunk-stats__vm">
                  <div className="chunk-stats__vm-title">
                    {t('chunkStatsVmBlockTitle')}
                  </div>
                  <div className="chunk-stats__vm-body">
                    <div className="chunk-stats__vm-telemetry-col">
                      <dl className="chunk-stats__vm-grid">
                        <dt>{t('chunkVmColElapsed')}</dt>
                        <dd
                          className="chunk-stats__vm-mono"
                          title={vm?.startedAt ?? ''}
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
                      {vm &&
                        hasVmTelemetry &&
                        (vm.startedAt || vm.completedAt) && (
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
                            {mainCancelEnabled
                              ? t('chunkStatsTranscriptionActive')
                              : t('chunkStatsTranscriptionActiveNoCancel')}
                          </span>
                        </div>
                      )}
                    </div>
                    <div className="chunk-stats__vm-actions-col">
                      <div className="chunk-stats__vm-actions chunk-stats__vm-actions-stack">
                        <button
                          type="button"
                          disabled={busy || !mainCancelEnabled}
                          title={t('chunkCancelChunk')}
                          aria-label={t('chunkVmCancelAria').replace(
                            '{n}',
                            String(g.index)
                          )}
                          onClick={() => void runAction('cancel', g.index)}
                        >
                          {t('chunkCancelChunk')}
                        </button>
                        <button
                          type="button"
                          disabled={busy || !mainSkipEnabled}
                          title={t('chunkSkip')}
                          onClick={() => void runAction('skip', g.index)}
                        >
                          {t('chunkSkip')}
                        </button>
                        <button
                          type="button"
                          disabled={busy || !mainRetranscribeEnabled}
                          title={
                            hasSplitArtifacts
                              ? t('chunkRetranscribeBlockedSplit')
                              : t('chunkRetranscribe')
                          }
                          onClick={() =>
                            void runAction('retranscribe', g.index)
                          }
                        >
                          {t('chunkRetranscribe')}
                        </button>
                        <button
                          type="button"
                          disabled={busy || !mainSplitEnabled}
                          title={t('chunkSplitTitle')}
                          onClick={() => void runSplit(g.index)}
                        >
                          {t('chunkSplit')}
                        </button>
                      </div>
                    </div>
                    <VmActivityLogCol
                      transcriptActivityLog={vm?.transcriptActivityLog}
                      t={t}
                    />
                  </div>
                </div>

                {showArtifactDone && (
                  <div className="chunk-stats__artifact-done">
                    <span className="chunk-stats__artifact-done-label">
                      {t('chunkStatsArtifactCompleted')}
                    </span>
                  </div>
                )}

                {(g.audioFiles.length > 0 || g.jsonFiles.length > 0) && (
                  <div
                    className="chunk-stats__media-row"
                    aria-label={`${t('chunkStatsAudio')} / ${t('chunkStatsJson')}`}
                  >
                    <div className="chunk-stats__media-row__audio">
                      {g.audioFiles.length > 0 ? (
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
                      ) : (
                        <p className="chunk-stats__media-row__empty chunk-stats__muted">
                          {t('chunkStatsNoAudioInRow')}
                        </p>
                      )}
                    </div>
                    <div className="chunk-stats__media-row__text">
                      {g.jsonFiles.length > 0 ? (
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
                      ) : (
                        <p className="chunk-stats__media-row__empty chunk-stats__muted">
                          {t('chunkStatsNoTranscriptFile')}
                        </p>
                      )}
                    </div>
                  </div>
                )}

                {g.mergedSplitFiles.length > 0 && (
                  <div className="chunk-stats__block">
                    <div className="chunk-stats__block-title">
                      {t('chunkStatsSplitMerged')}
                    </div>
                    <ul className="pf-list chunk-stats__pf-list">
                      {g.mergedSplitFiles.map((f) => (
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

                {g.subChunks.length > 0 && (
                  <div className="chunk-stats__block chunk-stats__block--sub">
                    <div className="chunk-stats__block-title">
                      {t('chunkStatsSubChunks')}
                    </div>
                    <ul className="chunk-stats__sub-list">
                      {g.subChunks.map((s) => {
                        const subVmRunning = vmIsRunning(s.vmRow?.state);
                        const subIdxResolved =
                          s.subIndex ?? s.vmRow?.subChunkIndex ?? null;
                        const subCancelEnabled =
                          live &&
                          subVmRunning &&
                          canCancelRunningSubChunk &&
                          subIdxResolved != null &&
                          !readOnly;
                        const subRetranscribeEnabled =
                          canTranscribeSubChunk &&
                          !readOnly &&
                          subIdxResolved != null &&
                          s.audioFiles.length > 0 &&
                          !subVmRunning;
                        return (
                        <li
                          key={`${g.index}-sub-${s.subIndex ?? 'u'}-${s.audioFiles[0]?.relativePath ?? s.jsonFiles[0]?.relativePath ?? 'empty'}`}
                          className="chunk-stats__sub-card"
                        >
                          <div className="chunk-stats__sub-head">
                            <span className="chunk-stats__sub-label">
                              {t('chunkStatsSubChunkPrefix')} #
                              {s.subIndex ?? '?'}
                              {s.displayStem ? (
                                <span
                                  className="chunk-stats__stem"
                                  title={s.displayStem}
                                >
                                  {' '}
                                  — {s.displayStem}
                                </span>
                              ) : null}
                            </span>
                          </div>
                          {s.vmRow && (
                            <SubChunkVmTelemetry
                              vm={s.vmRow}
                              locale={locale}
                              t={t}
                              nowTick={nowTick}
                              busy={busy}
                              parentChunkIndex={g.index}
                              subIndex={
                                s.subIndex ?? s.vmRow.subChunkIndex ?? 0
                              }
                              readOnly={readOnly}
                              cancelEnabled={subCancelEnabled}
                              retranscribeEnabled={subRetranscribeEnabled}
                              onCancelSub={() => {
                                if (subIdxResolved == null) return;
                                void runAction('cancel', g.index, {
                                  subChunkIndex: subIdxResolved,
                                });
                              }}
                              onRetranscribeSub={() => {
                                if (subIdxResolved == null) return;
                                void runAction('transcribe_sub', g.index, {
                                  subChunkIndex: subIdxResolved,
                                });
                              }}
                              onSkipSub={() =>
                                void runAction('skip', g.index)
                              }
                              onSplitParent={() => void runSplit(g.index)}
                            />
                          )}
                          {!s.vmRow &&
                            s.subIndex != null &&
                            s.audioFiles.length > 0 &&
                            canTranscribeSubChunk &&
                            !subVmRunning && (
                              <div className="chunk-stats__sub-actions">
                                <button
                                  type="button"
                                  disabled={busy}
                                  title={t('chunkTranscribeSubTitle')}
                                  onClick={() =>
                                    void runAction('transcribe_sub', g.index, {
                                      subChunkIndex: s.subIndex!,
                                    })
                                  }
                                >
                                  {t('chunkTranscribeSub')}
                                </button>
                              </div>
                            )}
                          {(s.audioFiles.length > 0 ||
                            s.jsonFiles.length > 0) && (
                            <div
                              className="chunk-stats__media-row chunk-stats__media-row--sub"
                              aria-label={`${t('chunkStatsAudio')} / ${t('chunkStatsJson')}`}
                            >
                              <div className="chunk-stats__media-row__audio">
                                {s.audioFiles.length > 0 ? (
                                  <ul className="pf-list chunk-stats__pf-list">
                                    {s.audioFiles.map((f) => (
                                      <FileRow
                                        key={f.relativePath}
                                        jobId={jobId}
                                        f={f}
                                        t={t}
                                        onEditText={openEditor}
                                      />
                                    ))}
                                  </ul>
                                ) : (
                                  <p className="chunk-stats__media-row__empty chunk-stats__muted">
                                    {t('chunkStatsNoAudioInRow')}
                                  </p>
                                )}
                              </div>
                              <div className="chunk-stats__media-row__text">
                                {s.jsonFiles.length > 0 ? (
                                  <ul className="pf-list chunk-stats__pf-list">
                                    {s.jsonFiles.map((f) => (
                                      <FileRow
                                        key={f.relativePath}
                                        jobId={jobId}
                                        f={f}
                                        t={t}
                                        onEditText={openEditor}
                                      />
                                    ))}
                                  </ul>
                                ) : (
                                  <p className="chunk-stats__media-row__empty chunk-stats__muted">
                                    {t('chunkStatsNoTranscriptFile')}
                                  </p>
                                )}
                              </div>
                            </div>
                          )}
                          {canPostDoneChunkOps &&
                            !readOnly &&
                            subIdxResolved != null &&
                            !subVmRunning && (
                              <div className="chunk-stats__sub-delete-wrap">
                                <button
                                  type="button"
                                  className="chunk-stats__sub-delete-btn"
                                  disabled={busy}
                                  title={t('chunkDeleteSubChunkTitle')}
                                  onClick={() => {
                                    if (
                                      !window.confirm(
                                        t('chunkDeleteSubChunkConfirm')
                                          .replace(
                                            '{parent}',
                                            String(g.index)
                                          )
                                          .replace(
                                            '{sub}',
                                            String(subIdxResolved)
                                          )
                                      )
                                    )
                                      return;
                                    void (async () => {
                                      try {
                                        await deleteJobSubChunk(
                                          jobId,
                                          g.index,
                                          subIdxResolved!
                                        );
                                        await onProjectFilesChanged?.();
                                      } catch (e) {
                                        setMessage(
                                          e instanceof Error
                                            ? e.message
                                            : t('chunkActionFailed')
                                        );
                                      }
                                    })();
                                  }}
                                >
                                  {t('chunkDeleteSubChunk')}
                                </button>
                              </div>
                            )}
                        </li>
                        );
                      })}
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
        .chunk-stats__global-actions {
          margin: -0.25rem 0 0.55rem 0;
        }
        .chunk-stats__rebuild-combined {
          font-size: 0.75rem;
          padding: 0.25rem 0.55rem;
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
        .chunk-stats__cancelled-actions {
          display: flex;
          flex-wrap: wrap;
          gap: 0.35rem;
          margin-bottom: 0.4rem;
        }
        .chunk-stats__cancelled-actions button {
          font-size: 0.75rem;
          padding: 0.2rem 0.5rem;
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
          max-width: min(100%, 1200px);
        }
        .chunk-stats__vm--sub {
          margin-top: 0.3rem;
          margin-bottom: 0.4rem;
        }
        .chunk-stats__vm-body {
          display: grid;
          grid-template-columns: minmax(0, 220px) auto minmax(0, 1fr);
          align-items: start;
          gap: 0.5rem 0.65rem;
        }
        @media (max-width: 640px) {
          .chunk-stats__vm-body {
            grid-template-columns: 1fr;
          }
        }
        .chunk-stats__vm-telemetry-col {
          min-width: 0;
        }
        .chunk-stats__vm-telemetry-col .chunk-stats__vm-err {
          white-space: normal;
          word-break: break-word;
        }
        .chunk-stats__vm-log-col {
          min-width: 0;
          border-left: 1px solid var(--color-border);
          padding-left: 0.5rem;
          margin-left: -0.15rem;
        }
        @media (max-width: 640px) {
          .chunk-stats__vm-log-col {
            border-left: none;
            padding-left: 0;
            margin-left: 0;
            border-top: 1px solid var(--color-border);
            padding-top: 0.45rem;
          }
          .chunk-stats__vm-actions-col {
            border-top: 1px solid var(--color-border);
            padding-top: 0.45rem;
          }
        }
        .chunk-stats__vm-log-header {
          font-size: 0.7rem;
          font-weight: 600;
          color: var(--color-label);
          margin-bottom: 0.25rem;
        }
        .chunk-stats__vm-log-body {
          margin: 0;
          max-height: 9rem;
          overflow: auto;
          display: flex;
          flex-direction: column;
          gap: 0.35rem;
          font-size: 0.68rem;
          line-height: 1.35;
          font-family: ui-monospace, monospace;
          color: var(--color-text-secondary);
        }
        .chunk-stats__vm-log-line {
          margin: 0;
          padding: 0.22rem 0.4rem;
          border-radius: 4px;
          background: color-mix(in srgb, var(--color-surface) 55%, transparent);
          border: 1px solid color-mix(in srgb, var(--color-border) 70%, transparent);
          white-space: pre-wrap;
          word-break: break-word;
        }
        .chunk-stats__vm-log-body--empty {
          font-family: inherit;
          font-size: 0.72rem;
          padding: 0.15rem 0;
        }
        .chunk-stats__vm-actions-col {
          min-width: 0;
        }
        .chunk-stats__vm-actions-stack {
          flex-direction: column;
          align-items: flex-start;
          margin-top: 0;
          gap: 0.35rem;
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
        .chunk-stats__media-row {
          display: flex;
          flex-wrap: wrap;
          align-items: flex-start;
          gap: 0.65rem 1rem;
          margin-top: 0.35rem;
        }
        .chunk-stats__media-row--sub {
          margin-top: 0.3rem;
        }
        .chunk-stats__media-row__audio {
          flex: 1 1 180px;
          min-width: 0;
          max-width: 1100px;
        }
        .chunk-stats__media-row__text {
          flex: 1 1 180px;
          min-width: 0;
          max-width: 600px;
        }
        .chunk-stats__media-row__empty {
          margin: 0;
          padding: 0.3rem 0;
        }
        .chunk-stats__block { margin-top: 0.35rem; }
        .chunk-stats__block--sub {
          margin-top: 0.5rem;
          padding-top: 0.35rem;
          border-top: 1px dashed var(--color-border);
        }
        .chunk-stats__sub-list {
          list-style: none;
          margin: 0;
          padding: 0;
          display: flex;
          flex-direction: column;
          gap: 0.45rem;
        }
        .chunk-stats__sub-card {
          margin-left: 50px;
          border: 1px solid var(--color-border);
          border-radius: 6px;
          padding: 0.4rem 0.5rem;
          background: color-mix(in srgb, var(--color-subtle-panel) 65%, var(--color-surface));
        }
        .chunk-stats__sub-head {
          margin-bottom: 0.25rem;
        }
        .chunk-stats__sub-label {
          font-size: 0.78rem;
          font-weight: 600;
          font-variant-numeric: tabular-nums;
          color: var(--color-heading);
        }
        .chunk-stats__sub-actions {
          margin: 0.35rem 0 0.25rem 0;
        }
        .chunk-stats__sub-actions button {
          padding: 0.3rem 0.55rem;
          font-size: 0.75rem;
          border: 1px solid var(--color-border-strong);
          border-radius: 4px;
          background: var(--color-surface);
          cursor: pointer;
          color: var(--color-heading);
        }
        .chunk-stats__sub-actions button:hover:not(:disabled) {
          background: var(--color-surface-hover);
        }
        .chunk-stats__sub-actions button:disabled {
          opacity: 0.5;
          cursor: not-allowed;
        }
        .chunk-stats__sub-delete-wrap {
          margin-top: 0.4rem;
          padding-top: 0.35rem;
          border-top: 1px dashed color-mix(in srgb, var(--color-border) 80%, transparent);
        }
        .chunk-stats__sub-delete-btn {
          padding: 0.28rem 0.5rem;
          font-size: 0.72rem;
          border: 1px solid var(--color-border-strong);
          border-radius: 4px;
          background: var(--color-surface);
          cursor: pointer;
          color: var(--color-label);
        }
        .chunk-stats__sub-delete-btn:hover:not(:disabled) {
          background: var(--color-surface-hover);
        }
        .chunk-stats__sub-delete-btn:disabled {
          opacity: 0.5;
          cursor: not-allowed;
        }
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
