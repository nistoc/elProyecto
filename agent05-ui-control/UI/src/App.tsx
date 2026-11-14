import { useMemo, useState } from 'react';
import { I18nProvider, useI18n } from './contexts/I18nContext';
import { ThemeProvider, useTheme } from './contexts/ThemeContext';
import { useJob, type StepId } from './hooks/useJob';
import { useJobChunkArtifactGroups } from './hooks/useJobChunkArtifactGroups';
import { useJobProjectFiles } from './hooks/useJobProjectFiles';
import { useJobLanguageShare } from './hooks/useJobLanguageShare';
import { useDebouncedValue } from './hooks/useDebouncedValue';
import { StepCard } from './components/StepCard';
import { UploadCard } from './components/UploadCard';
import { LessonsPanel } from './components/LessonsPanel';
import { JobsList } from './components/JobsList';
import { ResultSection } from './components/ResultSection';
import { ProjectFilesPanel } from './components/ProjectFilesPanel';
import { ChunkControlsStats } from './components/ChunkControlsStats';
import { RefinerProgress } from './components/RefinerProgress';
import { RefinerSettingsPanel } from './components/RefinerSettingsPanel';
import { runSettingsLine } from './utils/runSettings';
import { languageShareLines } from './utils/languageShare';

function AppContent() {
  const { t, locale, setLocale } = useI18n();
  const { theme, toggleTheme } = useTheme();
  const {
    jobId,
    job,
    jobsList,
    loadingList,
    activeStep,
    setActiveStep,
    file,
    setFile,
    isSubmitting,
    error,
    getStepStatus,
    refreshList,
    handleSelectJob,
    handleStart,
    handleReset,
    handleDeleteJob,
    jobSnapshotRevision,
    refreshJobSnapshot,
    startRefiner,
    refineProgress,
  } = useJob();

  // SSE snapshots arrive ~every 2s while transcribing; debounce the heavy /files and
  // /chunk-artifact-groups refetches so they hit Agent04 at most once per 3s. Job switches
  // stay instant (both hooks key on jobId) and manual reloads bypass this via .reload().
  // Lessons of the recordings folder take the place of the job view while they are open.
  const [showLessons, setShowLessons] = useState(false);
  // A job picked in the sidebar or in a lesson is shown itself, not the upload form that was open
  // (or that the job remembers as its last step).
  const openJob = (id: string) => {
    setShowLessons(false);
    handleSelectJob(id, 'transcriber');
  };

  const debouncedRevision = useDebouncedValue(jobSnapshotRevision, 3000);
  const jobFiles = useJobProjectFiles(jobId, debouncedRevision);
  const jobChunkGroups = useJobChunkArtifactGroups(jobId, debouncedRevision);
  const languageShare = useJobLanguageShare(jobId, debouncedRevision);
  const languageLines = useMemo(
    () => languageShareLines(languageShare, t),
    [languageShare, t]
  );

  // Combined transcripts the refiner can consume: every transcript*.md that is NOT a _fixed
  // output. Newest first — timestamped names (transcript_YYYYMMDD_HHmmss_fff.md) sort by time.
  const refinerInputs = useMemo(() => {
    const ts = jobFiles.data?.transcripts ?? [];
    return ts
      .filter(
        (f) =>
          f.name.toLowerCase().endsWith('.md') &&
          !f.name.toLowerCase().includes('_fixed')
      )
      .slice()
      .sort((a, b) => b.name.localeCompare(a.name));
  }, [jobFiles.data]);

  const steps: { id: StepId; title: string }[] = [
    { id: 'transcriber', title: t('transcriber') },
    { id: 'refiner', title: t('refiner') },
    { id: 'result', title: t('result') },
  ];

  return (
    <div className="app">
      <header className="topbar">
        <div className="topbar__brand">
          <h1 className="topbar__title">{t('appTitle')}</h1>
          <button
            type="button"
            className="topbar__upload"
            onClick={() => {
              setShowLessons(false);
              setActiveStep('upload');
            }}
          >
            {t('upload')}
          </button>
          <button
            type="button"
            className={
              showLessons
                ? 'topbar__lessons topbar__lessons--on'
                : 'topbar__lessons'
            }
            onClick={() => setShowLessons((v) => !v)}
            aria-pressed={showLessons}
            data-testid="topbar-lessons"
          >
            {t('lessons')}
          </button>
        </div>
        <div className="topbar__actions">
          <button
            type="button"
            className="topbar__theme"
            onClick={toggleTheme}
            title={t('themeSwitch')}
            aria-label={t('themeSwitch')}
          >
            {theme === 'dark' ? '☀️ ' : '🌙 '}
            {theme === 'dark' ? t('themeLight') : t('themeDark')}
          </button>
          <select
            value={locale}
            onChange={(e) => setLocale(e.target.value as 'en' | 'ru' | 'es')}
            className="topbar__locale"
          >
            <option value="en">EN</option>
            <option value="ru">RU</option>
            <option value="es">ES</option>
          </select>
          <button type="button" onClick={handleReset} className="topbar__clear">
            {t('clear')}
          </button>
        </div>
      </header>

      <div className="layout">
        <aside className="sidebar">
          <JobsList
            jobs={jobsList}
            currentJobId={jobId}
            onSelectJob={openJob}
            onRefresh={refreshList}
            onDelete={handleDeleteJob}
            loading={loadingList}
            t={t}
          />
        </aside>

        <main className="content">
          {showLessons && (
            <LessonsPanel
              t={t}
              onOpenJob={openJob}
              onJobsChanged={refreshList}
            />
          )}
          <div
            className="steps-row"
            style={showLessons ? { display: 'none' } : undefined}
          >
            {steps.map(({ id, title }) => (
              <StepCard
                key={id}
                title={title}
                status={getStepStatus(id)}
                active={activeStep === id}
                onSelect={() => setActiveStep(id)}
              />
            ))}
          </div>

          {!showLessons &&
            activeStep !== 'upload' &&
            runSettingsLine(job?.runSettings, t) && (
              <p className="job-run-settings" data-testid="job-run-settings">
                {runSettingsLine(job?.runSettings, t)}
              </p>
            )}

          {!showLessons &&
            activeStep !== 'upload' &&
            languageLines.length > 0 && (
              <div
                className="job-language-share"
                data-testid="job-language-share"
              >
                {languageLines.map((line) => (
                  <p key={line}>{line}</p>
                ))}
              </div>
            )}

          <div
            className="step-content"
            style={showLessons ? { display: 'none' } : undefined}
          >
            {activeStep === 'upload' && (
              <UploadCard
                file={file}
                onFileChange={setFile}
                onStart={handleStart}
                disabled={isSubmitting}
                t={t}
                error={error}
              />
            )}

            {activeStep === 'transcriber' && (
              <div className="step-panel">
                {job ? (
                  <>
                    <p className="step-panel__meta">
                      {t('status')}: {job.status} · {t('phase')}: {job.phase}
                    </p>
                    {job.transcriptionError?.trim() && (
                      <div
                        className="step-panel__transcription-error"
                        role="alert"
                      >
                        <strong className="step-panel__transcription-error-title">
                          {t('transcriptionErrorTitle')}
                        </strong>
                        <p className="step-panel__transcription-error-body">
                          {job.transcriptionError}
                        </p>
                      </div>
                    )}
                    {jobId && (
                      <>
                        <ChunkControlsStats
                          jobId={jobId}
                          job={job}
                          files={jobFiles.data}
                          filesLoading={jobFiles.loading}
                          filesError={jobFiles.error}
                          chunkArtifactGroups={jobChunkGroups.data}
                          locale={locale}
                          t={t}
                          refreshJobSnapshot={refreshJobSnapshot}
                          onProjectFilesChanged={async () => {
                            await jobFiles.reload();
                            jobChunkGroups.reload();
                            await refreshJobSnapshot();
                          }}
                        />
                        <h4 className="step-panel__files-heading">
                          {t('projectFiles')}
                        </h4>
                        <ProjectFilesPanel
                          jobId={jobId}
                          mode="full"
                          t={t}
                          filesRefreshKey={debouncedRevision}
                          managedFiles={jobFiles}
                          managedChunkGroups={jobChunkGroups}
                          hideChunkSections
                        />
                      </>
                    )}
                  </>
                ) : (
                  <p className="step-panel__empty">
                    {jobId ? t('loading') : t('selectJobOrCreate')}
                  </p>
                )}
              </div>
            )}

            {activeStep === 'refiner' && (
              <div className="step-panel">
                {job && jobId ? (
                  <>
                    <RefinerSettingsPanel
                      t={t}
                      disabled={job.phase === 'refiner'}
                    />
                    <div className="refiner-panel">
                      <div className="refiner-panel__head">
                        <span className="refiner-panel__title">
                          {t('refinerInputLabel')}
                        </span>
                        {job.phase === 'refiner' && (
                          <span className="refiner-panel__badge refiner-panel__badge--running">
                            {t('refinerRunning')}
                          </span>
                        )}
                        {job.phase === 'awaiting_refiner' && (
                          <button
                            type="button"
                            className="refiner-actions__skip"
                            onClick={() => startRefiner(true)}
                          >
                            {t('refinerSkip')}
                          </button>
                        )}
                      </div>
                      {refinerInputs.length === 0 ? (
                        <p className="refiner-panel__hint">
                          {t('refinerInputUnknown')}
                        </p>
                      ) : (
                        <ul className="refiner-files">
                          {refinerInputs.map((f) => (
                            <li
                              key={f.relativePath}
                              className="refiner-files__row"
                            >
                              <div className="refiner-files__meta">
                                <code className="refiner-panel__file">
                                  {f.name}
                                </code>
                                <span className="refiner-panel__arrow">→</span>
                                <code className="refiner-panel__file refiner-panel__file--out">
                                  transcript_fixed_&lt;{t('refinerOutputTime')}
                                  &gt;.md
                                </code>
                              </div>
                              <button
                                type="button"
                                className="refiner-actions__run"
                                disabled={job.phase === 'refiner'}
                                onClick={() => startRefiner(false, f.name)}
                              >
                                {job.phase === 'completed'
                                  ? t('refinerRerun')
                                  : t('refinerRun')}
                              </button>
                            </li>
                          ))}
                        </ul>
                      )}
                      {job.phase === 'awaiting_refiner' && (
                        <p className="refiner-panel__hint">
                          {t('refinerAwaitingHint')}
                        </p>
                      )}
                      {job.phase === 'completed' && (
                        <p className="refiner-panel__hint">
                          {t('refinerCompleted')}
                        </p>
                      )}
                    </div>
                    <RefinerProgress progress={refineProgress} t={t} />
                    <ResultSection
                      filesRefreshKey={debouncedRevision}
                      jobId={jobId}
                      job={job}
                      t={t}
                      variant="refiner"
                      chunkArtifactGroups={jobChunkGroups.data}
                    />
                  </>
                ) : (
                  <p className="step-panel__empty">{t('selectJob')}</p>
                )}
              </div>
            )}

            {activeStep === 'result' && jobId && (
              <div className="step-panel">
                <ResultSection
                  filesRefreshKey={debouncedRevision}
                  jobId={jobId}
                  job={job}
                  t={t}
                  chunkArtifactGroups={jobChunkGroups.data}
                />
              </div>
            )}

            {activeStep === 'result' && !jobId && (
              <p className="step-panel__empty">{t('selectJobForResult')}</p>
            )}
          </div>
        </main>
      </div>

      <footer
        className="status-bar"
        role="status"
        aria-label={t('statusBarAriaLabel')}
      >
        <span
          className={
            job?.transcriptionFooterHint?.trim()
              ? 'status-bar__text status-bar__text--hint'
              : 'status-bar__text'
          }
          title={job?.transcriptionFooterHint?.trim() || undefined}
        >
          {job?.transcriptionFooterHint?.trim() || t('statusBarDefaultMessage')}
        </span>
        {job?.transcriptionSyncDebug?.trim() ? (
          <span className="status-bar__debug" title={t('statusBarDebugTitle')}>
            {job.transcriptionSyncDebug.trim()}
          </span>
        ) : null}
      </footer>

      <style>{`
        .app {
          flex: 1;
          min-height: 0;
          display: flex;
          flex-direction: column;
          overflow: hidden;
        }
        .topbar {
          flex-shrink: 0;
          display: flex;
          justify-content: space-between;
          align-items: center;
          padding: 0.75rem 1.5rem;
          border-bottom: 1px solid var(--color-border);
          background: var(--color-surface-raised);
          color: var(--color-text);
        }
        .topbar__brand {
          display: flex;
          align-items: center;
          gap: 0.75rem;
          min-width: 0;
        }
        .topbar__title { margin: 0; font-size: 1.25rem; color: var(--color-text); }
        .topbar__upload {
          flex-shrink: 0;
          padding: 0.35rem 0.75rem;
          border-radius: 6px;
          border: 1px solid var(--color-border-strong);
          background: var(--color-primary);
          color: var(--color-on-primary);
          font-size: 0.875rem;
          font-weight: 500;
          cursor: pointer;
        }
        .topbar__upload:hover { background: var(--color-primary-hover); }
        .topbar__lessons {
          flex-shrink: 0;
          padding: 0.35rem 0.75rem;
          border-radius: 6px;
          border: 1px solid var(--color-border-strong);
          background: var(--color-surface);
          color: var(--color-text);
          font-size: 0.875rem;
          font-weight: 500;
          cursor: pointer;
        }
        .topbar__lessons:hover { background: var(--color-surface-hover); }
        .topbar__lessons--on {
          background: var(--color-accent-soft);
          border-color: var(--color-primary);
        }
        .topbar__actions { display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap; }
        .topbar__theme,
        .topbar__locale,
        .topbar__clear {
          padding: 0.25rem 0.5rem;
          border-radius: 6px;
          border: 1px solid var(--color-border-strong);
          background: var(--color-surface);
          color: var(--color-text);
          cursor: pointer;
          font-size: 0.875rem;
        }
        .topbar__theme:hover,
        .topbar__clear:hover { background: var(--color-surface-hover); }
        .topbar__locale { cursor: pointer; }
        .layout {
          flex: 1;
          min-height: 0;
          display: flex;
          overflow: hidden;
        }
        .sidebar {
          width: 280px;
          flex-shrink: 0;
          min-height: 0;
          border-right: 1px solid var(--color-border);
          background: var(--color-sidebar);
          overflow-x: hidden;
          overflow-y: auto;
        }
        .content {
          flex: 1;
          min-width: 0;
          min-height: 0;
          padding: 1rem;
          display: flex;
          flex-direction: column;
          overflow: hidden;
        }
        .status-bar {
          flex-shrink: 0;
          display: flex;
          flex-direction: column;
          align-items: stretch;
          gap: 0.25rem;
          min-height: 1.75rem;
          padding: 0.35rem 1rem;
          border-top: 1px solid var(--color-border);
          background: var(--color-surface-sunken);
          color: var(--color-text-secondary);
          font-size: 0.75rem;
        }
        .status-bar__text {
          flex: 1;
          min-width: 0;
          white-space: nowrap;
          overflow: hidden;
          text-overflow: ellipsis;
        }
        .status-bar__text--hint {
          color: var(--color-warning, #b8860b);
        }
        .status-bar__debug {
          font-family: ui-monospace, monospace;
          font-size: 0.62rem;
          line-height: 1.3;
          color: var(--color-text-muted);
          word-break: break-all;
          white-space: normal;
        }
        .steps-row {
          flex-shrink: 0;
          display: flex;
          gap: 0.5rem;
          margin-bottom: 1rem;
          flex-wrap: wrap;
        }
        .step-content {
          flex: 1;
          min-height: 0;
          margin-top: 0.5rem;
          overflow-x: hidden;
          overflow-y: auto;
        }
        .step-panel { padding: 1rem; }
        .step-panel__meta { margin: 0 0 0.5rem 0; font-size: 0.875rem; color: var(--color-text-secondary); }
        .job-run-settings { margin: 0 0 0.75rem 0; font-size: 0.875rem; color: var(--color-text-secondary); }
        .step-panel__files-heading { margin: 1rem 0 0.35rem 0; font-size: 0.9rem; color: var(--color-heading); }
        .step-panel__empty { color: var(--color-text-muted); margin: 0; }
        .step-panel__hint { margin: 0 0 0.75rem 0; font-size: 0.875rem; color: var(--color-label); }
        .refiner-panel {
          margin: 0 0 1rem 0;
          padding: 0.85rem 1rem;
          border: 1px solid var(--color-border);
          border-radius: 8px;
          background: var(--color-subtle-panel, var(--color-surface));
        }
        .refiner-panel__head {
          display: flex;
          align-items: center;
          gap: 0.6rem;
          margin-bottom: 0.55rem;
        }
        .refiner-panel__title { font-size: 0.9rem; color: var(--color-heading); font-weight: 500; }
        .refiner-panel__badge {
          font-size: 0.7rem;
          padding: 0.1rem 0.45rem;
          border-radius: 999px;
        }
        .refiner-panel__badge--running {
          background: var(--color-accent, #2d7ff9);
          color: #fff;
        }
        .refiner-panel__io {
          display: flex;
          align-items: center;
          gap: 0.5rem;
          flex-wrap: wrap;
          margin-bottom: 0.75rem;
        }
        .refiner-panel__file {
          font-family: var(--font-mono, monospace);
          font-size: 0.78rem;
          padding: 0.2rem 0.45rem;
          border-radius: 5px;
          background: var(--color-surface);
          border: 1px solid var(--color-border);
          color: var(--color-text);
          word-break: break-all;
        }
        .refiner-panel__file--out { color: var(--color-text-secondary); }
        .refiner-panel__arrow { color: var(--color-text-muted); }
        .refiner-files { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 0.4rem; }
        .refiner-files__row {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 0.75rem;
          padding: 0.5rem 0.6rem;
          border: 1px solid var(--color-border);
          border-radius: 6px;
          background: var(--color-surface);
          flex-wrap: wrap;
        }
        .refiner-files__meta { display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap; min-width: 0; }
        .refiner-panel__hint { margin: 0.6rem 0 0 0; font-size: 0.8rem; color: var(--color-text-secondary); }
        .refiner-actions { display: flex; gap: 0.5rem; margin: 0; }
        .refiner-actions__run {
          padding: 0.4rem 0.9rem;
          border-radius: 6px;
          border: 1px solid var(--color-border-strong);
          background: var(--color-primary);
          color: var(--color-on-primary);
          font-size: 0.875rem;
          font-weight: 500;
          cursor: pointer;
        }
        .refiner-actions__run:hover:not(:disabled) { background: var(--color-primary-hover); }
        .refiner-actions__run:disabled { opacity: 0.5; cursor: not-allowed; }
        .refiner-actions__skip {
          padding: 0.4rem 0.9rem;
          border-radius: 6px;
          border: 1px solid var(--color-border-strong);
          background: var(--color-surface);
          color: var(--color-text);
          font-size: 0.875rem;
          cursor: pointer;
        }
        .refiner-actions__skip:hover { background: var(--color-surface-hover); }
        .step-panel__transcription-error {
          margin: 0 0 1rem 0;
          padding: 0.65rem 0.85rem;
          border-radius: 8px;
          border: 1px solid var(--color-danger, #c62828);
          background: color-mix(in srgb, var(--color-danger, #c62828) 12%, var(--color-surface));
          color: var(--color-text);
        }
        .step-panel__transcription-error-title {
          display: block;
          font-size: 0.875rem;
          margin-bottom: 0.35rem;
          color: var(--color-danger, #b71c1c);
        }
        .step-panel__transcription-error-body {
          margin: 0;
          font-size: 0.8125rem;
          line-height: 1.45;
          white-space: pre-wrap;
          word-break: break-word;
        }
      `}</style>
    </div>
  );
}

export default function App() {
  return (
    <ThemeProvider>
      <I18nProvider>
        <AppContent />
      </I18nProvider>
    </ThemeProvider>
  );
}
