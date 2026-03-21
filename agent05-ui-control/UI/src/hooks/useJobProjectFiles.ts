import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchJobFiles } from '../api';
import type { JobProjectFiles } from '../types';

export interface JobProjectFilesState {
  data: JobProjectFiles | null;
  jobDir: string | null;
  loading: boolean;
  error: string | null;
  refreshing: boolean;
  /** Manual refresh (toolbar) — bumps internal key with same jobId. */
  reload: () => void;
}

/**
 * Fetches GET /api/jobs/:id/files when jobId / filesRefreshKey changes.
 * Use one instance in App for Transcriber and pass the same state to Stats + ProjectFilesPanel.
 */
export function useJobProjectFiles(
  jobId: string | null,
  filesRefreshKey: number
): JobProjectFilesState {
  const [data, setData] = useState<JobProjectFiles | null>(null);
  const [jobDir, setJobDir] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const prevJobIdRef = useRef<string | null>(null);

  const reload = useCallback(() => setReloadKey((k) => k + 1), []);

  useEffect(() => {
    if (!jobId) {
      setData(null);
      setJobDir(null);
      setError(null);
      setLoading(false);
      setRefreshing(false);
      prevJobIdRef.current = null;
      return;
    }

    let cancelled = false;
    const jobChanged = prevJobIdRef.current !== jobId;
    prevJobIdRef.current = jobId;

    if (jobChanged) {
      setLoading(true);
      setData(null);
      setJobDir(null);
    } else {
      setRefreshing(true);
    }
    setError(null);

    fetchJobFiles(jobId)
      .then((res) => {
        if (cancelled) return;
        if (!res) {
          setData(null);
          setJobDir(null);
          return;
        }
        setData(res.files);
        setJobDir(typeof res.jobDir === 'string' ? res.jobDir : null);
      })
      .catch((e) => {
        if (!cancelled) {
          setError(e instanceof Error ? e.message : 'Failed to load files');
          setData(null);
          setJobDir(null);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
          setRefreshing(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [jobId, filesRefreshKey, reloadKey]);

  return { data, jobDir, loading, error, refreshing, reload };
}
