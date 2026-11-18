import { useCallback, useState } from 'react';
import {
  JOBS_SORT_DEFAULT,
  JOBS_SORT_KEY,
  formatJobsSort,
  parseStoredJobsSort,
  type JobsSort,
} from '../utils/jobsSort';

function readStoredSort(): JobsSort {
  try {
    return (
      parseStoredJobsSort(localStorage.getItem(JOBS_SORT_KEY)) ??
      JOBS_SORT_DEFAULT
    );
  } catch {
    // A browser that refuses storage: the list keeps the usual order.
    return JOBS_SORT_DEFAULT;
  }
}

/** The order of the jobs list, kept in the browser between visits. */
export function useJobsSort(): [JobsSort, (sort: JobsSort) => void] {
  const [sort, setSort] = useState(readStoredSort);
  const change = useCallback((next: JobsSort) => {
    setSort(next);
    try {
      localStorage.setItem(JOBS_SORT_KEY, formatJobsSort(next));
    } catch {
      // The order still holds until the page is closed.
    }
  }, []);
  return [sort, change];
}
