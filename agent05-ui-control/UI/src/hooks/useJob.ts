import { useState, useEffect, useCallback, useRef } from 'react';
import type {
  JobSnapshot,
  JobListItem,
  StreamEvent,
  StepStatus,
} from '../types';
import {
  fetchJob,
  fetchJobsList,
  createJob,
  deleteJob,
  subscribeToJob,
} from '../api';
import { useLogBuffer } from './useLogBuffer';

export type StepId = 'upload' | 'transcriber' | 'refiner' | 'result';

function getStepStatus(step: StepId, job: JobSnapshot | null): StepStatus {
  if (!job) return 'waiting';
  const phase = job.phase;
  const status = job.status;
  switch (step) {
    case 'upload':
      return job.id ? 'done' : 'waiting';
    case 'transcriber':
      if (phase === 'transcriber') return 'running';
      if (
        phase === 'awaiting_refiner' ||
        phase === 'refiner' ||
        phase === 'completed'
      )
        return 'done';
      if (status === 'failed' && phase === 'idle') return 'failed';
      return phase === 'idle' ? 'waiting' : 'done';
    case 'refiner':
      if (phase === 'refiner') return 'running';
      if (phase === 'completed') return 'done';
      if (phase === 'awaiting_refiner') return 'waiting';
      return phase === 'transcriber' || phase === 'idle' ? 'waiting' : 'done';
    case 'result':
      return phase === 'completed' && status === 'done' ? 'done' : 'waiting';
    default:
      return 'waiting';
  }
}

const ACTIVE_STEP_KEY = 'xtract-manager-active-step';

export function useJob(initialJobId: string | null): {
  jobId: string | null;
  job: JobSnapshot | null;
  jobsList: JobListItem[];
  loadingList: boolean;
  activeStep: StepId;
  file: File | null;
  isSubmitting: boolean;
  error: string | null;
  getStepStatus: (step: StepId) => StepStatus;
  setJobId: (id: string | null) => void;
  setActiveStep: (step: StepId) => void;
  setFile: (f: File | null) => void;
  /** Increments when job snapshot updates over SSE; pass to ProjectFilesPanel as filesRefreshKey. */
  jobSnapshotRevision: number;
  refreshList: () => Promise<void>;
  handleSelectJob: (id: string) => void;
  handleStart: (tags?: string[]) => Promise<void>;
  handleReset: () => void;
  handleDeleteJob: (id: string) => Promise<void>;
  logsPaused: boolean;
  bufferedCount: number;
  toggleLogsPause: () => void;
  clearLogsForStep: () => void;
} {
  const [jobId, setJobIdState] = useState<string | null>(initialJobId);
  const [job, setJob] = useState<JobSnapshot | null>(null);
  const [jobsList, setJobsList] = useState<JobListItem[]>([]);
  const [loadingList, setLoadingList] = useState(true);
  const [activeStep, setActiveStepState] = useState<StepId>(() => {
    const s = localStorage.getItem(ACTIVE_STEP_KEY);
    return (s as StepId) || 'upload';
  });
  const [file, setFile] = useState<File | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  /** Bumps on each SSE snapshot so ProjectFilesPanel refetches /api/jobs/:id/files (disk artifacts). */
  const [jobSnapshotRevision, setJobSnapshotRevision] = useState(0);
  const logBuffer = useLogBuffer();
  const unsubscribeRef = useRef<(() => void) | null>(null);

  const setJobId = useCallback((id: string | null) => {
    setJobIdState(id);
    setJob(null);
    setError(null);
    setJobSnapshotRevision(0);
  }, []);

  const setActiveStep = useCallback((step: StepId) => {
    setActiveStepState(step);
    localStorage.setItem(ACTIVE_STEP_KEY, step);
    if (jobId) localStorage.setItem(`${ACTIVE_STEP_KEY}-${jobId}`, step);
  }, [jobId]);

  const refreshList = useCallback(async () => {
    setLoadingList(true);
    try {
      const list = await fetchJobsList();
      const sorted = [...list].sort((a, b) =>
        (b.createdAt ?? '').localeCompare(a.createdAt ?? '')
      );
      setJobsList(sorted);
    } finally {
      setLoadingList(false);
    }
  }, []);

  useEffect(() => {
    refreshList();
  }, [refreshList]);
  useEffect(() => {
    if (!jobId) {
      setJob(null);
      return;
    }
    let cancelled = false;
    const ac = new AbortController();
    (async () => {
      const snap = await fetchJob(jobId);
      if (!cancelled && snap) {
        setJob(snap);
        setJobSnapshotRevision((r) => r + 1);
      }
    })();
    const close = subscribeToJob(
      jobId,
      (ev: StreamEvent) => {
        if (cancelled) return;
        if (ev.type === 'snapshot' && ev.payload) {
          setJob(ev.payload as JobSnapshot);
          setJobSnapshotRevision((r) => r + 1);
          return;
        }
        if (
          ev.type === 'status' &&
          ev.payload &&
          typeof ev.payload === 'object'
        ) {
          const p = ev.payload as { phase?: string; status?: string };
          setJob((prev) =>
            prev
              ? {
                  ...prev,
                  phase: p.phase ?? prev.phase,
                  status: p.status ?? prev.status,
                }
              : null
          );
          return;
        }
        if (ev.type === 'done') {
          unsubscribeRef.current?.();
          fetchJob(jobId).then((s) => {
            if (cancelled || !s) return;
            setJob(s);
            if (s.status !== 'failed') setActiveStepState('result');
          });
        }
      },
      { signal: ac.signal }
    );
    unsubscribeRef.current = close;
    return () => {
      cancelled = true;
      ac.abort();
      close();
    };
  }, [jobId]);

  const handleSelectJob = useCallback(
    (id: string) => {
      setJobId(id);
      setActiveStepState((prev) => {
        const s = localStorage.getItem(`${ACTIVE_STEP_KEY}-${id}`);
        return (s as StepId) || prev;
      });
    },
    [setJobId]
  );

  const handleStart = useCallback(
    async (tags?: string[]) => {
      if (!file) return;
      setError(null);
      setIsSubmitting(true);
      try {
        const { jobId: newId } = await createJob(file, tags);
        setJobIdState(newId);
        setActiveStepState('transcriber');
        localStorage.setItem(ACTIVE_STEP_KEY, 'transcriber');
        localStorage.setItem(`${ACTIVE_STEP_KEY}-${newId}`, 'transcriber');
        setFile(null);
        await refreshList();
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Failed to create job');
      } finally {
        setIsSubmitting(false);
      }
    },
    [file, refreshList]
  );

  const handleReset = useCallback(() => {
    setJobId(null);
    setJob(null);
    setFile(null);
    setError(null);
    setJobSnapshotRevision(0);
    setActiveStepState('upload');
    localStorage.removeItem(ACTIVE_STEP_KEY);
  }, [setJobId]);

  const handleDeleteJob = useCallback(
    async (id: string) => {
      try {
        await deleteJob(id);
        if (jobId === id) handleReset();
        await refreshList();
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Failed to delete job');
      }
    },
    [jobId, handleReset, refreshList]
  );

  const toggleLogsPause = logBuffer.togglePause;
  const clearLogsForStep = useCallback(() => {
    logBuffer.reset();
    setJob((prev) => (prev ? { ...prev, logs: [] } : null));
  }, [logBuffer]);

  const getStepStatusFor = useCallback(
    (step: StepId) => getStepStatus(step, job),
    [job]
  );

  return {
    jobId,
    job,
    jobsList,
    loadingList,
    activeStep,
    file,
    isSubmitting,
    error,
    getStepStatus: getStepStatusFor,
    setJobId,
    setActiveStep,
    setFile,
    jobSnapshotRevision,
    refreshList,
    handleSelectJob,
    handleStart,
    handleReset,
    handleDeleteJob,
    logsPaused: logBuffer.logsPaused,
    bufferedCount: logBuffer.bufferedCount,
    toggleLogsPause,
    clearLogsForStep,
  };
}
