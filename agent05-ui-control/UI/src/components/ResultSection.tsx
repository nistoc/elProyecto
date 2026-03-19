import { useCallback, useEffect, useMemo, useState } from 'react';
import type { JobSnapshot, JobProjectFiles } from '../types';
import { fetchJobFiles, jobProjectFileContentUrl } from '../api';
import { ProjectFilesView } from './ProjectFilesPanel';

interface ResultSectionProps {
  jobId: string;
  job: JobSnapshot | null;
  t: (key: string) => string;
}

function findByName(files: { name: string; relativePath: string }[], name: string) {
  return files.find((f) => f.name === name);
}

export function ResultSection({ jobId, job, t }: ResultSectionProps) {
  const copyFilename = () => {
    if (job?.originalFilename) {
      navigator.clipboard.writeText(job.originalFilename);
    }
  };

  const [projectFiles, setProjectFiles] = useState<JobProjectFiles | null>(null);
  const [filesErr, setFilesErr] = useState<string | null>(null);
  const [filesLoading, setFilesLoading] = useState(true);
  const [filesReloadKey, setFilesReloadKey] = useState(0);

  const reloadProjectFiles = useCallback(() => {
    setFilesReloadKey((k) => k + 1);
  }, []);

  useEffect(() => {
    let cancelled = false;
    setFilesLoading(true);
    setFilesErr(null);
    fetchJobFiles(jobId)
      .then((res) => {
        if (cancelled) return;
        setProjectFiles(res?.files ?? null);
      })
      .catch((e) => {
        if (!cancelled) {
          setFilesErr(e instanceof Error ? e.message : t('failedToLoadFiles'));
          setProjectFiles(null);
        }
      })
      .finally(() => {
        if (!cancelled) setFilesLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [jobId, filesReloadKey, t]);

  const keyLinks = useMemo(() => {
    if (!projectFiles) return [];
    const transcripts = projectFiles.transcripts;
    const entries: { label: string; path: string }[] = [];
    const tr = findByName(transcripts, 'transcript.md');
    if (tr) entries.push({ label: t('linkTranscript'), path: tr.relativePath });
    const tf = findByName(transcripts, 'transcript_fixed.md');
    if (tf) entries.push({ label: t('linkTranscriptFixed'), path: tf.relativePath });
    const rj = findByName(transcripts, 'response.json');
    if (rj) entries.push({ label: t('linkResponseJson'), path: rj.relativePath });
    return entries;
  }, [projectFiles, t]);

  return (
    <div className="result-section">
      <h3 className="result-section__title">{t('result')}</h3>
      <dl className="result-section__meta">
        <dt>{t('jobId')}</dt>
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

      {keyLinks.length > 0 && (
        <div className="result-section__key-links">
          <h4 className="result-section__files-title">{t('resultQuickLinks')}</h4>
          <ul className="result-section__links-list">
            {keyLinks.map(({ label, path }) => (
              <li key={path}>
                <a
                  href={jobProjectFileContentUrl(jobId, path)}
                  target="_blank"
                  rel="noreferrer"
                >
                  {label}
                </a>
              </li>
            ))}
          </ul>
        </div>
      )}

      {filesLoading && (
        <p className="result-section__hint">{t('loadingFiles')}</p>
      )}
      {filesErr && (
        <p className="result-section__hint result-section__hint--err">{filesErr}</p>
      )}
      {projectFiles && (
        <ProjectFilesView
          jobId={jobId}
          data={projectFiles}
          mode="transcripts"
          t={t}
          onFilesMutated={reloadProjectFiles}
        />
      )}

      {!projectFiles && !filesLoading && !filesErr && (
        <p className="result-section__hint">{t('noProjectFiles')}</p>
      )}

      {job?.result && (
        <div className="result-section__links">
          {job.result.transcript && !findByName(projectFiles?.transcripts ?? [], 'transcript.md') && (
            <p>
              <strong>{t('linkTranscript')}:</strong> {t('inWorkspace')}
            </p>
          )}
          {job.result.transcriptFixed && !findByName(projectFiles?.transcripts ?? [], 'transcript_fixed.md') && (
            <p>
              <strong>{t('linkTranscriptFixed')}:</strong> {t('inWorkspace')}
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
        .result-section__files-title { margin: 0.75rem 0 0.5rem 0; font-size: 0.9rem; }
        .result-section__links { margin-top: 1rem; font-size: 0.875rem; }
        .result-section__key-links { margin-top: 1rem; }
        .result-section__links-list { margin: 0; padding-left: 1.25rem; font-size: 0.875rem; }
        .result-section__links-list a { color: #2563eb; }
        .result-section__hint { margin: 0.5rem 0; font-size: 0.875rem; color: #64748b; }
        .result-section__hint--err { color: #b91c1c; }
      `}</style>
    </div>
  );
}
