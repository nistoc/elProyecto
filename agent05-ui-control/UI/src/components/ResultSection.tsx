import type { JobSnapshot, JobFileInfo } from '../types';

interface ResultSectionProps {
  jobId: string;
  job: JobSnapshot | null;
  t: (key: string) => string;
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(2)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

function formatDuration(seconds: number): string {
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${m}:${s.toString().padStart(2, '0')}`;
}

export function ResultSection({ jobId, job, t }: ResultSectionProps) {
  const copyFilename = () => {
    if (job?.originalFilename) {
      navigator.clipboard.writeText(job.originalFilename);
    }
  };

  return (
    <div className="result-section">
      <h3 className="result-section__title">Result</h3>
      <dl className="result-section__meta">
        <dt>Job ID</dt>
        <dd>{jobId}</dd>
        <dt>{t('filename')}</dt>
        <dd>
          {job?.originalFilename ?? '—'}
          {job?.originalFilename && (
            <button
              type="button"
              onClick={copyFilename}
              className="result-section__copy"
            >
              {t('copyFilename')}
            </button>
          )}
        </dd>
        <dt>{t('status')}</dt>
        <dd>{job?.status ?? '—'}</dd>
        <dt>{t('phase')}</dt>
        <dd>{job?.phase ?? '—'}</dd>
        {job?.jobDirectoryPath && (
          <>
            <dt>{t('jobDirectoryPath')}</dt>
            <dd className="result-section__path">{job.jobDirectoryPath}</dd>
          </>
        )}
      </dl>
      {job?.files && job.files.length > 0 && (
        <div className="result-section__files">
          <h4 className="result-section__files-title">{t('projectFiles')}</h4>
          <ul className="result-section__files-list">
            {job.files.map((f: JobFileInfo) => (
              <li key={f.name} className="result-section__file">
                <span className="result-section__file-name">{f.name}</span>
                <span className="result-section__file-meta">
                  {formatSize(f.sizeBytes)}
                  {f.kind === 'text' && f.lineCount != null && ` · ${f.lineCount} lines`}
                  {f.kind === 'audio' && f.durationSeconds != null && ` · ${formatDuration(f.durationSeconds)}`}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
      {job?.result && (
        <div className="result-section__links">
          {job.result.transcript && (
            <p>
              <strong>Transcript:</strong> available in workspace
            </p>
          )}
          {job.result.transcriptFixed && (
            <p>
              <strong>Fixed transcript:</strong> available in workspace
            </p>
          )}
        </div>
      )}
      <style>{`
        .result-section { padding: 1rem; }
        .result-section__title { margin: 0 0 0.5rem 0; font-size: 1rem; }
        .result-section__meta { display: grid; grid-template-columns: auto 1fr; gap: 0.25rem 1rem; font-size: 0.875rem; }
        .result-section__meta dt { color: #64748b; }
        .result-section__meta dd { margin: 0; }
        .result-section__path { word-break: break-all; font-size: 0.8rem; color: #64748b; }
        .result-section__copy { margin-left: 0.5rem; font-size: 0.75rem; padding: 0.125rem 0.25rem; }
        .result-section__files { margin-top: 1rem; }
        .result-section__files-title { margin: 0 0 0.5rem 0; font-size: 0.9rem; }
        .result-section__files-list { list-style: none; margin: 0; padding: 0; }
        .result-section__file { display: flex; justify-content: space-between; align-items: baseline; gap: 0.5rem; padding: 0.25rem 0; font-size: 0.875rem; border-bottom: 1px solid #e2e8f0; }
        .result-section__file-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .result-section__file-meta { flex-shrink: 0; color: #64748b; font-size: 0.8rem; }
        .result-section__links { margin-top: 1rem; font-size: 0.875rem; }
      `}</style>
    </div>
  );
}
