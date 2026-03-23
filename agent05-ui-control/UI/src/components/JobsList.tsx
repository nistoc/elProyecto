import { useState } from 'react';
import type { JobListItem } from '../types';

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '';
  try {
    const d = new Date(iso);
    return d.toLocaleDateString(undefined, { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
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
}

export function JobsList({
  jobs,
  currentJobId,
  onSelectJob,
  onRefresh,
  onDelete,
  loading,
  t,
}: JobsListProps) {
  const [confirmId, setConfirmId] = useState<string | null>(null);

  return (
    <div className="jobs-list">
      <div className="jobs-list__header">
        <h2 className="jobs-list__title">{t('jobs')}</h2>
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
      <ul className="jobs-list__ul">
        {jobs.map((j) => (
          <li key={j.id} className="jobs-list__li">
            <div className="jobs-list__row">
              <button
                type="button"
                onClick={() => onSelectJob(j.id)}
                className={`jobs-list__item ${currentJobId === j.id ? 'active' : ''}`}
                title={j.id}
              >
                <span className="jobs-list__name">
                  {j.originalFilename || j.id}
                </span>
                <span className="jobs-list__meta">
                  <span className="jobs-list__status">{j.status}</span>
                  {j.completedAt ? (
                    <span className="jobs-list__date">{formatDate(j.completedAt)}</span>
                  ) : j.createdAt ? (
                    <span className="jobs-list__date">{formatDate(j.createdAt)}</span>
                  ) : null}
                </span>
              </button>
              <button
                type="button"
                onClick={() =>
                  setConfirmId((prev) => (prev === j.id ? null : j.id))
                }
                className="jobs-list__del"
                title={
                  confirmId === j.id ? t('cancel') : t('deleteJob')
                }
                aria-expanded={confirmId === j.id}
              >
                ×
              </button>
            </div>
            {confirmId === j.id && (
              <div className="jobs-list__confirm">
                <span className="jobs-list__confirm-text">{t('confirmDelete')}</span>
                <div className="jobs-list__confirm-actions">
                  <button
                    type="button"
                    className="jobs-list__confirm-btn jobs-list__confirm-btn--danger"
                    onClick={() => {
                      onDelete(j.id);
                      setConfirmId(null);
                    }}
                  >
                    {t('delete')}
                  </button>
                  <button
                    type="button"
                    className="jobs-list__confirm-btn"
                    onClick={() => setConfirmId(null)}
                  >
                    {t('cancel')}
                  </button>
                </div>
              </div>
            )}
          </li>
        ))}
      </ul>
      {!loading && jobs.length === 0 && (
        <p className="jobs-list__empty">{t('noJobs')}</p>
      )}
      <style>{`
        .jobs-list { padding: 0.5rem; }
        .jobs-list__header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.5rem; }
        .jobs-list__title { font-size: 1rem; margin: 0; color: var(--color-text); }
        .jobs-list__refresh { padding: 0.25rem 0.5rem; font-size: 0.875rem; border-radius: 4px; border: 1px solid var(--color-border-strong); background: var(--color-surface); color: var(--color-text); cursor: pointer; }
        .jobs-list__ul { list-style: none; margin: 0; padding: 0; }
        .jobs-list__li {
          display: flex;
          flex-direction: column;
          align-items: stretch;
          gap: 0.35rem;
          margin-bottom: 0.35rem;
        }
        .jobs-list__row {
          display: flex;
          align-items: stretch;
          gap: 0.25rem;
          min-width: 0;
        }
        .jobs-list__item {
          flex: 1;
          text-align: left;
          padding: 0.5rem;
          border: 1px solid var(--color-border);
          border-radius: 4px;
          background: var(--color-surface);
          color: var(--color-text);
          cursor: pointer;
        }
        .jobs-list__item:hover { background: var(--color-surface-hover); }
        .jobs-list__item.active { border-color: var(--color-primary); background: var(--color-accent-soft); }
        .jobs-list__name { font-weight: 500; display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .jobs-list__meta { display: flex; align-items: center; gap: 0.5rem; font-size: 0.75rem; color: var(--color-text-secondary); margin-top: 0.125rem; }
        .jobs-list__status { }
        .jobs-list__date { }
        .jobs-list__del {
          flex-shrink: 0;
          width: 28px;
          min-height: 28px;
          padding: 0;
          align-self: stretch;
          border: 1px solid var(--color-border);
          border-radius: 4px;
          background: var(--color-surface);
          color: var(--color-text);
          cursor: pointer;
          font-size: 1.1rem;
          line-height: 1;
        }
        .jobs-list__confirm {
          display: flex;
          flex-direction: column;
          gap: 0.4rem;
          padding: 0.4rem 0.35rem 0.5rem;
          border: 1px solid var(--color-border);
          border-radius: 4px;
          background: var(--color-surface-sunken);
          font-size: 0.75rem;
          color: var(--color-text);
        }
        .jobs-list__confirm-text { line-height: 1.35; }
        .jobs-list__confirm-actions {
          display: flex;
          flex-wrap: wrap;
          gap: 0.35rem;
          align-items: center;
        }
        .jobs-list__confirm-btn {
          padding: 0.3rem 0.55rem;
          border-radius: 4px;
          border: 1px solid var(--color-border-strong);
          background: var(--color-surface);
          color: var(--color-text);
          cursor: pointer;
          font-size: 0.75rem;
        }
        .jobs-list__confirm-btn:hover { background: var(--color-surface-hover); }
        .jobs-list__confirm-btn--danger {
          border-color: color-mix(in srgb, var(--color-danger, #c62828) 45%, var(--color-border-strong));
          color: var(--color-danger, #b71c1c);
        }
        .jobs-list__loading, .jobs-list__empty { color: var(--color-text-muted); font-size: 0.875rem; margin: 0.5rem 0; }
      `}</style>
    </div>
  );
}
