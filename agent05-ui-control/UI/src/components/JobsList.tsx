import { Fragment, memo, useMemo, useState } from 'react';
import type { JobListItem } from '../types';
import {
  lessonGroupHeads,
  nextJobsSort,
  type JobsSort,
  type JobsSortKey,
} from '../utils/jobsSort';
import { languageShareShort } from '../utils/languageShare';
import type { LessonJobInfo } from '../utils/lessons';
import { runSettingsShort } from '../utils/runSettings';
import type { SidebarDetail } from '../utils/sidebarWidth';
import { LessonPageLinks } from './LessonPageLinks';

// Made once: the list prints two dates for every job, and a format made for each of them is slow.
const dateTimeFormat = new Intl.DateTimeFormat(undefined, {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
});

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '';
  try {
    return dateTimeFormat.format(new Date(iso));
  } catch {
    return iso;
  }
}

interface JobsListProps {
  jobs: JobListItem[];
  currentJobId: string | null;
  onSelectJob: (id: string) => void;
  onRefresh: () => void;
  onDelete: (id: string) => void;
  loading: boolean;
  t: (key: string) => string;
  /** How much to tell about a job; follows the width of the list (see sidebarDetail). */
  detail?: SidebarDetail;
  /** What the lessons list knows of the jobs, by job id; shown from detail 2. */
  lessonJobs?: Map<string, LessonJobInfo> | null;
  /** The order `jobs` came in; with `onSort` the list offers to change it. */
  sort?: JobsSort;
  onSort?: (sort: JobsSort) => void;
  /** Opens the package of a lesson (by its date) inside the app; the heading of a lesson offers it. */
  onOpenPackage?: (date: string) => void;
  /** Opens the notes of a lesson (by its date; homework first) inside the app. */
  onOpenNotes?: (date: string) => void;
}

/**
 * "2026-10-23 20:21 · Maria Lopez" — the recording a job belongs to and what of it the job holds.
 * The title of a dialogue is left out when it is the name the row already shows.
 */
function lessonPlace(
  info: LessonJobInfo,
  jobName: string,
  t: (key: string) => string
): string {
  const title = info.label && info.label !== jobName.trim() ? info.label : '';
  const what =
    info.kind === 'dialogue'
      ? [t('jobsKindDialogue'), title].filter(Boolean).join(': ')
      : info.kind === 'mixed'
        ? t('lessonsTrackMixed')
        : info.label;
  return [`${info.date} ${info.time}`.trim(), what].filter(Boolean).join(' · ');
}

/** "pause 1.0 s · gpt-transcribe · es 74 % · ru 18 % · transcript · refined"; empty when nothing is known. */
function lessonFacts(info: LessonJobInfo, t: (key: string) => string): string {
  return [
    runSettingsShort(info.settings, t),
    languageShareShort(info.share),
    info.hasTranscript ? t('lessonsHasTranscript') : '',
    info.hasRefined ? t('lessonsHasRefined') : '',
  ]
    .filter(Boolean)
    .join(' · ');
}

function JobsListView({
  jobs,
  currentJobId,
  onSelectJob,
  onRefresh,
  onDelete,
  loading,
  t,
  detail = 0,
  lessonJobs = null,
  sort,
  onSort,
  onOpenPackage,
  onOpenNotes,
}: JobsListProps) {
  const [confirmId, setConfirmId] = useState<string | null>(null);

  // A list ordered by lesson gets a heading above the first job of every lesson.
  const byLesson = sort?.key === 'lesson';
  const groupHeads = useMemo(
    () => (byLesson && lessonJobs ? lessonGroupHeads(jobs, lessonJobs) : null),
    [byLesson, jobs, lessonJobs]
  );

  const arrow = sort?.dir === 'asc' ? '↑' : '↓';
  const dirWord = t(sort?.dir === 'asc' ? 'jobsSortOldest' : 'jobsSortNewest');
  /** The name of a column the list can be ordered by: a click orders by it, a second one turns it over. */
  const columnName = (key: JobsSortKey, label: string) =>
    sort && onSort ? (
      <button
        type="button"
        className={
          sort.key === key
            ? 'jobs-list__col-sort jobs-list__col-sort--on'
            : 'jobs-list__col-sort'
        }
        onClick={() => onSort(nextJobsSort(sort, key))}
        title={t(key === 'lesson' ? 'jobsSortLesson' : 'jobsSortCreated')}
        aria-pressed={sort.key === key}
      >
        {label}
        {sort.key === key && <span aria-hidden="true"> {arrow}</span>}
      </button>
    ) : (
      <span>{label}</span>
    );

  return (
    <div
      className={`jobs-list jobs-list--d${detail}`}
      data-testid="jobs-list"
      data-detail={detail}
    >
      <div className="jobs-list__header">
        <h2 className="jobs-list__title">
          {t('jobs')}
          {detail >= 1 && jobs.length > 0 && (
            <span className="jobs-list__count"> · {jobs.length}</span>
          )}
        </h2>
        <button
          type="button"
          onClick={onRefresh}
          disabled={loading}
          className="jobs-list__refresh"
        >
          {t('refresh')}
        </button>
      </div>
      {sort && onSort && jobs.length > 0 && (
        <div className="jobs-list__sort" data-testid="jobs-list-sort">
          <select
            className="jobs-list__sort-key"
            value={sort.key}
            onChange={(e) =>
              onSort({ ...sort, key: e.target.value as JobsSortKey })
            }
            aria-label={t('jobsSort')}
            title={t('jobsSort')}
          >
            <option value="lesson">{t('jobsSortLesson')}</option>
            <option value="created">{t('jobsSortCreated')}</option>
          </select>
          <button
            type="button"
            className="jobs-list__sort-dir"
            onClick={() => onSort(nextJobsSort(sort, sort.key))}
            title={t('jobsSortTurn')}
            aria-label={`${dirWord}. ${t('jobsSortTurn')}`}
            data-dir={sort.dir}
          >
            <span aria-hidden="true">{arrow}</span>
            <span className="jobs-list__sort-word">{dirWord}</span>
          </button>
        </div>
      )}
      {loading && <p className="jobs-list__loading">…</p>}
      {detail >= 3 && jobs.length > 0 && (
        <div className="jobs-list__columns">
          <span>{t('jobsColJob')}</span>
          {columnName('lesson', t('jobsColLesson'))}
          <span>{t('jobsColState')}</span>
          {columnName('created', t('jobsColDates'))}
          <span>{t('jobId')}</span>
        </div>
      )}
      <ul className="jobs-list__ul">
        {jobs.map((j) => {
          const info = detail >= 2 ? lessonJobs?.get(j.id) : undefined;
          const facts = info ? lessonFacts(info, t) : '';
          const created = formatDate(j.createdAt);
          const completed = formatDate(j.completedAt);
          const head = groupHeads?.get(j.id);
          return (
            <Fragment key={j.id}>
              {head && (
                <li className="jobs-list__group" data-testid="jobs-list-group">
                  <span>{head.date ?? t('jobsNoLesson')}</span>
                  <span className="jobs-list__group-count">{head.count}</span>
                  <LessonPageLinks
                    pages={head.pages}
                    t={t}
                    packageDate={head.hasPackage ? head.date : null}
                    onOpenPackage={onOpenPackage}
                    notes={
                      head.notes && head.date
                        ? { date: head.date, ...head.notes }
                        : null
                    }
                    onOpenNotes={onOpenNotes}
                  />
                </li>
              )}
              <li className="jobs-list__li">
                <button
                  type="button"
                  onClick={() => onSelectJob(j.id)}
                  className={`jobs-list__item ${currentJobId === j.id ? 'active' : ''}`}
                  title={j.id}
                >
                  <span className="jobs-list__name">
                    {j.originalFilename || j.id}
                  </span>
                  {detail >= 2 && (info || detail >= 3) && (
                    <span
                      className="jobs-list__lesson"
                      data-testid="jobs-list-lesson"
                    >
                      {info ? (
                        <>
                          <span className="jobs-list__lesson-place">
                            {lessonPlace(info, j.originalFilename ?? '', t)}
                          </span>
                          {facts && (
                            <span className="jobs-list__lesson-facts">
                              {facts}
                            </span>
                          )}
                        </>
                      ) : (
                        <span className="jobs-list__none">—</span>
                      )}
                    </span>
                  )}
                  <span className="jobs-list__facts">
                    <span className="jobs-list__meta">
                      <span className="jobs-list__status">{j.status}</span>
                      {detail >= 1 && j.phase && (
                        <span className="jobs-list__phase">{j.phase}</span>
                      )}
                      {detail < 2 && (completed || created) && (
                        <span className="jobs-list__date">
                          {completed || created}
                        </span>
                      )}
                      {detail >= 2 &&
                        (j.tags ?? []).map((tag) => (
                          <span key={tag} className="jobs-list__tag">
                            {tag}
                          </span>
                        ))}
                    </span>
                    {detail >= 2 && (
                      <span className="jobs-list__dates">
                        {created && (
                          <span>
                            {t('jobsCreated')} {created}
                          </span>
                        )}
                        {completed && (
                          <span>
                            {t('jobsCompleted')} {completed}
                          </span>
                        )}
                        {!created && !completed && (
                          <span className="jobs-list__none">—</span>
                        )}
                      </span>
                    )}
                    {detail >= 2 && (
                      <span className="jobs-list__id">{j.id}</span>
                    )}
                  </span>
                </button>
                {confirmId === j.id ? (
                  <span className="jobs-list__confirm">
                    {t('confirmDelete')}{' '}
                    <button type="button" onClick={() => onDelete(j.id)}>
                      {t('delete')}
                    </button>
                    <button type="button" onClick={() => setConfirmId(null)}>
                      {t('cancel')}
                    </button>
                  </span>
                ) : (
                  <button
                    type="button"
                    onClick={() => setConfirmId(j.id)}
                    className="jobs-list__del"
                    title={t('deleteJob')}
                  >
                    ×
                  </button>
                )}
              </li>
            </Fragment>
          );
        })}
      </ul>
      {!loading && jobs.length === 0 && (
        <p className="jobs-list__empty">{t('noJobs')}</p>
      )}
      <style>{`
        .jobs-list { padding: 0.5rem; }
        .jobs-list__header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.5rem; }
        .jobs-list__title { font-size: 1rem; margin: 0; color: var(--color-text); }
        .jobs-list__count { font-weight: 400; color: var(--color-text-secondary); }
        .jobs-list__refresh { padding: 0.25rem 0.5rem; font-size: 0.875rem; border-radius: 4px; border: 1px solid var(--color-border-strong); background: var(--color-surface); color: var(--color-text); cursor: pointer; }
        .jobs-list__sort { display: flex; align-items: stretch; gap: 0.25rem; margin-bottom: 0.5rem; }
        .jobs-list__sort-key,
        .jobs-list__sort-dir { padding: 0.2rem 0.4rem; font: inherit; font-size: 0.8rem; border-radius: 4px; border: 1px solid var(--color-border-strong); background: var(--color-surface); color: var(--color-text); cursor: pointer; }
        .jobs-list__sort-key { flex: 0 1 14rem; min-width: 0; }
        .jobs-list__sort-dir { flex: 0 0 auto; display: inline-flex; align-items: baseline; gap: 0.3rem; white-space: nowrap; }
        /* The narrowest list has room for the arrow only; its meaning stays in the hint. */
        .jobs-list--d0 .jobs-list__sort-word { display: none; }
        .jobs-list__ul { list-style: none; margin: 0; padding: 0; }
        /* The day of a lesson above its jobs, in a list ordered by lesson. */
        .jobs-list__group { display: flex; align-items: center; gap: 0.4rem; margin: 0.75rem 0 0.3rem; padding: 0 0.15rem; font-size: 0.75rem; font-weight: 600; color: var(--color-heading); font-variant-numeric: tabular-nums; }
        .jobs-list__group:first-child { margin-top: 0.25rem; }
        .jobs-list__group::after { content: ''; flex: 1; height: 1px; background: var(--color-border); }
        .jobs-list__group-count { font-weight: 400; color: var(--color-text-secondary); }
        .jobs-list__group-count::before { content: '· '; }
        .jobs-list__group > span { flex-shrink: 0; }
        /* The page of the lesson stands next to its day, before the line; in a narrow list its name is cut. */
        .jobs-list__group > .lesson-pages { flex: 0 1 auto; flex-wrap: nowrap; overflow: hidden; }
        .jobs-list__group .lesson-pages__link { min-width: 0; }
        .jobs-list--d0 .jobs-list__group a.lesson-pages__link > span:last-child { display: none; }
        .jobs-list__li { display: flex; align-items: center; gap: 0.25rem; margin-bottom: 0.25rem; }
        .jobs-list__item {
          flex: 1;
          min-width: 0; /* let a long name shrink to an ellipsis instead of pushing × out of the pane */
          text-align: left;
          padding: 0.5rem;
          border: 1px solid var(--color-border);
          border-radius: 4px;
          background: var(--color-surface);
          color: var(--color-text);
          cursor: pointer;
          font: inherit;
        }
        .jobs-list__item:hover { background: var(--color-surface-hover); }
        .jobs-list__item.active { border-color: var(--color-primary); background: var(--color-accent-soft); }
        .jobs-list__name { font-weight: 500; display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .jobs-list__facts { display: flex; flex-wrap: wrap; align-items: baseline; gap: 0.125rem 0.75rem; margin-top: 0.125rem; }
        .jobs-list__meta { display: flex; flex-wrap: wrap; align-items: baseline; gap: 0.125rem 0.5rem; font-size: 0.75rem; color: var(--color-text-secondary); }
        .jobs-list__phase::before { content: '· '; }
        .jobs-list__tag { padding: 0 0.35rem; border: 1px solid var(--color-border-strong); border-radius: 999px; }
        .jobs-list__dates { display: flex; flex-wrap: wrap; gap: 0 0.75rem; font-size: 0.75rem; color: var(--color-text-secondary); font-variant-numeric: tabular-nums; }
        .jobs-list__id { font-family: ui-monospace, monospace; font-size: 0.7rem; color: var(--color-text-muted); }
        .jobs-list__lesson { display: flex; flex-wrap: wrap; gap: 0 0.75rem; margin-top: 0.125rem; font-size: 0.8rem; }
        .jobs-list__lesson-place { color: var(--color-heading); }
        .jobs-list__lesson-facts { color: var(--color-text-secondary); }
        .jobs-list__none { color: var(--color-text-muted); }
        .jobs-list__del { flex-shrink: 0; width: 28px; height: 28px; padding: 0; border: 1px solid var(--color-border); border-radius: 4px; background: var(--color-surface); color: var(--color-text); cursor: pointer; }
        .jobs-list__confirm { font-size: 0.75rem; display: flex; gap: 0.25rem; align-items: center; }
        .jobs-list__confirm button { padding: 0.125rem 0.25rem; }
        .jobs-list__loading, .jobs-list__empty { color: var(--color-text-muted); font-size: 0.875rem; margin: 0.5rem 0; }

        /* Wider list: the whole name instead of an ellipsis. */
        .jobs-list--d1 .jobs-list__name {
          white-space: normal;
          overflow-wrap: anywhere;
          display: -webkit-box;
          -webkit-box-orient: vertical;
          -webkit-line-clamp: 2;
        }
        .jobs-list--d2 .jobs-list__name,
        .jobs-list--d3 .jobs-list__name { white-space: normal; overflow-wrap: anywhere; }

        /* Widest list: a table, one column for each kind of fact. */
        .jobs-list--d3 {
          --jobs-columns: minmax(0, 2fr) minmax(0, 2fr) 7.5rem 11.5rem 7rem;
        }
        .jobs-list__columns {
          position: sticky;
          top: 0;
          z-index: 1;
          display: grid;
          grid-template-columns: var(--jobs-columns);
          gap: 0 0.75rem;
          /* The same inset as a row: its border and padding at the left, the × button at the right. */
          padding: 0.3rem calc(0.5rem + 1px + 0.25rem + 28px) 0.3rem calc(0.5rem + 1px);
          margin-bottom: 0.25rem;
          background: var(--color-sidebar);
          border-bottom: 1px solid var(--color-border);
          font-size: 0.7rem;
          text-transform: uppercase;
          letter-spacing: 0.03em;
          color: var(--color-label);
        }
        .jobs-list__col-sort { padding: 0; border: 0; background: none; font: inherit; color: inherit; text-transform: inherit; letter-spacing: inherit; text-align: left; cursor: pointer; }
        .jobs-list__col-sort:hover { text-decoration: underline; }
        .jobs-list__col-sort--on { color: var(--color-text); font-weight: 600; }
        .jobs-list--d3 .jobs-list__li { align-items: stretch; }
        .jobs-list--d3 .jobs-list__item {
          display: grid;
          grid-template-columns: var(--jobs-columns);
          gap: 0 0.75rem;
          align-items: start;
        }
        .jobs-list--d3 .jobs-list__facts { display: contents; }
        .jobs-list--d3 .jobs-list__lesson,
        .jobs-list--d3 .jobs-list__dates { flex-direction: column; margin-top: 0; }
        .jobs-list--d3 .jobs-list__meta { flex-direction: column; gap: 0; font-size: 0.8rem; }
        .jobs-list--d3 .jobs-list__phase::before { content: none; }
        .jobs-list--d3 .jobs-list__del { height: auto; min-height: 28px; }
      `}</style>
    </div>
  );
}

/** Drawn anew only when what it shows changes — not for every pixel of a dragged edge. */
export const JobsList = memo(JobsListView);
