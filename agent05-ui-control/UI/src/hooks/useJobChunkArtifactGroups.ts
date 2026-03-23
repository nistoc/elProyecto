import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchJobChunkArtifactGroups } from '../api';
import type { ChunkArtifactGroup } from '../utils/chunkArtifactGroups';

export interface JobChunkArtifactGroupsState {
  /**
   * Null until the first response for the current job (same as Chunk controls).
   * After any completed fetch — always an array (empty if the API failed).
   */
  data: ChunkArtifactGroup[] | null;
  loading: boolean;
  refreshing: boolean;
  reload: () => void;
}

/**
 * Single GET .../chunk-artifact-groups per job; share across Chunk controls, project files, Result.
 * Refresh when `refreshKey` changes (e.g. jobSnapshotRevision after SSE).
 */
export function useJobChunkArtifactGroups(
  jobId: string | null,
  refreshKey: number
): JobChunkArtifactGroupsState {
  const [data, setData] = useState<ChunkArtifactGroup[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const prevJobIdRef = useRef<string | null>(null);

  const reload = useCallback(() => setReloadKey((k) => k + 1), []);

  useEffect(() => {
    if (!jobId) {
      setData(null);
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
    } else {
      setRefreshing(true);
    }

    fetchJobChunkArtifactGroups(jobId)
      .then((res) => {
        if (!cancelled) setData(res.groups ?? []);
      })
      .catch(() => {
        if (!cancelled) setData([]);
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
  }, [jobId, refreshKey, reloadKey]);

  return { data, loading, refreshing, reload };
}
