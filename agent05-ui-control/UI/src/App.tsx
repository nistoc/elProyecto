import { useState, useCallback, useEffect } from 'react';
import { I18nProvider, useI18n } from './contexts/I18nContext';
import { ThemeProvider, useTheme } from './contexts/ThemeContext';
import { useJob, type StepId } from './hooks/useJob';
import { StepCard } from './components/StepCard';
import { UploadCard } from './components/UploadCard';
import { JobsList } from './components/JobsList';
import { LogsSection } from './components/LogsSection';
import { ResultSection } from './components/ResultSection';
import { ProjectFilesPanel } from './components/ProjectFilesPanel';
import { ChunkControlPanel } from './components/ChunkControlPanel';

function AppContent() {
  const { t, locale, setLocale } = useI18n();
  const { theme, toggleTheme } = useTheme();
  const [initialJobId] = useState<string | null>(() => null);
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
    logsPaused,
    bufferedCount,
    toggleLogsPause,
    clearLogsForStep,
  } = useJob(initialJobId);

  const [chunkFileFilter, setChunkFileFilter] = useState<number | null>(null);
  const onChunkFileFilterChange = useCallback((v: number | null) => {
    setChunkFileFilter(v);
  }, []);

  useEffect(() => {
    if (activeStep !== 'transcriber') setChunkFileFilter(null);
  }, [activeStep]);

  useEffect(() => {
    setChunkFileFilter(null);
  }, [jobId]);

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
            onClick={() => setActiveStep('upload')}
          >
            {t('upload')}
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
            onSelectJob={handleSelectJob}
            onRefresh={refreshList}
            onDelete={handleDeleteJob}
            loading={loadingList}
            t={t}
          />
        </aside>

        <main className="content">
          <div className="steps-row">
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

          <div className="step-content">
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
                    <LogsSection
                      title={t('logs')}
                      logs={job.logs ?? []}
                      paused={logsPaused}
                      bufferedCount={bufferedCount}
                      onTogglePause={toggleLogsPause}
                      onClearLogs={clearLogsForStep}
                    />
                    {jobId && (
                      <>
                        <ChunkControlPanel
                          jobId={jobId}
                          job={job}
                          t={t}
                          fileFilterChunkIndex={chunkFileFilter}
                          onFileFilterChunkChange={onChunkFileFilterChange}
                        />
                        <h4 className="step-panel__files-heading">
                          {t('projectFiles')}
                        </h4>
                        <ProjectFilesPanel
                          jobId={jobId}
                          mode="full"
                          t={t}
                          chunkIndexFilter={chunkFileFilter}
                          filesRefreshKey={jobSnapshotRevision}
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
                    {job.phase === 'awaiting_refiner' && (
                      <p className="step-panel__hint">{t('refinerAwaitingHint')}</p>
                    )}
                    {job.phase === 'refiner' && (
                      <p className="step-panel__hint">{t('refinerRunning')}</p>
                    )}
                    {job.phase === 'completed' && (
                      <p className="step-panel__hint">{t('refinerCompleted')}</p>
                    )}
                    <LogsSection
                      title={t('logs')}
                      logs={job.logs ?? []}
                      paused={logsPaused}
                      bufferedCount={bufferedCount}
                      onTogglePause={toggleLogsPause}
                      onClearLogs={clearLogsForStep}
                    />
                    <ResultSection
                      jobId={jobId}
                      job={job}
                      t={t}
                      variant="refiner"
                    />
                  </>
                ) : (
                  <p className="step-panel__empty">{t('selectJob')}</p>
                )}
              </div>
            )}

            {activeStep === 'result' && jobId && (
              <div className="step-panel">
                {job && (
                  <LogsSection
                    title={t('logs')}
                    logs={job.logs ?? []}
                    paused={logsPaused}
                    bufferedCount={bufferedCount}
                    onTogglePause={toggleLogsPause}
                    onClearLogs={clearLogsForStep}
                  />
                )}
                <ResultSection jobId={jobId} job={job} t={t} />
              </div>
            )}

            {activeStep === 'result' && !jobId && (
              <p className="step-panel__empty">{t('selectJobForResult')}</p>
            )}
          </div>
        </main>
      </div>

      <style>{`
        .app { min-height: 100vh; display: flex; flex-direction: column; }
        .topbar {
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
        .layout { display: flex; flex: 1; min-height: 0; }
        .sidebar {
          width: 280px;
          border-right: 1px solid var(--color-border);
          background: var(--color-sidebar);
          overflow-y: auto;
        }
        .content { flex: 1; padding: 1rem; overflow-y: auto; }
        .steps-row { display: flex; gap: 0.5rem; margin-bottom: 1rem; flex-wrap: wrap; }
        .step-content { margin-top: 0.5rem; }
        .step-panel { padding: 1rem; }
        .step-panel__meta { margin: 0 0 0.5rem 0; font-size: 0.875rem; color: var(--color-text-secondary); }
        .step-panel__files-heading { margin: 1rem 0 0.35rem 0; font-size: 0.9rem; color: var(--color-heading); }
        .step-panel__empty { color: var(--color-text-muted); margin: 0; }
        .step-panel__hint { margin: 0 0 0.75rem 0; font-size: 0.875rem; color: var(--color-label); }
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
