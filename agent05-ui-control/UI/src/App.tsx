import { useCallback, useMemo, useState } from 'react';
import { I18nProvider, useI18n } from './contexts/I18nContext';
import { ThemeProvider, useTheme } from './contexts/ThemeContext';
import { useJob, type StepId } from './hooks/useJob';
import { useJobChunkArtifactGroups } from './hooks/useJobChunkArtifactGroups';
import { useJobProjectFiles } from './hooks/useJobProjectFiles';
import { useJobLanguageShare } from './hooks/useJobLanguageShare';
import { useLessonJobs } from './hooks/useLessonJobs';
import { useDebouncedValue } from './hooks/useDebouncedValue';
import { StepCard } from './components/StepCard';
import { UploadCard } from './components/UploadCard';
import { LessonPageLinks } from './components/LessonPageLinks';
import { LessonPackageView } from './components/LessonPackageView';
import { LessonNotesView } from './components/LessonNotesView';
import { LessonsPanel } from './components/LessonsPanel';
import { JobsSidebar } from './components/JobsSidebar';
import { JobStats } from './components/JobStats';
import { ResultSection } from './components/ResultSection';
import { ProjectFilesPanel } from './components/ProjectFilesPanel';
import { ChunkControlsStats } from './components/ChunkControlsStats';
import { RefinerProgress } from './components/RefinerProgress';
import { RefinerSettingsPanel } from './components/RefinerSettingsPanel';
import { runSettingsLine } from './utils/runSettings';
import { refinePromptFor, type RefinePromptPick } from './utils/refinePrompt';

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
  // The package of a lesson (by its date) takes the place of whatever is open; closing it shows that again.
  const [packageDate, setPackageDate] = useState<string | null>(null);
  // The notes of a lesson (homework first) take the place the same way; one of the two is open at a time.
  const [notesDate, setNotesDate] = useState<string | null>(null);
  const openPackage = useCallback((date: string) => {
    setNotesDate(null);
    setPackageDate(date);
  }, []);
  const openNotes = useCallback((date: string) => {
    setPackageDate(null);
    setNotesDate(date);
  }, []);
  const lessonOpen = packageDate ?? notesDate;
  // A job picked in the sidebar or in a lesson is shown itself, not the upload form that was open
  // (or that the job remembers as its last step).
  // Kept the same between renders: the jobs list is not drawn anew for every status event of a job.
  const openJob = useCallback(
    (id: string) => {
      setShowLessons(false);
      setPackageDate(null);
      setNotesDate(null);
      handleSelectJob(id, 'transcriber');
    },
    [handleSelectJob]
  );

  // What the lessons list knows of the jobs: the jobs list is ordered and described by it, and an open
  // job shows the lesson it belongs to with the page made of that lesson. Read again when a job appears
  // or moves to another phase, and when the page of a lesson is changed in the lessons list.
  const [lessonsRevision, setLessonsRevision] = useState(0);
  const lessonsChanged = useCallback(
    () => setLessonsRevision((n) => n + 1),
    []
  );
  const lessonsSignature = useMemo(
    () =>
      `${jobsList.map((j) => `${j.id}:${j.phase}`).join('|')}#${lessonsRevision}`,
    [jobsList, lessonsRevision]
  );
  const lessonJobs = useLessonJobs(true, lessonsSignature);
  const jobLesson = jobId ? lessonJobs?.get(jobId) : undefined;

  // The prompt of a refine run: the lesson prompt is offered first for a job that holds a lesson;
  // what the operator picks holds for the job it was picked for.
  const [promptPick, setPromptPick] = useState<RefinePromptPick | null>(null);
  const refinePrompt = refinePromptFor(jobId, job?.lesson, promptPick);

  const debouncedRevision = useDebouncedValue(jobSnapshotRevision, 3000);
  const jobFiles = useJobProjectFiles(jobId, debouncedRevision);
  const jobChunkGroups = useJobChunkArtifactGroups(jobId, debouncedRevision);
  const languageShare = useJobLanguageShare(jobId, debouncedRevision);

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
              setPackageDate(null);
              setNotesDate(null);
              setActiveStep('upload');
            }}
          >
            {t('upload')}
          </button>
          <button
            type="button"
            className={
              showLessons && !lessonOpen
                ? 'topbar__lessons topbar__lessons--on'
                : 'topbar__lessons'
            }
            onClick={() => {
              // From an open package the button leads to the lessons, whatever was under the package.
              setShowLessons((v) => (lessonOpen ? true : !v));
              setPackageDate(null);
              setNotesDate(null);
            }}
            aria-pressed={showLessons && !lessonOpen}
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
        <JobsSidebar
          jobs={jobsList}
          currentJobId={jobId}
          onSelectJob={openJob}
          onRefresh={refreshList}
          onDelete={handleDeleteJob}
          loading={loadingList}
          t={t}
          lessonJobs={lessonJobs}
          onOpenPackage={openPackage}
          onOpenNotes={openNotes}
        />

        <main className="content">
          {packageDate && (
            <LessonPackageView
              date={packageDate}
              t={t}
              onClose={() => setPackageDate(null)}
              onOpenJob={openJob}
            />
          )}
          {notesDate && (
            <LessonNotesView
              date={notesDate}
              t={t}
              onClose={() => setNotesDate(null)}
            />
          )}
          {/* Kept under an open package, so closing the package shows the lessons as they were left. */}
          {showLessons && (
            <div style={{ display: lessonOpen ? 'none' : 'contents' }}>
              <LessonsPanel
                t={t}
                onOpenJob={openJob}
                onJobsChanged={refreshList}
                onLessonsChanged={lessonsChanged}
                onOpenPackage={openPackage}
                onOpenNotes={openNotes}
              />
            </div>
          )}
          <div
            className="steps-row"
            style={showLessons || lessonOpen ? { display: 'none' } : undefined}
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

          <div
            className="step-content"
            style={showLessons || lessonOpen ? { display: 'none' } : undefined}
          >
            {/* Scrolled with the step: in a low window they must not take the room of the step itself. */}
            {activeStep !== 'upload' && jobLesson && (
              <p className="job-lesson" data-testid="job-lesson">
                <span>
                  {t('jobLesson')} {jobLesson.date} · {t('jobLessonRecording')}{' '}
                  {jobLesson.time}
                </span>
                <LessonPageLinks
                  pages={jobLesson.pages}
                  t={t}
                  packageDate={jobLesson.hasPackage ? jobLesson.date : null}
                  onOpenPackage={openPackage}
                  notes={
                    jobLesson.notes
                      ? { date: jobLesson.date, ...jobLesson.notes }
                      : null
                  }
                  onOpenNotes={openNotes}
                />
              </p>
            )}
            {activeStep !== 'upload' &&
              runSettingsLine(job?.runSettings, t) && (
                <p className="job-run-settings" data-testid="job-run-settings">
                  {runSettingsLine(job?.runSettings, t)}
                </p>
              )}
            {activeStep !== 'upload' && (
              <JobStats report={languageShare} t={t} />
            )}

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
                    <div className="dock dock--refiner">
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
                        <div
                          className="refiner-prompt"
                          role="radiogroup"
                          aria-label={t('refinerPromptLabel')}
                          data-testid="refiner-prompt"
                        >
                          <span className="refiner-prompt__label">
                            {t('refinerPromptLabel')}
                          </span>
                          {(['lesson', 'saved'] as const).map((p) => (
                            <label key={p} className="refiner-prompt__option">
                              <input
                                type="radio"
                                name="refiner-prompt"
                                value={p}
                                checked={refinePrompt === p}
                                disabled={job.phase === 'refiner'}
                                onChange={() =>
                                  setPromptPick({ jobId, prompt: p })
                                }
                              />
                              {t(
                                p === 'lesson'
                                  ? 'refinerPromptLesson'
                                  : 'refinerPromptSaved'
                              )}
                            </label>
                          ))}
                          <p
                            className="refiner-prompt__hint"
                            data-testid="refiner-prompt-hint"
                          >
                            {t(
                              refinePrompt === 'lesson'
                                ? 'refinerPromptLessonHint'
                                : 'refinerPromptSavedHint'
                            )}
                          </p>
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
                                  <span className="refiner-panel__arrow">
                                    →
                                  </span>
                                  <code className="refiner-panel__file refiner-panel__file--out">
                                    transcript_fixed_&lt;
                                    {t('refinerOutputTime')}
                                    &gt;.md
                                  </code>
                                </div>
                                <button
                                  type="button"
                                  className="refiner-actions__run"
                                  disabled={job.phase === 'refiner'}
                                  onClick={() =>
                                    startRefiner(false, f.name, refinePrompt)
                                  }
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
        /* The steps stand in one row while there is room for it and go under each other in a narrow window. */
        .steps-row {
          flex-shrink: 0;
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(min(100%, 200px), 1fr));
          gap: 0.5rem;
          margin-bottom: 1rem;
        }
        /*
         * A band of the page whose panels stand side by side while there is room and go under each other
         * when there is not. The page stays a stack of such bands; only the inside of a band is columns.
         */
        .dock {
          display: flex;
          flex-wrap: wrap;
          align-items: stretch;
          gap: 1rem;
          margin: 0 0 1rem 0;
        }
        .dock > * { min-width: 0; }
        .dock--refiner > .refiner-settings { flex: 2.2 1 900px; margin: 0; }
        .dock--refiner > .refiner-panel { flex: 1 1 460px; margin: 0; }
        .step-content {
          flex: 1;
          min-height: 0;
          overflow-x: hidden;
          overflow-y: auto;
        }
        .step-panel { padding: 1rem; }
        .step-panel__meta { margin: 0 0 0.5rem 0; font-size: 0.875rem; color: var(--color-text-secondary); }
        .job-lesson { display: flex; flex-wrap: wrap; align-items: center; gap: 0.6rem; margin: 0 0 0.4rem 0; font-size: 0.875rem; color: var(--color-heading); font-variant-numeric: tabular-nums; }
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
        .refiner-prompt {
          display: flex;
          flex-wrap: wrap;
          align-items: center;
          gap: 0.3rem 1rem;
          margin: 0 0 0.7rem 0;
          font-size: 0.85rem;
          color: var(--color-text);
        }
        .refiner-prompt__label { font-size: 0.8rem; color: var(--color-text-secondary); }
        .refiner-prompt__option { display: inline-flex; align-items: center; gap: 0.35rem; cursor: pointer; }
        .refiner-prompt__hint { flex-basis: 100%; margin: 0; font-size: 0.8rem; color: var(--color-text-secondary); }
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
