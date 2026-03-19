import { useEffect, useState } from 'react';
import {
  fetchJobFiles,
  jobProjectFileContentUrl,
  fetchJobFileText,
  putJobFileContent,
} from '../api';
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

function TextFileEditorModal({
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

function FileRow({
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
      <div className="pf-file-main">
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
  /** Called after a text file is saved (refresh lists / line counts). */
  onFilesMutated?: () => void;
}

/** Renders structured file lists (no fetch). */
export function ProjectFilesView({
  jobId,
  data,
  mode,
  t,
  onFilesMutated,
}: ProjectFilesViewProps) {
  const [editTarget, setEditTarget] = useState<{
    relativePath: string;
    name: string;
  } | null>(null);

  const openEditor = (f: JobProjectFile) =>
    setEditTarget({ relativePath: f.relativePath, name: f.name });

  const splitTranscripts = data.splitChunks.filter((x) => x.isTranscript);

  const body =
    mode === 'transcripts' ? (
      <>
        <Section
          title={t('sectionTranscripts')}
          jobId={jobId}
          items={data.transcripts}
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
        {!data.transcripts.length && !splitTranscripts.length && (
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
          items={data.transcripts}
          t={t}
          onEditText={openEditor}
        />
        <Section
          title={t('sectionChunks')}
          jobId={jobId}
          items={data.chunks}
          t={t}
          onEditText={openEditor}
        />
        <Section
          title={t('sectionChunkJson')}
          jobId={jobId}
          items={data.chunkJson}
          t={t}
          onEditText={openEditor}
        />
        <Section
          title={t('sectionIntermediate')}
          jobId={jobId}
          items={data.intermediate}
          t={t}
          onEditText={openEditor}
        />
        <Section
          title={t('sectionConverted')}
          jobId={jobId}
          items={data.converted}
          t={t}
          onEditText={openEditor}
        />
        <Section
          title={t('sectionSplitChunks')}
          jobId={jobId}
          items={data.splitChunks}
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
}

export function ProjectFilesPanel({ jobId, mode, t }: ProjectFilesPanelProps) {
  const [data, setData] = useState<JobProjectFiles | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setErr(null);
    fetchJobFiles(jobId)
      .then((res) => {
        if (cancelled) return;
        setData(res?.files ?? null);
      })
      .catch((e) => {
        if (!cancelled) {
          setErr(e instanceof Error ? e.message : t('failedToLoadFiles'));
          setData(null);
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [jobId, reloadKey, t]);

  if (loading) {
    return <p className="pf-status">{t('loadingFiles')}</p>;
  }
  if (err) {
    return (
      <>
        <p className="pf-status pf-status--err">{err}</p>
        <style>{styles}</style>
      </>
    );
  }
  if (!data) {
    return (
      <>
        <p className="pf-status">{t('noProjectFiles')}</p>
        <style>{styles}</style>
      </>
    );
  }

  return (
    <ProjectFilesView
      jobId={jobId}
      data={data}
      mode={mode}
      t={t}
      onFilesMutated={() => setReloadKey((k) => k + 1)}
    />
  );
}

const styles = `
  .project-files { margin-top: 1rem; }
  .pf-status { margin: 0.5rem 0; font-size: 0.875rem; color: #64748b; }
  .pf-status--err { color: #b91c1c; }
  .pf-section { margin-bottom: 1rem; }
  .pf-section__title { margin: 0 0 0.35rem 0; font-size: 0.85rem; color: #475569; text-transform: uppercase; letter-spacing: 0.03em; }
  .pf-list { list-style: none; margin: 0; padding: 0; }
  .pf-file {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: 0.5rem;
    padding: 0.35rem 0;
    border-bottom: 1px solid #e2e8f0;
    font-size: 0.8125rem;
  }
  .pf-file-main { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 0.15rem; }
  .pf-file-name { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .pf-file-meta { color: #64748b; font-size: 0.75rem; }
  .pf-file-actions { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; flex-shrink: 0; }
  .pf-audio { max-width: min(220px, 100%); height: 28px; vertical-align: middle; }
  .pf-open, .pf-edit { font-size: 0.75rem; }
  .pf-open { color: #2563eb; }
  .pf-edit {
    padding: 0.15rem 0.4rem;
    border: 1px solid #cbd5e1;
    border-radius: 4px;
    background: #fff;
    cursor: pointer;
    color: #334155;
  }
  .pf-edit:hover { background: #f8fafc; }
  .pf-modal-overlay {
    position: fixed;
    inset: 0;
    background: rgba(15, 23, 42, 0.45);
    z-index: 1000;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 1rem;
  }
  .pf-modal {
    background: #fff;
    border-radius: 8px;
    box-shadow: 0 25px 50px -12px rgba(0,0,0,0.25);
    max-width: min(900px, 100%);
    width: 100%;
    max-height: min(85vh, 900px);
    display: flex;
    flex-direction: column;
    padding: 1rem;
    gap: 0.75rem;
  }
  .pf-modal__title { margin: 0; font-size: 1rem; }
  .pf-textarea {
    flex: 1;
    min-height: 240px;
    width: 100%;
    font-family: ui-monospace, monospace;
    font-size: 0.8125rem;
    line-height: 1.45;
    padding: 0.5rem;
    border: 1px solid #e2e8f0;
    border-radius: 4px;
    resize: vertical;
  }
  .pf-modal__actions { display: flex; gap: 0.5rem; justify-content: flex-end; }
  .pf-modal__actions button {
    padding: 0.35rem 0.75rem;
    border-radius: 4px;
    border: 1px solid #cbd5e1;
    background: #fff;
    cursor: pointer;
  }
  .pf-modal__actions button:last-child {
    background: #2563eb;
    color: #fff;
    border-color: #2563eb;
  }
  .pf-modal__actions button:disabled { opacity: 0.5; cursor: not-allowed; }
`;
