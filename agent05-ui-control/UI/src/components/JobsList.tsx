import { useState } from 'react';
import type { JobListItem } from '../types';
import { languageShareShort } from '../utils/languageShare';
import type { LessonJobInfo } from '../utils/lessons';
import { runSettingsShort } from '../utils/runSettings';
import type { SidebarDetail } from '../utils/sidebarWidth';

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '';
  try {
    const d = new Date(iso);
    return d.toLocaleDateString(undefined, {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
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

export function JobsList({
  jobs,
  currentJobId,
  onSelectJob,
  onRefresh,
  onDelete,
  loading,
  t,
  detail = 0,
  lessonJobs = null,
}: JobsListProps) {
  const [confirmId, setConfirmId] = useState<string | null>(null);

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
      {loading && <p className="jobs-list__loading">…</p>}
      {detail >= 3 && jobs.length > 0 && (
        <div className="jobs-list__columns" aria-hidden="true">
          <span>{t('jobsColJob')}</span>
          <span>{t('jobsColLesson')}</span>
          <span>{t('jobsColState')}</span>
          <span>{t('jobsColDates')}</span>
          <span>{t('jobId')}</span>
        </div>
      )}
      <ul className="jobs-list__ul">
        {jobs.map((j) => {
          const info = detail >= 2 ? lessonJobs?.get(j.id) : undefined;
          const facts = info ? lessonFacts(info, t) : '';
          const created = formatDate(j.createdAt);
          const completed = formatDate(j.completedAt);
          return (
            <li key={j.id} className="jobs-list__li">
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
                  {detail >= 2 && <span className="jobs-list__id">{j.id}</span>}
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
        .jobs-list__ul { list-style: none; margin: 0; padding: 0; }
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
