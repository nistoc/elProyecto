import { useCallback, useEffect, useState } from 'react';
import {
  createDialogue,
  fetchLessons,
  putLessonPage,
  removeLessonPage,
  startTrackRun,
} from '../api';
import {
  buildJobSettings,
  emptyJobSettingsForm,
  type JobSettingsForm,
} from '../utils/jobSettings';
import {
  canBuildDialogue,
  defaultDialogueTitle,
  dialogueCandidates,
  estimateRunUsd,
  formatBytes,
  formatDuration,
  formatUsd,
  lessonPageLabel,
  periodDurationSec,
  periodTimeLabel,
  runUsdPerMinute,
  sessionCounts,
  sessionDurationSec,
  trackDefaultModel,
  type LessonPage,
  type LessonPeriod,
  type LessonSession,
  type LessonTrack,
  type LessonsResponse,
} from '../utils/lessons';
import { languageShareLines, languageShareShort } from '../utils/languageShare';
import { runSettingsLine, runSettingsShort } from '../utils/runSettings';
import { JobSettingsFields } from './JobSettingsFields';
import { LessonPageLinks } from './LessonPageLinks';

interface LessonsPanelProps {
  t: (key: string) => string;
  /** Opens a job in the main view (a run of a track or a dialogue). */
  onOpenJob: (jobId: string) => void;
  /** A job was created from here: the jobs list has to be read again. */
  onJobsChanged: () => void;
  /** The page of a lesson was put or removed here: whoever shows lessons has to read them again. */
  onLessonsChanged?: () => void;
}

/** What the operator typed for the page of one lesson. */
interface PageForm {
  sessionId: string;
  url: string;
  title: string;
}

/** What the operator typed for the dialogue of one period. */
interface DialogueForm {
  periodId: string;
  title: string;
  /** Per participant track: the name to print and the run whose transcript is taken. */
  tracks: Record<string, { speaker: string; jobId: string }>;
}

function fill(text: string, values: Record<string, string>): string {
  return Object.entries(values).reduce(
    (acc, [k, v]) => acc.replace(`{${k}}`, v),
    text
  );
}

/**
 * Lessons of the recordings folder: lesson → period → track with the jobs that transcribed it.
 * A track is transcribed from here (paid, after the price estimate is shown), and the transcripts of
 * participant tracks are merged into a dialogue (free). The recordings folder itself is only read.
 */
export function LessonsPanel({
  t,
  onOpenJob,
  onJobsChanged,
  onLessonsChanged,
}: LessonsPanelProps) {
  const [data, setData] = useState<LessonsResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState<Record<string, boolean>>({});
  // The track whose paid run waits for a confirmation.
  const [confirmTrack, setConfirmTrack] = useState<string | null>(null);
  const [settingsForm, setSettingsForm] =
    useState<JobSettingsForm>(emptyJobSettingsForm);
  const [dialogueForm, setDialogueForm] = useState<DialogueForm | null>(null);
  // The lesson whose page link is being put or removed.
  const [pageForm, setPageForm] = useState<PageForm | null>(null);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetchLessons();
      setData(response);
      // The newest lesson is open at first sight; the rest open on click.
      setOpen((prev) =>
        Object.keys(prev).length > 0 || response.sessions.length === 0
          ? prev
          : { [response.sessions[0].id]: true }
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const builtSettings = buildJobSettings(settingsForm);

  const askRun = (track: LessonTrack) => {
    setActionError(null);
    setDialogueForm(null);
    setPageForm(null);
    setSettingsForm(emptyJobSettingsForm);
    setConfirmTrack(track.id);
  };

  const askPage = (session: LessonSession) => {
    setActionError(null);
    setConfirmTrack(null);
    setDialogueForm(null);
    setPageForm((prev) =>
      prev?.sessionId === session.id
        ? null
        : { sessionId: session.id, url: '', title: '' }
    );
  };

  /** The pages go by the date of the lesson: every lesson of that date shows the same ones. */
  const showPages = (date: string, pages: LessonPage[]) => {
    setData((prev) =>
      prev
        ? {
            ...prev,
            sessions: prev.sessions.map((s) =>
              s.date === date ? { ...s, pages } : s
            ),
          }
        : prev
    );
    onLessonsChanged?.();
  };

  const savePage = async (session: LessonSession) => {
    if (!pageForm || busy || !pageForm.url.trim()) return;
    setBusy(true);
    setActionError(null);
    try {
      const saved = await putLessonPage(
        session.date,
        pageForm.url.trim(),
        pageForm.title
      );
      showPages(saved.date, saved.pages);
      setPageForm(null);
    } catch (e) {
      setActionError(
        `${t('lessonPageFailed')}: ${e instanceof Error ? e.message : String(e)}`
      );
    } finally {
      setBusy(false);
    }
  };

  const removePage = async (session: LessonSession, page: LessonPage) => {
    if (busy) return;
    setBusy(true);
    setActionError(null);
    try {
      const left = await removeLessonPage(session.date, page.url);
      showPages(left.date, left.pages);
    } catch (e) {
      setActionError(
        `${t('lessonPageFailed')}: ${e instanceof Error ? e.message : String(e)}`
      );
    } finally {
      setBusy(false);
    }
  };

  const startRun = async (track: LessonTrack) => {
    if (!builtSettings.ok || busy) return;
    setBusy(true);
    setActionError(null);
    try {
      const { jobId } = await startTrackRun(track.id, builtSettings.settings);
      setConfirmTrack(null);
      onJobsChanged();
      onOpenJob(jobId);
    } catch (e) {
      setActionError(
        `${t('lessonsRunFailed')}: ${e instanceof Error ? e.message : String(e)}`
      );
    } finally {
      setBusy(false);
    }
  };

  const askDialogue = (session: LessonSession, period: LessonPeriod) => {
    setActionError(null);
    setConfirmTrack(null);
    setPageForm(null);
    const tracks: DialogueForm['tracks'] = {};
    for (const c of dialogueCandidates(period))
      tracks[c.trackId] = { speaker: c.speaker, jobId: c.jobId };
    setDialogueForm({
      periodId: period.id,
      title: defaultDialogueTitle(session, period),
      tracks,
    });
  };

  const buildDialogue = async () => {
    if (!dialogueForm || busy) return;
    setBusy(true);
    setActionError(null);
    try {
      const { jobId } = await createDialogue(
        dialogueForm.title.trim(),
        Object.values(dialogueForm.tracks).map((x) => ({
          jobId: x.jobId,
          speaker: x.speaker.trim(),
        }))
      );
      setDialogueForm(null);
      onJobsChanged();
      onOpenJob(jobId);
    } catch (e) {
      setActionError(
        `${t('lessonsDialogueFailed')}: ${e instanceof Error ? e.message : String(e)}`
      );
    } finally {
      setBusy(false);
    }
  };

  const renderTrack = (track: LessonTrack) => {
    const confirming = confirmTrack === track.id;
    // The price follows the model the run will go to: the one chosen in the open form, else the track's own.
    const defaultModel = trackDefaultModel(track, data);
    const rate = runUsdPerMinute(
      track,
      confirming ? settingsForm.model : '',
      data
    );
    const estimate = estimateRunUsd(track.durationSec, rate);
    return (
      <li key={track.id} className="lessons__track" data-testid="lesson-track">
        <div className="lessons__track-row">
          <span className="lessons__track-name" title={track.fileName}>
            {track.kind === 'mixed'
              ? t('lessonsTrackMixed')
              : track.participant || track.fileName}
          </span>
          <span className="lessons__muted">
            {formatDuration(track.durationSec)} · {formatBytes(track.sizeBytes)}
          </span>
          <span className="lessons__runs">
            {track.runs.length === 0 && (
              <span className="lessons__muted">{t('lessonsNoRuns')}</span>
            )}
            {track.runs.map((r) => (
              <button
                key={r.jobId}
                type="button"
                className="lessons__chip"
                onClick={() => onOpenJob(r.jobId)}
                title={[
                  `${t('lessonsOpenJob')} · ${r.createdAt ?? ''}`,
                  runSettingsLine(r.settings, t),
                  languageShareLines({ total: r.share }, t)[0],
                ]
                  .filter(Boolean)
                  .join('\n')}
                data-testid="lesson-run"
              >
                {r.jobId} · {r.status}
                {r.hasTranscript ? ` · ${t('lessonsHasTranscript')}` : ''}
                {r.hasRefined ? ` · ${t('lessonsHasRefined')}` : ''}
                {runSettingsShort(r.settings, t) && (
                  <span className="lessons__chip-settings">
                    {' · '}
                    {runSettingsShort(r.settings, t)}
                  </span>
                )}
                {languageShareShort(r.share) && (
                  <span
                    className="lessons__chip-settings"
                    data-testid="lesson-run-share"
                  >
                    {' · '}
                    {languageShareShort(r.share)}
                  </span>
                )}
              </button>
            ))}
          </span>
          <button
            type="button"
            className="lessons__btn"
            onClick={() => (confirming ? setConfirmTrack(null) : askRun(track))}
            disabled={busy}
            data-testid="lesson-track-transcribe"
          >
            {t('lessonsTranscribe')} · ≤ {formatUsd(estimate)}
          </button>
        </div>
        {confirming && (
          <div className="lessons__confirm" role="group">
            <p className="lessons__confirm-text">
              {fill(t('lessonsRunConfirmText'), {
                usd: formatUsd(estimate),
                duration: formatDuration(track.durationSec),
                rate: String(rate),
              })}
            </p>
            {defaultModel && !settingsForm.model.trim() && (
              <p
                className="lessons__confirm-text"
                data-testid="lesson-track-model-note"
              >
                {fill(t('lessonsRunTrackModelNote'), { model: defaultModel })}
              </p>
            )}
            <JobSettingsFields
              form={settingsForm}
              onChange={(p) => setSettingsForm((prev) => ({ ...prev, ...p }))}
              t={t}
              defaultModel={defaultModel}
            />
            {actionError && <p className="lessons__error">{actionError}</p>}
            <div className="lessons__confirm-actions">
              <button
                type="button"
                className="lessons__btn lessons__btn--paid"
                onClick={() => void startRun(track)}
                disabled={busy || !builtSettings.ok}
                data-testid="lesson-track-start-paid"
              >
                {t('lessonsRunStart')} · ≤ {formatUsd(estimate)}
              </button>
              <button
                type="button"
                className="lessons__btn"
                onClick={() => setConfirmTrack(null)}
                disabled={busy}
              >
                {t('lessonsCancel')}
              </button>
            </div>
          </div>
        )}
      </li>
    );
  };

  const renderDialogueForm = (period: LessonPeriod) => {
    if (!dialogueForm || dialogueForm.periodId !== period.id) return null;
    const candidates = dialogueCandidates(period);
    const ready =
      dialogueForm.title.trim() !== '' &&
      candidates.every(
        (c) => (dialogueForm.tracks[c.trackId]?.speaker ?? '').trim() !== ''
      );
    const patchTrack = (
      trackId: string,
      patch: Partial<{ speaker: string; jobId: string }>
    ) =>
      setDialogueForm((prev) =>
        prev
          ? {
              ...prev,
              tracks: {
                ...prev.tracks,
                [trackId]: { ...prev.tracks[trackId], ...patch },
              },
            }
          : prev
      );
    return (
      <div className="lessons__confirm" role="group">
        <p className="lessons__confirm-text">{t('lessonsDialogueHint')}</p>
        <label className="lessons__field">
          <span>{t('lessonsDialogueTitle')}</span>
          <input
            type="text"
            value={dialogueForm.title}
            onChange={(e) =>
              setDialogueForm((prev) =>
                prev ? { ...prev, title: e.target.value } : prev
              )
            }
          />
        </label>
        {candidates.map((c) => (
          <label key={c.trackId} className="lessons__field">
            <span>{t('lessonsDialogueSpeaker')}</span>
            <input
              type="text"
              value={dialogueForm.tracks[c.trackId]?.speaker ?? ''}
              onChange={(e) =>
                patchTrack(c.trackId, { speaker: e.target.value })
              }
            />
            <select
              value={dialogueForm.tracks[c.trackId]?.jobId ?? c.jobId}
              onChange={(e) => patchTrack(c.trackId, { jobId: e.target.value })}
              aria-label={t('lessonsDialogueRun')}
            >
              {c.runs.map((r) => (
                <option key={r.jobId} value={r.jobId}>
                  {r.jobId} · {(r.createdAt ?? '').slice(0, 16)}
                  {runSettingsShort(r.settings, t)
                    ? ` · ${runSettingsShort(r.settings, t)}`
                    : ''}
                </option>
              ))}
            </select>
          </label>
        ))}
        {actionError && <p className="lessons__error">{actionError}</p>}
        <div className="lessons__confirm-actions">
          <button
            type="button"
            className="lessons__btn lessons__btn--primary"
            onClick={() => void buildDialogue()}
            disabled={busy || !ready}
            data-testid="lesson-dialogue-build"
          >
            {t('lessonsDialogueCreate')}
          </button>
          <button
            type="button"
            className="lessons__btn"
            onClick={() => setDialogueForm(null)}
            disabled={busy}
          >
            {t('lessonsCancel')}
          </button>
        </div>
      </div>
    );
  };

  const renderPeriod = (session: LessonSession, period: LessonPeriod) => (
    <li key={period.id} className="lessons__period" data-testid="lesson-period">
      <div className="lessons__period-head">
        <strong>{periodTimeLabel(period)}</strong>
        <span className="lessons__muted">
          {formatDuration(periodDurationSec(period))}
        </span>
        {period.state === 'unconverted' && (
          <span
            className="lessons__badge lessons__badge--warn"
            title={t('lessonsStateUnconvertedHint')}
          >
            {t('lessonsStateUnconverted')}
          </span>
        )}
        {period.state === 'empty' && (
          <span className="lessons__badge lessons__badge--warn">
            {t('lessonsStateEmpty')}
          </span>
        )}
        {period.hasVideo && (
          <span className="lessons__badge">{t('lessonsVideo')}</span>
        )}
        {period.hasChat && (
          <span className="lessons__badge">{t('lessonsChat')}</span>
        )}
        {period.hasSubtitles && (
          <span className="lessons__badge">{t('lessonsSubtitles')}</span>
        )}
        {period.hasOldTranscript && (
          <span className="lessons__badge">{t('lessonsOldTranscript')}</span>
        )}
        {period.rawZoomBytes > 0 && (
          <span className="lessons__badge" title={t('lessonsRawZoomHint')}>
            {t('lessonsRawZoom')} {formatBytes(period.rawZoomBytes)}
          </span>
        )}
      </div>
      {period.tracks.length > 0 && (
        <ul className="lessons__tracks">{period.tracks.map(renderTrack)}</ul>
      )}
      {period.tracks.some((x) => x.kind === 'participant') && (
        <div className="lessons__dialogues">
          <span className="lessons__muted">{t('lessonsDialogues')}:</span>
          {period.dialogues.length === 0 && (
            <span className="lessons__muted">—</span>
          )}
          {period.dialogues.map((d) => (
            <button
              key={d.jobId}
              type="button"
              className="lessons__chip"
              onClick={() => onOpenJob(d.jobId)}
              title={`${t('lessonsOpenJob')} · ${d.trackJobIds.join(' + ')}`}
            >
              {d.title || d.jobId}
            </button>
          ))}
          <button
            type="button"
            className="lessons__btn"
            onClick={() =>
              dialogueForm?.periodId === period.id
                ? setDialogueForm(null)
                : askDialogue(session, period)
            }
            disabled={busy || !canBuildDialogue(period)}
            title={
              canBuildDialogue(period)
                ? t('lessonsDialogueHint')
                : t('lessonsDialogueNeedsRuns')
            }
            data-testid="lesson-dialogue-open"
          >
            {t('lessonsBuildDialogue')}
          </button>
        </div>
      )}
      {renderDialogueForm(period)}
    </li>
  );

  const renderPageForm = (session: LessonSession) => {
    if (pageForm?.sessionId !== session.id) return null;
    return (
      <div
        className="lessons__confirm lessons__page-form"
        role="group"
        data-testid="lesson-page-form"
      >
        <p className="lessons__confirm-text">{t('lessonPageHint')}</p>
        {(session.pages ?? []).map((page) => (
          <div key={page.url} className="lessons__field">
            <span>{lessonPageLabel(page, t('lessonPage'))}</span>
            <code className="lessons__page-url">{page.url}</code>
            <button
              type="button"
              className="lessons__btn"
              onClick={() => void removePage(session, page)}
              disabled={busy}
              data-testid="lesson-page-remove"
            >
              {t('lessonPageRemove')}
            </button>
          </div>
        ))}
        <label className="lessons__field">
          <span>{t('lessonPageUrl')}</span>
          <input
            type="url"
            className="lessons__page-input"
            value={pageForm.url}
            placeholder="https://…"
            onChange={(e) => setPageForm({ ...pageForm, url: e.target.value })}
            data-testid="lesson-page-url"
          />
        </label>
        <label className="lessons__field">
          <span>{t('lessonPageTitle')}</span>
          <input
            type="text"
            value={pageForm.title}
            placeholder={t('lessonPage')}
            maxLength={120}
            onChange={(e) =>
              setPageForm({ ...pageForm, title: e.target.value })
            }
            data-testid="lesson-page-title"
          />
        </label>
        {actionError && <p className="lessons__error">{actionError}</p>}
        <div className="lessons__confirm-actions">
          <button
            type="button"
            className="lessons__btn lessons__btn--primary"
            onClick={() => void savePage(session)}
            disabled={busy || !pageForm.url.trim()}
            data-testid="lesson-page-save"
          >
            {t('lessonPageSave')}
          </button>
          <button
            type="button"
            className="lessons__btn"
            onClick={() => setPageForm(null)}
            disabled={busy}
          >
            {t('lessonsCancel')}
          </button>
        </div>
      </div>
    );
  };

  const renderSession = (session: LessonSession) => {
    const counts = sessionCounts(session);
    const isOpen = !!open[session.id];
    return (
      <li
        key={session.id}
        className="lessons__session"
        data-testid="lesson-session"
      >
        <div className="lessons__session-row">
          <button
            type="button"
            className="lessons__session-head"
            onClick={() =>
              setOpen((prev) => ({ ...prev, [session.id]: !isOpen }))
            }
            aria-expanded={isOpen}
          >
            <span className="lessons__caret">{isOpen ? '▾' : '▸'}</span>
            <strong>{session.date}</strong>
            {session.title && <span>{session.title}</span>}
            <span className="lessons__muted">
              {counts.periods} {t('lessonsPeriods')} ·{' '}
              {formatDuration(sessionDurationSec(session))}
            </span>
            {session.loose && (
              <span
                className="lessons__badge lessons__badge--warn"
                title={t('lessonsLooseHint')}
              >
                {t('lessonsLoose')}
              </span>
            )}
            {counts.unconverted > 0 && (
              <span
                className="lessons__badge lessons__badge--warn"
                title={t('lessonsStateUnconvertedHint')}
              >
                {t('lessonsStateUnconverted')}: {counts.unconverted}
              </span>
            )}
            <span className="lessons__muted lessons__session-counts">
              {t('lessonsRuns')}: {counts.runs} ({counts.tracksWithRuns}/
              {counts.tracks}) · {t('lessonsDialogues')}: {counts.dialogues}
            </span>
          </button>
          {/* Next to the head, not in it: a link cannot stand inside a button. */}
          <span className="lessons__session-pages">
            <LessonPageLinks pages={session.pages} t={t} />
            <button
              type="button"
              className="lessons__btn"
              onClick={() => askPage(session)}
              title={t('lessonPageHint')}
              aria-expanded={pageForm?.sessionId === session.id}
              data-testid="lesson-page-edit"
            >
              {t('lessonPageEdit')}
            </button>
          </span>
        </div>
        {renderPageForm(session)}
        {isOpen && (
          <ul className="lessons__periods">
            {session.periods.length === 0 && (
              <li className="lessons__muted">{t('lessonsNoPeriods')}</li>
            )}
            {session.periods.map((p) => renderPeriod(session, p))}
          </ul>
        )}
      </li>
    );
  };

  return (
    <section className="lessons" data-testid="lessons-panel">
      <header className="lessons__header">
        <h2 className="lessons__title">{t('lessonsTitle')}</h2>
        {data?.rootPath && (
          <code className="lessons__root">{data.rootPath}</code>
        )}
        <button
          type="button"
          className="lessons__btn"
          onClick={() => void load()}
          disabled={loading}
        >
          {t('refresh')}
        </button>
      </header>
      <p className="lessons__note">{t('lessonsReadOnlyNote')}</p>
      {loading && !data && (
        <p className="lessons__muted">{t('lessonsLoading')}</p>
      )}
      {error && <p className="lessons__error">{error}</p>}
      {data && !data.enabled && (
        <p className="lessons__muted">{t('lessonsDisabled')}</p>
      )}
      {data?.enabled && data.sessions.length === 0 && (
        <p className="lessons__muted">{t('lessonsEmpty')}</p>
      )}
      {data?.enabled && (
        <ul className="lessons__sessions">
          {data.sessions.map(renderSession)}
        </ul>
      )}
      <style>{`
        .lessons {
          flex: 1;
          min-height: 0;
          overflow-y: auto;
          padding: 1rem 1.25rem;
          border: 1px solid var(--color-border);
          border-radius: 8px;
          background: var(--color-surface);
          color: var(--color-text);
        }
        .lessons__header { display: flex; flex-wrap: wrap; align-items: center; gap: 0.75rem; }
        .lessons__title { margin: 0; font-size: 1.1rem; color: var(--color-heading); }
        .lessons__root { font-size: 0.8rem; color: var(--color-text-secondary); }
        .lessons__note { margin: 0.5rem 0 0.75rem; font-size: 0.875rem; color: var(--color-text-secondary); }
        .lessons__muted { color: var(--color-text-secondary); font-size: 0.875rem; }
        .lessons__error { color: var(--color-error); font-size: 0.875rem; margin: 0.5rem 0; }
        .lessons__sessions, .lessons__periods, .lessons__tracks { list-style: none; margin: 0; padding: 0; }
        .lessons__session { border-top: 1px solid var(--color-border); }
        .lessons__session-row { display: flex; flex-wrap: wrap; align-items: center; gap: 0.25rem 0.6rem; }
        .lessons__session-pages { display: inline-flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; padding-right: 0.25rem; }
        .lessons__page-form { margin: 0 0 0.6rem 1.5rem; }
        .lessons__page-url { flex: 1; min-width: 12rem; font-size: 0.8rem; color: var(--color-text-secondary); overflow-wrap: anywhere; }
        .lessons__field input.lessons__page-input { flex: 1; min-width: min(100%, 28rem); }
        .lessons__session-head {
          flex: 1;
          min-width: min(100%, 24rem);
          display: flex;
          flex-wrap: wrap;
          align-items: baseline;
          gap: 0.6rem;
          padding: 0.55rem 0.25rem;
          border: none;
          background: none;
          color: var(--color-text);
          font: inherit;
          text-align: left;
          cursor: pointer;
        }
        .lessons__session-head:hover { background: var(--color-surface-hover); }
        .lessons__session-counts { margin-left: auto; }
        .lessons__caret { width: 1rem; color: var(--color-text-secondary); }
        /* The recordings of a lesson stand side by side while there is room. */
        .lessons__periods {
          padding: 0.5rem 0 0.75rem 1.5rem;
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(min(100%, 640px), 1fr));
          align-items: start;
          gap: 0.5rem 0.75rem;
        }
        .lessons__period {
          min-width: 0;
          padding: 0.6rem 0.75rem;
          border: 1px solid var(--color-border);
          border-radius: 6px;
          background: var(--color-surface-sunken);
        }
        .lessons__period-head { display: flex; flex-wrap: wrap; align-items: baseline; gap: 0.5rem; }
        .lessons__badge {
          padding: 0.05rem 0.45rem;
          border: 1px solid var(--color-border-strong);
          border-radius: 999px;
          font-size: 0.75rem;
          color: var(--color-text-secondary);
          white-space: nowrap;
        }
        .lessons__badge--warn { border-color: var(--color-error); color: var(--color-error-muted); }
        .lessons__tracks { margin-top: 0.4rem; }
        .lessons__track { padding: 0.3rem 0; border-top: 1px dashed var(--color-border); }
        .lessons__track-row { display: flex; flex-wrap: wrap; align-items: center; gap: 0.6rem; }
        .lessons__track-name { min-width: 11rem; font-weight: 500; }
        .lessons__runs { flex: 1; display: flex; flex-wrap: wrap; gap: 0.35rem; min-width: 12rem; }
        .lessons__chip-settings { opacity: 0.75; }
        .lessons__chip {
          padding: 0.1rem 0.5rem;
          border: 1px solid var(--color-border-strong);
          border-radius: 999px;
          background: var(--color-accent-soft);
          color: var(--color-link);
          font-size: 0.8rem;
          cursor: pointer;
        }
        .lessons__chip:hover { border-color: var(--color-primary); }
        .lessons__btn {
          padding: 0.25rem 0.6rem;
          border: 1px solid var(--color-border-strong);
          border-radius: 6px;
          background: var(--color-surface);
          color: var(--color-text);
          font-size: 0.85rem;
          cursor: pointer;
          white-space: nowrap;
        }
        .lessons__btn:hover:not(:disabled) { background: var(--color-surface-hover); }
        .lessons__btn:disabled { opacity: 0.5; cursor: not-allowed; }
        .lessons__btn--primary,
        .lessons__btn--paid {
          background: var(--color-primary);
          border-color: var(--color-primary);
          color: var(--color-on-primary);
        }
        .lessons__btn--primary:hover:not(:disabled),
        .lessons__btn--paid:hover:not(:disabled) { background: var(--color-primary-hover); }
        .lessons__dialogues { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; margin-top: 0.5rem; }
        .lessons__confirm {
          margin-top: 0.5rem;
          padding: 0.6rem 0.75rem;
          border: 1px solid var(--color-border-strong);
          border-radius: 6px;
          background: var(--color-surface);
        }
        .lessons__confirm-text { margin: 0 0 0.4rem; font-size: 0.875rem; }
        .lessons__confirm-actions { display: flex; gap: 0.5rem; margin-top: 0.5rem; }
        .lessons__field { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; margin-top: 0.4rem; }
        .lessons__field span { min-width: 10rem; font-size: 0.875rem; }
        .lessons__field input,
        .lessons__field select {
          padding: 0.25rem 0.5rem;
          border: 1px solid var(--color-border);
          border-radius: 4px;
          background: var(--color-surface);
          color: var(--color-text);
          min-width: 14rem;
        }
      `}</style>
    </section>
  );
}
