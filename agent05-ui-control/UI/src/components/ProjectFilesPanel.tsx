import { useEffect, useRef, useState } from 'react';
import {
  fetchJobFiles,
  jobProjectFileContentUrl,
  fetchJobFileText,
  putJobFileContent,
} from '../api';
import type { JobProjectFilesState } from '../hooks/useJobProjectFiles';
import type { JobProjectFile, JobProjectFiles } from '../types';

export type ProjectFilesMode = 'full' | 'transcripts';

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

function filterProjectFilesByChunk(
  data: JobProjectFiles,
  chunkIndex: number
): JobProjectFiles {
  const by = (files: JobProjectFile[]) =>
    files.filter(
      (f) => f.index === chunkIndex || f.parentIndex === chunkIndex
    );
  return {
    ...data,
    chunks: by(data.chunks),
    chunkJson: by(data.chunkJson),
    transcripts: by(data.transcripts),
    intermediate: by(data.intermediate),
    converted: by(data.converted),
    splitChunks: by(data.splitChunks),
  };
}

/** Section title with chunk count or 1-based index range when all rows have `index`. */
function indexedSectionTitle(
  items: JobProjectFile[],
  baseTitle: string
): string {
  if (!items.length) return baseTitle;
  const count = items.length;
  const idxs = items
    .map((x) => x.index)
    .filter((x): x is number => x != null);
  if (idxs.length === count) {
    const mn = Math.min(...idxs);
    const mx = Math.max(...idxs);
    if (mn === mx) return `${baseTitle} [${mn + 1}]`;
    return `${baseTitle} [${mn + 1}–${mx + 1}]`;
  }
  return `${baseTitle} [${count}]`;
}

function fileMeta(f: JobProjectFile, t: (key: string) => string): string {
  const parts: string[] = [formatSize(f.sizeBytes)];
  if (f.kind === 'text' && f.lineCount != null)
    parts.push(`${f.lineCount} ${t('lines')}`);
  if (f.kind === 'audio' && f.durationSeconds != null)
    parts.push(formatDuration(f.durationSeconds));
  if (f.index != null) parts.push(`#${f.index}`);
  if (f.parentIndex != null || f.subIndex != null) {
    parts.push(`p${f.parentIndex ?? '?'}/s${f.subIndex ?? '?'}`);
  }
  return parts.join(' · ');
}

export function TextFileEditorModal({
  jobId,
  file,
  onClose,
  onSaved,
  t,
}: {
  jobId: string;
  file: { relativePath: string; name: string } | null;
  onClose: () => void;
  onSaved: () => void;
  t: (key: string) => string;
}) {
  const [text, setText] = useState('');
  const [loadErr, setLoadErr] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saveErr, setSaveErr] = useState<string | null>(null);

  const relPath = file?.relativePath;
  useEffect(() => {
    if (!relPath) return;
    let cancelled = false;
    setLoadErr(null);
    setSaveErr(null);
    setText('');
    fetchJobFileText(jobId, relPath)
      .then((body) => {
        if (!cancelled) setText(body);
      })
      .catch((e) => {
        if (!cancelled)
          setLoadErr(
            e instanceof Error ? e.message : t('loadEditorFailed')
          );
      });
    return () => {
      cancelled = true;
    };
  }, [jobId, relPath, t]);

  useEffect(() => {
    if (!file) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [file, onClose]);

  if (!file) return null;

  const handleSave = async () => {
    setSaveErr(null);
    setSaving(true);
    try {
      await putJobFileContent(jobId, file.relativePath, text);
      onSaved();
      onClose();
    } catch (e) {
      setSaveErr(e instanceof Error ? e.message : t('saveFailed'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="pf-modal-overlay" role="presentation" onClick={onClose}>
      <div
        className="pf-modal"
        role="dialog"
        aria-labelledby="pf-editor-title"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 id="pf-editor-title" className="pf-modal__title">
          {t('editorTitle')}: {file.name}
        </h3>
        {loadErr && <p className="pf-status pf-status--err">{loadErr}</p>}
        {!loadErr && (
          <textarea
            className="pf-textarea"
            value={text}
            onChange={(e) => setText(e.target.value)}
            spellCheck={false}
          />
        )}
        {saveErr && <p className="pf-status pf-status--err">{saveErr}</p>}
        <div className="pf-modal__actions">
          <button type="button" onClick={onClose}>
            {t('cancelEditor')}
          </button>
          <button
            type="button"
            disabled={saving || !!loadErr}
            onClick={handleSave}
          >
            {saving ? t('saving') : t('saveFile')}
          </button>
        </div>
      </div>
    </div>
  );
}

export function FileRow({
  jobId,
  f,
  t,
  onEditText,
}: {
  jobId: string;
  f: JobProjectFile;
  t: (key: string) => string;
  onEditText?: (f: JobProjectFile) => void;
}) {
  const url = jobProjectFileContentUrl(jobId, f.relativePath);
  return (
    <li className="pf-file">
      <div
        className="pf-file-main"
        title={
          f.kind === 'text' && onEditText
            ? `${f.relativePath}\n${t('doubleClickToEdit')}`
            : f.relativePath
        }
        onDoubleClick={(e) => {
          if (f.kind !== 'text' || !onEditText) return;
          e.preventDefault();
          onEditText(f);
        }}
      >
        <span className="pf-file-name" title={f.relativePath}>
          {f.name}
        </span>
        <span className="pf-file-meta">{fileMeta(f, t)}</span>
      </div>
      <div className="pf-file-actions">
        {f.kind === 'audio' && (
          <audio
            className="pf-audio"
            src={url}
            controls
            preload="none"
          />
        )}
        <a href={url} target="_blank" rel="noreferrer" className="pf-open">
          {t('openFile')}
        </a>
        {f.kind === 'text' && onEditText && (
          <button
            type="button"
            className="pf-edit"
            onClick={() => onEditText(f)}
          >
            {t('editFile')}
          </button>
        )}
      </div>
    </li>
  );
}

function Section({
  title,
  jobId,
  items,
  t,
  onEditText,
}: {
  title: string;
  jobId: string;
  items: JobProjectFile[];
  t: (key: string) => string;
  onEditText?: (f: JobProjectFile) => void;
}) {
  if (!items.length) return null;
  return (
    <section className="pf-section">
      <h4 className="pf-section__title">{title}</h4>
      <ul className="pf-list">
        {items.map((f) => (
          <FileRow
            key={`${f.relativePath}`}
            jobId={jobId}
            f={f}
            t={t}
            onEditText={onEditText}
          />
        ))}
      </ul>
    </section>
  );
}

interface ProjectFilesViewProps {
  jobId: string;
  data: JobProjectFiles;
  mode: ProjectFilesMode;
  t: (key: string) => string;
  /** When set, chunk-related file sections are narrowed to this 0-based chunk index. */
  chunkIndexFilter?: number | null;
  /** Called after a text file is saved (refresh lists / line counts). */
  onFilesMutated?: () => void;
  /** Hide chunks + chunk JSON (shown in Chunk controls Stats). */
  hideChunkSections?: boolean;
}

/** Renders structured file lists (no fetch). */
export function ProjectFilesView({
  jobId,
  data,
  mode,
  t,
  chunkIndexFilter = null,
  onFilesMutated,
  hideChunkSections = false,
}: ProjectFilesViewProps) {
  const [editTarget, setEditTarget] = useState<{
    relativePath: string;
    name: string;
  } | null>(null);

  const viewData =
    chunkIndexFilter != null
      ? filterProjectFilesByChunk(data, chunkIndexFilter)
      : data;

  const openEditor = (f: JobProjectFile) =>
    setEditTarget({ relativePath: f.relativePath, name: f.name });

  const splitTranscripts = viewData.splitChunks.filter((x) => x.isTranscript);

  const body =
    mode === 'transcripts' ? (
      <>
        <Section
          title={t('sectionTranscripts')}
          jobId={jobId}
          items={viewData.transcripts}
          t={t}
          onEditText={openEditor}
        />
        <Section
          title={t('sectionSplitTranscripts')}
          jobId={jobId}
          items={splitTranscripts}
          t={t}
          onEditText={openEditor}
        />
        {!viewData.transcripts.length && !splitTranscripts.length && (
          <p className="pf-status">{t('noTranscriptFiles')}</p>
        )}
      </>
    ) : (
      <>
        <Section
          title={t('sectionOriginal')}
          jobId={jobId}
          items={data.original}
          t={t}
          onEditText={openEditor}
        />
        <Section
          title={t('sectionTranscripts')}
          jobId={jobId}
          items={viewData.transcripts}
          t={t}
          onEditText={openEditor}
        />
        {!hideChunkSections && (
          <>
            <Section
              title={indexedSectionTitle(viewData.chunks, t('sectionChunks'))}
              jobId={jobId}
              items={viewData.chunks}
              t={t}
              onEditText={openEditor}
            />
            <Section
              title={indexedSectionTitle(
                viewData.chunkJson,
                t('sectionChunkJson')
              )}
              jobId={jobId}
              items={viewData.chunkJson}
              t={t}
              onEditText={openEditor}
            />
          </>
        )}
        <Section
          title={t('sectionIntermediate')}
          jobId={jobId}
          items={viewData.intermediate}
          t={t}
          onEditText={openEditor}
        />
        <Section
          title={t('sectionConverted')}
          jobId={jobId}
          items={viewData.converted}
          t={t}
          onEditText={openEditor}
        />
        <Section
          title={t('sectionSplitChunks')}
          jobId={jobId}
          items={viewData.splitChunks}
          t={t}
          onEditText={openEditor}
        />
      </>
    );

  return (
    <div className="project-files">
      {body}
      {editTarget && (
        <TextFileEditorModal
          jobId={jobId}
          file={editTarget}
          onClose={() => setEditTarget(null)}
          onSaved={() => onFilesMutated?.()}
          t={t}
        />
      )}
      <style>{styles}</style>
    </div>
  );
}

interface ProjectFilesPanelProps {
  jobId: string;
  mode: ProjectFilesMode;
  t: (key: string) => string;
  chunkIndexFilter?: number | null;
  /** When this changes (e.g. SSE job snapshot), refetch GET .../files so new disk artifacts appear without switching tabs. */
  filesRefreshKey?: number;
  /** When set, file list state comes from parent (shared with Chunk controls Stats). */
  managedFiles?: JobProjectFilesState;
  hideChunkSections?: boolean;
}

export function ProjectFilesPanel({
  jobId,
  mode,
  t,
  chunkIndexFilter = null,
  filesRefreshKey = 0,
  managedFiles,
  hideChunkSections = false,
}: ProjectFilesPanelProps) {
  const [data, setData] = useState<JobProjectFiles | null>(null);
  const [jobDir, setJobDir] = useState<string | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const prevJobIdRef = useRef<string | null>(null);

  useEffect(() => {
    if (managedFiles != null) return;

    let cancelled = false;
    const jobChanged = prevJobIdRef.current !== jobId;
    prevJobIdRef.current = jobId;

    if (jobChanged) {
      setLoading(true);
      setData(null);
      setJobDir(null);
    } else {
      setRefreshing(true);
    }
    setErr(null);

    fetchJobFiles(jobId)
      .then((res) => {
        if (cancelled) return;
        if (!res) {
          setData(null);
          setJobDir(null);
          return;
        }
        setData(res.files);
        setJobDir(typeof res.jobDir === 'string' ? res.jobDir : null);
      })
      .catch((e) => {
        if (!cancelled) {
          setErr(e instanceof Error ? e.message : t('failedToLoadFiles'));
          setData(null);
          setJobDir(null);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
          setRefreshing(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [jobId, reloadKey, filesRefreshKey, t]);

  const effectiveData = managedFiles?.data ?? data;
  const effectiveJobDir = managedFiles?.jobDir ?? jobDir;
  const effectiveErr = managedFiles?.error ?? err;
  const effectiveLoading = managedFiles?.loading ?? loading;
  const effectiveRefreshing = managedFiles?.refreshing ?? refreshing;
  const reloadFiles = managedFiles?.reload ?? (() => setReloadKey((k) => k + 1));

  const showFullSpinner = effectiveLoading && !effectiveData && !effectiveErr;

  const fullModeToolbar =
    mode === 'full' ? (
      <div className="pf-panel-toolbar">
        {effectiveJobDir && effectiveJobDir.length > 0 ? (
          <p className="pf-jobdir">
            <span className="pf-jobdir__label">{t('jobDirectoryPath')}:</span>{' '}
            <code className="pf-jobdir__path">{effectiveJobDir}</code>
          </p>
        ) : (
          <div className="pf-jobdir-spacer" aria-hidden />
        )}
        <div className="pf-toolbar-actions">
          {effectiveRefreshing && (
            <span className="pf-refresh-hint">{t('loadingFiles')}</span>
          )}
          <button
            type="button"
            className="pf-refresh-files"
            onClick={() => reloadFiles()}
            disabled={effectiveRefreshing}
          >
            {t('refresh')}
          </button>
        </div>
      </div>
    ) : null;

  if (showFullSpinner) {
    return (
      <>
        {fullModeToolbar}
        <p className="pf-status">{t('loadingFiles')}</p>
        <style>{styles}</style>
      </>
    );
  }
  if (effectiveErr) {
    return (
      <>
        {fullModeToolbar}
        <p className="pf-status pf-status--err">{effectiveErr}</p>
        <style>{styles}</style>
      </>
    );
  }
  if (!effectiveData) {
    return (
      <>
        {fullModeToolbar}
        <p className="pf-status">{t('noProjectFiles')}</p>
        <style>{styles}</style>
      </>
    );
  }

  return (
    <>
      {fullModeToolbar}
      <ProjectFilesView
        jobId={jobId}
        data={effectiveData}
        mode={mode}
        t={t}
        chunkIndexFilter={chunkIndexFilter}
        onFilesMutated={() => reloadFiles()}
        hideChunkSections={hideChunkSections}
      />
    </>
  );
}

const styles = `
  .project-files { margin-top: 1rem; color: var(--color-text); }
  .pf-status { margin: 0.5rem 0; font-size: 0.875rem; color: var(--color-text-secondary); }
  .pf-status--err { color: var(--color-error-muted); }
  .pf-section { margin-bottom: 1rem; }
  .pf-section__title { margin: 0 0 0.35rem 0; font-size: 0.85rem; color: var(--color-label); text-transform: uppercase; letter-spacing: 0.03em; }
  .pf-list { list-style: none; margin: 0; padding: 0; }
  .pf-file {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: flex-start;
    gap: 0.5rem;
    padding: 0.35rem 0;
    border-bottom: 1px solid var(--color-border);
    font-size: 0.8125rem;
  }
  .pf-file-main {
    width: 400px;
    min-width: 0;
    flex: 0 1 auto;
    display: flex;
    flex-direction: column;
    gap: 0.15rem;
  }
  .pf-file-name { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .pf-file-meta { color: var(--color-text-secondary); font-size: 0.75rem; }
  .pf-file-actions { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; flex-shrink: 0; }
  .pf-audio { max-width: min(220px, 100%); height: 28px; vertical-align: middle; }
  .pf-open, .pf-edit { font-size: 0.75rem; }
  .pf-open { color: var(--color-link); }
  .pf-edit {
    padding: 0.15rem 0.4rem;
    border: 1px solid var(--color-border-strong);
    border-radius: 4px;
    background: var(--color-surface);
    cursor: pointer;
    color: var(--color-heading);
  }
  .pf-edit:hover { background: var(--color-surface-hover); }
  .pf-modal-overlay {
    position: fixed;
    inset: 0;
    background: var(--modal-overlay);
    z-index: 1000;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 1rem;
  }
  .pf-modal {
    background: var(--color-surface);
    color: var(--color-text);
    border-radius: 8px;
    box-shadow: var(--shadow-modal);
    width: min(90vw, 1400px);
    max-width: 90vw;
    height: min(90vh, 900px);
    max-height: 90vh;
    display: flex;
    flex-direction: column;
    padding: 1rem;
    gap: 0.75rem;
  }
  .pf-modal__title { margin: 0; font-size: 1rem; }
  .pf-panel-toolbar {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.75rem 1rem;
    margin-bottom: 0.75rem;
    padding: 0.5rem 0;
    border-bottom: 1px solid var(--color-border);
  }
  .pf-jobdir { margin: 0; font-size: 0.8125rem; color: var(--color-label); flex: 1; min-width: 12rem; }
  .pf-jobdir__label { font-weight: 600; margin-right: 0.35rem; }
  .pf-jobdir__path { word-break: break-all; font-size: 0.75rem; color: var(--color-heading); }
  .pf-refresh-files {
    padding: 0.35rem 0.75rem;
    border-radius: 4px;
    border: 1px solid var(--color-border-strong);
    background: var(--color-surface);
    color: var(--color-text);
    cursor: pointer;
    font-size: 0.8125rem;
    flex-shrink: 0;
  }
  .pf-refresh-files:hover:not(:disabled) { background: var(--color-surface-hover); }
  .pf-refresh-files:disabled { opacity: 0.55; cursor: not-allowed; }
  .pf-refresh-hint { font-size: 0.75rem; color: var(--color-text-secondary); }
  .pf-toolbar-actions {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin-left: auto;
    flex-shrink: 0;
  }
  .pf-jobdir-spacer { flex: 1; min-width: 0; }
  .pf-textarea {
    flex: 1;
    min-height: 240px;
    width: 100%;
    font-family: ui-monospace, monospace;
    font-size: 0.8125rem;
    line-height: 1.45;
    padding: 0.5rem;
    border: 1px solid var(--color-border);
    border-radius: 4px;
    resize: vertical;
    background: var(--color-surface-sunken);
    color: var(--color-text);
  }
  .pf-modal__actions { display: flex; gap: 0.5rem; justify-content: flex-end; }
  .pf-modal__actions button {
    padding: 0.35rem 0.75rem;
    border-radius: 4px;
    border: 1px solid var(--color-border-strong);
    background: var(--color-surface);
    color: var(--color-text);
    cursor: pointer;
  }
  .pf-modal__actions button:last-child {
    background: var(--color-primary);
    color: var(--color-on-primary);
    border-color: var(--color-primary);
  }
  .pf-modal__actions button:disabled { opacity: 0.5; cursor: not-allowed; }
`;
