import { useState } from 'react';
import { jobProjectFileContentUrl, postJobChunkAction } from '../api';
import type { JobProjectFiles, JobSnapshot } from '../types';
import { buildChunkGroups } from '../utils/chunkArtifactGroups';

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(2)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

function labelForChunkState(state: string, t: (key: string) => string): string {
  const key = `chunkState${state}`;
  const v = t(key);
  return v === key ? state : v;
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
  t: (key: string) => string;
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
  t,
}: ChunkControlsStatsProps) {
  const [busyIdx, setBusyIdx] = useState<number | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const transcriberRunning =
    job.phase === 'transcriber' && job.status === 'running';
  const fileData = files ?? emptyFiles;
  const groups = buildChunkGroups(job, fileData);
  const showList = groups.length > 0;

  const runCancel = async (chunkIndex: number) => {
    setMessage(null);
    setBusyIdx(chunkIndex);
    try {
      const res = await postJobChunkAction(jobId, 'cancel', chunkIndex);
      setMessage(
        res.ok
          ? res.message || t('chunkActionOk')
          : `${t('chunkActionRejected')}: ${res.message}`
      );
    } catch (e) {
      setMessage(e instanceof Error ? e.message : t('chunkActionFailed'));
    } finally {
      setBusyIdx(null);
    }
  };

  return (
    <section
      className="chunk-stats"
      key={`${jobId}-${filesRefreshKey}`}
      aria-label={t('chunkStatsTitle')}
    >
      <h4 className="chunk-stats__title">{t('chunkStatsTitle')}</h4>
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
            const stateLabel = vm
              ? labelForChunkState(vm.state, t)
              : t('chunkStatePending');
            const running =
              transcriberRunning && vm?.state === 'Running';
            return (
              <li key={g.index} className="chunk-stats__card">
                <div className="chunk-stats__card-head">
                  <span className="chunk-stats__chunk-label">
                    {t('chunkStatsChunkPrefix')} #{g.index}
                  </span>
                  <span className="chunk-stats__state" title={vm?.errorMessage ?? ''}>
                    {t('chunkStatsVmStatus')}: {stateLabel}
                    {vm?.errorMessage?.trim() ? ` — ${vm.errorMessage}` : ''}
                  </span>
                  {running && (
                    <div className="chunk-stats__actions">
                      <span className="chunk-stats__inflight">
                        {t('chunkStatsTranscriptionActive')}
                      </span>
                      <button
                        type="button"
                        className="chunk-stats__cancel"
                        disabled={busyIdx === g.index}
                        onClick={() => void runCancel(g.index)}
                      >
                        {t('chunkStatsCancelChunk')}
                      </button>
                    </div>
                  )}
                </div>
                {g.audioFiles.length > 0 && (
                  <div className="chunk-stats__block">
                    <div className="chunk-stats__block-title">
                      {t('chunkStatsAudio')}
                    </div>
                    <ul className="chunk-stats__files">
                      {g.audioFiles.map((f) => (
                        <li key={f.relativePath}>
                          <a
                            href={jobProjectFileContentUrl(jobId, f.relativePath)}
                            target="_blank"
                            rel="noreferrer"
                            className="chunk-stats__link"
                          >
                            {f.name}
                          </a>
                          <span className="chunk-stats__meta">
                            {formatSize(f.sizeBytes)}
                          </span>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}
                {g.jsonFiles.length > 0 && (
                  <div className="chunk-stats__block">
                    <div className="chunk-stats__block-title">
                      {t('chunkStatsJson')}
                    </div>
                    <ul className="chunk-stats__files">
                      {g.jsonFiles.map((f) => (
                        <li key={f.relativePath}>
                          <a
                            href={jobProjectFileContentUrl(jobId, f.relativePath)}
                            target="_blank"
                            rel="noreferrer"
                            className="chunk-stats__link"
                          >
                            {f.name}
                          </a>
                          <span className="chunk-stats__meta">
                            {formatSize(f.sizeBytes)}
                          </span>
                        </li>
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
          gap: 0.5rem 0.75rem;
          margin-bottom: 0.35rem;
        }
        .chunk-stats__chunk-label {
          font-weight: 600;
          font-variant-numeric: tabular-nums;
          color: var(--color-heading);
        }
        .chunk-stats__state {
          font-size: 0.75rem;
          color: var(--color-label);
          flex: 1;
          min-width: 8rem;
        }
        .chunk-stats__actions {
          display: flex;
          flex-wrap: wrap;
          align-items: center;
          gap: 0.35rem;
          margin-left: auto;
        }
        .chunk-stats__inflight {
          font-size: 0.7rem;
          color: var(--color-info);
          max-width: 14rem;
        }
        .chunk-stats__cancel {
          padding: 0.2rem 0.45rem;
          font-size: 0.75rem;
          border-radius: 4px;
          border: 1px solid var(--color-border-strong);
          background: var(--color-surface-raised);
          color: var(--color-danger, #c62828);
          cursor: pointer;
        }
        .chunk-stats__cancel:disabled {
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
        .chunk-stats__files {
          list-style: none;
          margin: 0;
          padding: 0;
        }
        .chunk-stats__files li {
          display: flex;
          flex-wrap: wrap;
          align-items: baseline;
          gap: 0.35rem;
          font-size: 0.78rem;
          padding: 0.15rem 0;
        }
        .chunk-stats__link {
          color: var(--color-link);
          word-break: break-all;
        }
        .chunk-stats__meta {
          font-size: 0.72rem;
          color: var(--color-text-secondary);
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
