import { useMemo } from 'react';
import type { JobListItem } from '../types';
import { useJobsSort } from '../hooks/useJobsSort';
import { useSidebarWidth } from '../hooks/useSidebarWidth';
import { sortJobs } from '../utils/jobsSort';
import type { LessonJobInfo } from '../utils/lessons';
import { sidebarDetail } from '../utils/sidebarWidth';
import { JobsList } from './JobsList';

interface JobsSidebarProps {
  jobs: JobListItem[];
  currentJobId: string | null;
  onSelectJob: (id: string) => void;
  onRefresh: () => void;
  onDelete: (id: string) => void;
  loading: boolean;
  t: (key: string) => string;
  /** What the lessons list knows of the jobs, by job id; null until it is read. */
  lessonJobs: Map<string, LessonJobInfo> | null;
  /** Opens the package of a lesson (by its date) inside the app. */
  onOpenPackage?: (date: string) => void;
}

/**
 * The jobs list with an edge the operator drags: the wider the list, the more it tells about each job.
 * The width lives here, not in the app, so that a drag redraws only the list — the job view next to it
 * follows by layout alone.
 */
export function JobsSidebar(props: JobsSidebarProps) {
  const { jobs, t, lessonJobs } = props;
  const size = useSidebarWidth();
  const detail = sidebarDetail(size.width);
  const [sort, setSort] = useJobsSort();
  const sorted = useMemo(
    () => sortJobs(jobs, sort, lessonJobs),
    [jobs, sort, lessonJobs]
  );

  return (
    <>
      <aside
        className="sidebar"
        style={{ width: size.width }}
        data-testid="jobs-sidebar"
      >
        <JobsList
          {...props}
          jobs={sorted}
          detail={detail}
          lessonJobs={lessonJobs}
          sort={sort}
          onSort={setSort}
        />
      </aside>
      <div
        className={
          size.dragging
            ? 'sidebar-resizer sidebar-resizer--drag'
            : 'sidebar-resizer'
        }
        role="separator"
        aria-orientation="vertical"
        aria-label={t('sidebarResize')}
        aria-valuenow={size.width}
        aria-valuemin={size.minWidth}
        aria-valuemax={size.maxWidth}
        tabIndex={0}
        title={t('sidebarResizeHint')}
        onPointerDown={size.onPointerDown}
        onKeyDown={size.onKeyDown}
        onDoubleClick={size.reset}
        data-testid="sidebar-resizer"
      />
      <style>{`
        .sidebar {
          flex-shrink: 0;
          min-height: 0;
          background: var(--color-sidebar);
          overflow-x: hidden;
          overflow-y: auto;
        }
        .sidebar-resizer {
          flex: 0 0 7px;
          position: relative;
          cursor: col-resize;
          touch-action: none;
          background: var(--color-sidebar);
          border-right: 1px solid var(--color-border);
          outline: none;
        }
        /* Easier to catch than its 7 px: the pointer also takes it a few pixels into the job view. */
        .sidebar-resizer::before {
          content: '';
          position: absolute;
          top: 0;
          bottom: 0;
          left: 0;
          right: -5px;
          z-index: 2;
        }
        /* The grip: a thin line that lights up under the pointer, while dragged and on keyboard focus. */
        .sidebar-resizer::after {
          content: '';
          position: absolute;
          top: 0;
          bottom: 0;
          left: 2px;
          width: 3px;
          border-radius: 2px;
          background: transparent;
          transition: background 0.12s ease;
        }
        .sidebar-resizer:hover::after,
        .sidebar-resizer:focus-visible::after,
        .sidebar-resizer--drag::after { background: var(--color-primary); }
        /* While the edge is dragged nothing else reacts to the pointer and no text gets selected. */
        body:has(.sidebar-resizer--drag) { cursor: col-resize; user-select: none; }
      `}</style>
    </>
  );
}
