import { useCallback, useEffect, useMemo, useState } from 'react';
import type { JobSnapshot, JobProjectFiles } from '../types';
import { fetchJobFiles, jobProjectFileContentUrl } from '../api';
import { ProjectFilesView } from './ProjectFilesPanel';

export type ResultSectionVariant = 'result' | 'refiner';

interface ResultSectionProps {
  jobId: string;
  job: JobSnapshot | null;
  t: (key: string) => string;
  /** Refiner tab uses the same metadata, quick links, and transcript list as Result. */
  variant?: ResultSectionVariant;
  /** Refiner: start refiner for the transcript row the user clicked. */
  onRefineFromTranscript?: (relativePath: string) => void;
  refinerActionBusy?: boolean;
}

function findByName(files: { name: string; relativePath: string }[], name: string) {
  return files.find((f) => f.name === name);
}

/** Matches `transcript_fixed_1.md`, `transcript_fixed_12.md`, etc. (case-insensitive). */
const TRANSCRIPT_FIXED_VARIANT_RE = /^transcript_fixed_(\d+)\.md$/i;

function transcriptFixedVariantSortKey(name: string): number {
  const m = name.match(TRANSCRIPT_FIXED_VARIANT_RE);
  return m ? parseInt(m[1], 10) : 0;
}

function hasAnyTranscriptFixedOnDisk(
  transcripts: { name: string; relativePath?: string }[] | undefined
): boolean {
  if (!transcripts?.length) return false;
  if (transcripts.some((f) => f.name === 'transcript_fixed.md')) return true;
  return transcripts.some((f) => TRANSCRIPT_FIXED_VARIANT_RE.test(f.name));
}

function buildKeyLinks(
  transcripts: { name: string; relativePath: string }[],
  t: (key: string) => string
): { label: string; path: string }[] {
  const entries: { label: string; path: string }[] = [];
  const tr = findByName(transcripts, 'transcript.md');
  if (tr) entries.push({ label: t('linkTranscript'), path: tr.relativePath });

  const tf = findByName(transcripts, 'transcript_fixed.md');
  if (tf) entries.push({ label: t('linkTranscriptFixed'), path: tf.relativePath });

  const numbered = transcripts
    .filter((f) => TRANSCRIPT_FIXED_VARIANT_RE.test(f.name))
    .sort(
      (a, b) =>
        transcriptFixedVariantSortKey(a.name) - transcriptFixedVariantSortKey(b.name)
    );
  for (const f of numbered) {
    const n = transcriptFixedVariantSortKey(f.name);
    entries.push({
      label: `${t('linkTranscriptFixed')} (#${n})`,
      path: f.relativePath,
    });
  }

  const rj = findByName(transcripts, 'response.json');
  if (rj) entries.push({ label: t('linkResponseJson'), path: rj.relativePath });
  return entries;
}

export function ResultSection({
  jobId,
  job,
  t,
  variant = 'result',
  onRefineFromTranscript,
  refinerActionBusy,
}: ResultSectionProps) {
  const copyFilename = () => {
    if (job?.originalFilename) {
      navigator.clipboard.writeText(job.originalFilename);
    }
  };

  const [projectFiles, setProjectFiles] = useState<JobProjectFiles | null>(null);
  const [jobDirFromFilesApi, setJobDirFromFilesApi] = useState<string | null>(
    null
  );
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
        setJobDirFromFilesApi(
          res && typeof res.jobDir === 'string' && res.jobDir.length > 0
            ? res.jobDir
            : null
        );
      })
      .catch((e) => {
        if (!cancelled) {
          setFilesErr(e instanceof Error ? e.message : t('failedToLoadFiles'));
          setProjectFiles(null);
          setJobDirFromFilesApi(null);
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
    return buildKeyLinks(projectFiles.transcripts, t);
  }, [projectFiles, t]);

  const refinerStep = variant === 'refiner';
  const refineHandlerAvailable =
    refinerStep && typeof onRefineFromTranscript === 'function';

  const displayJobDir =
    job?.jobDirectoryPath?.trim() || jobDirFromFilesApi || null;

  return (
    <div
      className={
        variant === 'refiner'
          ? 'result-section result-section--refiner'
          : 'result-section'
      }
    >
      <h3 className="result-section__title">
        {variant === 'refiner' ? t('refiner') : t('result')}
      </h3>
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
        {displayJobDir && (
          <>
            <dt>{t('jobDirectoryPath')}</dt>
            <dd className="result-section__path">{displayJobDir}</dd>
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
          refineTargetRelativePath={null}
          refineShowOnEachEligibleRow={refineHandlerAvailable}
          onRefineTranscript={
            refineHandlerAvailable ? onRefineFromTranscript : undefined
          }
          refinerActionBusy={refineHandlerAvailable ? refinerActionBusy : undefined}
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
          {job.result.transcriptFixed &&
            !hasAnyTranscriptFixedOnDisk(projectFiles?.transcripts) && (
            <p>
              <strong>{t('linkTranscriptFixed')}:</strong> {t('inWorkspace')}
            </p>
          )}
        </div>
      )}
      <style>{`
        .result-section { color: var(--color-text); }
        .result-section__title { margin: 0 0 0.5rem 0; font-size: 1rem; color: var(--color-text); }
        .result-section__meta { display: grid; grid-template-columns: auto 1fr; gap: 0.25rem 1rem; font-size: 0.875rem; }
        .result-section__meta dt { color: var(--color-text-secondary); }
        .result-section__meta dd { margin: 0; }
        .result-section__path { word-break: break-all; font-size: 0.8rem; color: var(--color-text-secondary); }
        .result-section__copy { margin-left: 0.5rem; font-size: 0.75rem; padding: 0.125rem 0.25rem; border-radius: 4px; border: 1px solid var(--color-border-strong); background: var(--color-surface); color: var(--color-text); cursor: pointer; }
        .result-section__files-title { margin: 0.75rem 0 0.5rem 0; font-size: 0.9rem; color: var(--color-heading); }
        .result-section__links { margin-top: 1rem; font-size: 0.875rem; }
        .result-section__key-links { margin-top: 1rem; }
        .result-section__links-list { margin: 0; padding-left: 1.25rem; font-size: 0.875rem; }
        .result-section__links-list a { color: var(--color-link); }
        .result-section__hint { margin: 0.5rem 0; font-size: 0.875rem; color: var(--color-text-secondary); }
        .result-section__hint--err { color: var(--color-error-muted); }
        .result-section--refiner .pf-list {
          max-height: 20vh;
          overflow: auto;
          padding: 0.5rem 0.6rem;
          border: 1px solid var(--color-border-strong);
          border-radius: 6px;
          background: var(--color-surface);
        }
      `}</style>
    </div>
  );
}
