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
        ))}
      </ul>
      {!loading && jobs.length === 0 && (
        <p className="jobs-list__empty">No jobs</p>
      )}
      <style>{`
        .jobs-list { padding: 0.5rem; }
        .jobs-list__header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.5rem; }
        .jobs-list__title { font-size: 1rem; margin: 0; }
        .jobs-list__refresh { padding: 0.25rem 0.5rem; font-size: 0.875rem; }
        .jobs-list__ul { list-style: none; margin: 0; padding: 0; }
        .jobs-list__li { display: flex; align-items: center; gap: 0.25rem; margin-bottom: 0.25rem; }
        .jobs-list__item {
          flex: 1;
          text-align: left;
          padding: 0.5rem;
          border: 1px solid #e2e8f0;
          border-radius: 4px;
          background: #fff;
          cursor: pointer;
        }
        .jobs-list__item:hover { background: #f8fafc; }
        .jobs-list__item.active { border-color: #3b82f6; background: #eff6ff; }
        .jobs-list__name { font-weight: 500; display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .jobs-list__meta { display: flex; align-items: center; gap: 0.5rem; font-size: 0.75rem; color: #64748b; margin-top: 0.125rem; }
        .jobs-list__status { }
        .jobs-list__date { }
        .jobs-list__del { width: 28px; height: 28px; padding: 0; border: 1px solid #e2e8f0; border-radius: 4px; background: #fff; cursor: pointer; }
        .jobs-list__confirm { font-size: 0.75rem; display: flex; gap: 0.25rem; align-items: center; }
        .jobs-list__confirm button { padding: 0.125rem 0.25rem; }
        .jobs-list__loading, .jobs-list__empty { color: #94a3b8; font-size: 0.875rem; margin: 0.5rem 0; }
      `}</style>
    </div>
  );
}
