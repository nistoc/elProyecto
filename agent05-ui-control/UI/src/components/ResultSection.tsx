import type { JobSnapshot } from '../types';

interface ResultSectionProps {
  jobId: string;
  job: JobSnapshot | null;
  t: (key: string) => string;
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
      </dl>
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
        .result-section__copy { margin-left: 0.5rem; font-size: 0.75rem; padding: 0.125rem 0.25rem; }
        .result-section__links { margin-top: 1rem; font-size: 0.875rem; }
      `}</style>
    </div>
  );
}
