import { useEffect, useState } from 'react';
import { fetchJobLanguageShare } from '../api';
import type { JobLanguageShareReport } from '../utils/languageShare';

/**
 * GET /api/jobs/:id/language-share for the open job; asked again when `refreshKey` changes
 * (the debounced snapshot revision: new chunk answers arrive while a job runs).
 * Null until the first answer for the job, and when the job has nothing to count.
 */
export function useJobLanguageShare(
  jobId: string | null,
  refreshKey: number
): JobLanguageShareReport | null {
  const [state, setState] = useState<{
    jobId: string;
    report: JobLanguageShareReport | null;
  } | null>(null);

  useEffect(() => {
    if (!jobId) {
      setState(null);
      return;
    }
    let cancelled = false;
    fetchJobLanguageShare(jobId)
      .then((report) => {
        if (!cancelled) setState({ jobId, report });
      })
      .catch(() => {
        if (!cancelled) setState({ jobId, report: null });
      });
    return () => {
      cancelled = true;
    };
  }, [jobId, refreshKey]);

  // The report of the job that was open before is not shown under the next one.
  return state && state.jobId === jobId ? state.report : null;
}
