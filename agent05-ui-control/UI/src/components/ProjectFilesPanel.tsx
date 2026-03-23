import { useEffect, useRef, useState } from 'react';
import {
  fetchJobFiles,
  jobProjectFileContentUrl,
  fetchJobFileText,
  putJobFileContent,
} from '../api';
import type { JobChunkArtifactGroupsState } from '../hooks/useJobChunkArtifactGroups';
import type { JobProjectFilesState } from '../hooks/useJobProjectFiles';
import type { JobProjectFile, JobProjectFiles } from '../types';
import { isRefinerTranscriptArtifactName } from '../utils/transcriptStemLatest';
import { JobAudioWavePlayer } from './JobAudioWavePlayer';

/** Transcript .md rows that can be sent to the refiner (excludes transcript_fixed*). */
function isRefinerSourceTranscriptRow(f: JobProjectFile): boolean {
  if (f.kind !== 'text') return false;
  if (!f.name.toLowerCase().endsWith('.md')) return false;
  return !isRefinerTranscriptArtifactName(f.name);
}

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
  hideAudioActions,
  onDeleteFile,
  deleteDisabled,
  showRefine,
  onRefineTranscript,
  refineDisabled,
}: {
  jobId: string;
  f: JobProjectFile;
  t: (key: string) => string;
  onEditText?: (f: JobProjectFile) => void;
  /** When true, audio rows omit the inline player (render it separately, e.g. under the media row). */
  hideAudioActions?: boolean;
  /** When set, shows a delete control (e.g. orphan merged-split artifacts). */
  onDeleteFile?: (f: JobProjectFile) => void;
  deleteDisabled?: boolean;
  /** Refiner step: start refiner from this transcript row. */
  showRefine?: boolean;
  onRefineTranscript?: (relativePath: string) => void;
  refineDisabled?: boolean;
}) {
  const url = jobProjectFileContentUrl(jobId, f.relativePath);
  const nameTitle =
    f.kind === 'text' && onEditText
      ? `${f.relativePath}\n${t('doubleClickToEdit')}`
      : f.relativePath;
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
        <div className="pf-file-name-row">
          <a
            href={url}
            target="_blank"
            rel="noreferrer"
            className="pf-file-name"
            title={nameTitle}
            aria-label={`${t('openFile')}: ${f.name}`}
            onDoubleClick={(e) => {
              if (f.kind !== 'text' || !onEditText) return;
              e.preventDefault();
              e.stopPropagation();
              onEditText(f);
            }}
          >
            {f.name}
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
          {showRefine && onRefineTranscript && (
            <button
              type="button"
              className="pf-refine"
              disabled={refineDisabled}
              onClick={(e) => {
                e.preventDefault();
                e.stopPropagation();
                onRefineTranscript(f.relativePath);
              }}
            >
              {t('refineRowTranscript')}
            </button>
          )}
          {onDeleteFile && (
            <button
              type="button"
              className="pf-file-del"
              disabled={deleteDisabled}
              title={t('chunkDeleteMergedFileTitle')}
              aria-label={t('chunkDeleteMergedFileAria').replace('{name}', f.name)}
              onClick={(e) => {
                e.preventDefault();
                e.stopPropagation();
                onDeleteFile(f);
              }}
            >
              ×
            </button>
          )}
        </div>
        <span className="pf-file-meta">{fileMeta(f, t)}</span>
      </div>
      {f.kind === 'audio' && !hideAudioActions && (
        <div className="pf-file-actions">
          <JobAudioWavePlayer src={url} t={t} />
        </div>
      )}
    </li>
  );
}

function Section({
  title,
  jobId,
  items,
  t,
  onEditText,
  refineTargetRelativePath,
  refineShowOnEachEligibleRow,
  onRefineTranscript,
  refinerActionBusy,
}: {
  title: string;
  jobId: string;
  items: JobProjectFile[];
  t: (key: string) => string;
  onEditText?: (f: JobProjectFile) => void;
  /** When refineShowOnEachEligibleRow is false: only this row gets Refine. */
  refineTargetRelativePath?: string | null;
  /** Refiner step: show Refine on every eligible transcript .md (not only the primary stem row). */
  refineShowOnEachEligibleRow?: boolean;
  onRefineTranscript?: (relativePath: string) => void;
  refinerActionBusy?: boolean;
}) {
  if (!items.length) return null;
  const refineBusy = !!refinerActionBusy;
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
            showRefine={
              !!onRefineTranscript &&
              (refineShowOnEachEligibleRow
                ? isRefinerSourceTranscriptRow(f)
                : !!refineTargetRelativePath &&
                  f.relativePath === refineTargetRelativePath)
            }
            onRefineTranscript={onRefineTranscript}
            refineDisabled={refineBusy}
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
  /** Hide chunks + chunk JSON (shown in Chunk controls Stats). */
  hideChunkSections?: boolean;
  /** When refineShowOnEachEligibleRow is false: only this transcript row shows Refine. */
  refineTargetRelativePath?: string | null;
  /** Refiner step: show Refine on every eligible .md transcript row (default false). */
  refineShowOnEachEligibleRow?: boolean;
  onRefineTranscript?: (relativePath: string) => void;
  refinerActionBusy?: boolean;
}

/** Renders structured file lists (no fetch). */
export function ProjectFilesView({
  jobId,
  data,
  mode,
  t,
  onFilesMutated,
  hideChunkSections = false,
  refineTargetRelativePath,
  refineShowOnEachEligibleRow = false,
  onRefineTranscript,
  refinerActionBusy,
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
          refineTargetRelativePath={refineTargetRelativePath}
          refineShowOnEachEligibleRow={refineShowOnEachEligibleRow}
          onRefineTranscript={onRefineTranscript}
          refinerActionBusy={refinerActionBusy}
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
        {!hideChunkSections && (
          <>
            <Section
              title={indexedSectionTitle(data.chunks, t('sectionChunks'))}
              jobId={jobId}
              items={data.chunks}
              t={t}
              onEditText={openEditor}
            />
            <Section
              title={indexedSectionTitle(
                data.chunkJson,
                t('sectionChunkJson')
              )}
              jobId={jobId}
              items={data.chunkJson}
              t={t}
              onEditText={openEditor}
            />
          </>
        )}
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
  /** When this changes (e.g. SSE job snapshot), refetch GET .../files so new disk artifacts appear without switching tabs. */
  filesRefreshKey?: number;
  /** When set, file list state comes from parent (shared with Chunk controls Stats). */
  managedFiles?: JobProjectFilesState;
  /** When set, chunk-artifact-groups from parent (single fetch with Chunk controls). */
  managedChunkGroups?: JobChunkArtifactGroupsState;
  hideChunkSections?: boolean;
}

export function ProjectFilesPanel({
  jobId,
  mode,
  t,
  filesRefreshKey = 0,
  managedFiles,
  managedChunkGroups,
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
  const reloadChunkGroups = managedChunkGroups?.reload;

  const refreshFilesAndGroups = () => {
    reloadFiles();
    reloadChunkGroups?.();
  };

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
          {(effectiveRefreshing || managedChunkGroups?.refreshing) && (
            <span className="pf-refresh-hint">{t('loadingFiles')}</span>
          )}
          <button
            type="button"
            className="pf-refresh-files"
            onClick={() => refreshFilesAndGroups()}
            disabled={effectiveRefreshing || !!managedChunkGroups?.refreshing}
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
        onFilesMutated={() => refreshFilesAndGroups()}
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
  .pf-file-name-row {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.35rem 0.5rem;
    min-width: 0;
  }
  .pf-file-name-row .pf-file-name {
    min-width: 0;
  }
  a.pf-file-name {
    font-weight: 500;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    color: var(--color-link);
    text-decoration: none;
  }
  a.pf-file-name:hover {
    text-decoration: underline;
  }
  .pf-file-meta { color: var(--color-text-secondary); font-size: 0.75rem; }
  .pf-file-actions {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.5rem;
    flex-shrink: 0;
    flex: 0 0 auto;
    min-width: min(800px, 100%);
  }
  .pf-wave {
    position: relative;
    display: flex;
    align-items: center;
    gap: 0.4rem;
    width: 800px;
    min-width: 280px;
    flex: 0 0 auto;
    transition: none;
    --pf-wave-line: var(--color-border-strong);
    --pf-wave-fill: color-mix(in srgb, var(--color-primary) 50%, var(--color-surface-sunken));
    --pf-wave-playhead: var(--color-primary);
  }
  .pf-wave__audio {
    position: absolute;
    width: 0;
    height: 0;
    opacity: 0;
    pointer-events: none;
  }
  .pf-wave__play {
    flex-shrink: 0;
    width: 2rem;
    height: 2rem;
    padding: 0;
    border-radius: 4px;
    border: 1px solid var(--color-border-strong);
    background: var(--color-surface);
    color: var(--color-heading);
    cursor: pointer;
    font-size: 0.75rem;
    line-height: 1;
  }
  .pf-wave__play:disabled { opacity: 0.45; cursor: not-allowed; }
  .pf-wave__play:hover:not(:disabled) { background: var(--color-surface-hover); }
  .pf-wave__track-row {
    flex: 1;
    min-width: 0;
    display: flex;
    align-items: center;
    gap: 0.45rem;
  }
  .pf-wave__track {
    flex: 1;
    min-width: 0;
    position: relative;
    height: 40px;
    border-radius: 4px;
    border: 1px solid var(--color-border);
    background: var(--color-surface-sunken);
    overflow: hidden;
    touch-action: none;
    transition: none;
  }
  .pf-wave__cursor-time {
    position: absolute;
    top: 2px;
    left: 0;
    transform: translateX(-50%);
    z-index: 3;
    font-size: 0.6rem;
    line-height: 1.1;
    font-variant-numeric: tabular-nums;
    color: var(--color-heading);
    text-shadow: 0 0 3px var(--color-surface), 0 0 6px var(--color-surface);
    pointer-events: none;
    white-space: nowrap;
  }
  .pf-wave__duration {
    flex-shrink: 0;
    font-size: 0.68rem;
    font-variant-numeric: tabular-nums;
    color: var(--color-text-secondary);
    min-width: 2.75rem;
    text-align: right;
  }
  .pf-wave__canvas {
    display: block;
    width: 100%;
    height: 40px;
    vertical-align: top;
    transition: none;
  }
  .pf-wave__overlay {
    position: absolute;
    inset: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 0.7rem;
    color: var(--color-label);
    background: color-mix(in srgb, var(--color-surface) 88%, transparent);
    pointer-events: none;
  }
  .pf-wave__overlay--err { color: var(--color-error-muted); }
  .pf-edit { font-size: 0.75rem; }
  .pf-edit {
    padding: 0.15rem 0.4rem;
    border: 1px solid var(--color-border-strong);
    border-radius: 4px;
    background: var(--color-surface);
    cursor: pointer;
    color: var(--color-heading);
  }
  .pf-edit:hover { background: var(--color-surface-hover); }
  .pf-refine {
    font-size: 0.75rem;
    font-weight: 600;
    line-height: 1.2;
    padding: 0.15rem 0.45rem;
    border: 1px solid color-mix(in srgb, var(--color-primary) 40%, transparent);
    border-radius: 4px;
    background: var(--color-primary);
    color: #fff;
    cursor: pointer;
  }
  .pf-refine:hover:not(:disabled) {
    background: var(--color-primary-hover);
  }
  .pf-refine:disabled {
    opacity: 0.45;
    cursor: not-allowed;
  }
  .pf-file-del {
    flex-shrink: 0;
    width: 1.65rem;
    height: 1.65rem;
    padding: 0;
    border: 1px solid var(--color-border-strong);
    border-radius: 4px;
    background: var(--color-surface);
    color: var(--color-text);
    cursor: pointer;
    font-size: 1rem;
    line-height: 1;
  }
  .pf-file-del:hover:not(:disabled) { background: var(--color-surface-hover); }
  .pf-file-del:disabled { opacity: 0.45; cursor: not-allowed; }
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
