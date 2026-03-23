import { useState, useEffect, useCallback, useRef } from 'react';
import type {
  ChunkVirtualModelEntry,
  JobSnapshot,
  JobListItem,
  StreamEvent,
  StepStatus,
} from '../types';
import { playTranscriptionChunkDoneChime } from '../utils/playTranscriptionChunkDoneChime';
import {
  fetchJob,
  fetchJobsList,
  createJob,
  deleteJob,
  subscribeToJob,
  postRefinerStart,
  postRefinerPause,
  postRefinerResume,
  postRefinerSkip,
} from '../api';
import { useLogBuffer } from './useLogBuffer';
import { refinerUiDebug } from '../utils/refinerUiDebug';

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
        phase === 'refiner_paused' ||
        phase === 'completed'
      )
        return 'done';
      if (status === 'failed' && phase === 'idle') return 'failed';
      return phase === 'idle' ? 'waiting' : 'done';
    case 'refiner':
      if (phase === 'refiner') return 'running';
      if (
        phase === 'awaiting_refiner' &&
        typeof job.agent06RefineJobId === 'string' &&
        job.agent06RefineJobId.trim().length > 0
      )
        return 'running';
      if (phase === 'refiner_paused') return 'waiting';
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
const LAST_JOB_KEY = 'xtract-manager-last-job-id';

function chunkVmRowKey(e: ChunkVirtualModelEntry): string {
  if (e.isSubChunk === true) {
    return `${e.parentChunkIndex ?? -1}:sub:${e.subChunkIndex ?? -1}`;
  }
  return `main:${e.index}`;
}

function readLastJobId(): string | null {
  try {
    return localStorage.getItem(LAST_JOB_KEY);
  } catch {
    return null;
  }
}

function persistLastJobId(id: string | null) {
  try {
    if (id) localStorage.setItem(LAST_JOB_KEY, id);
    else localStorage.removeItem(LAST_JOB_KEY);
  } catch {
    /* ignore */
  }
}

export function useJob(): {
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
  /** Increments when job snapshot updates over SSE; pass as filesRefreshKey / chunk-groups refresh in App. */
  jobSnapshotRevision: number;
  /** GET /api/jobs/:id (disk-enriched VM); use after chunk-actions so sub-chunk rows refresh without full reload. */
  refreshJobSnapshot: () => Promise<void>;
  refreshList: () => Promise<void>;
  handleSelectJob: (id: string) => void;
  handleStart: (tags?: string[]) => Promise<void>;
  handleReset: () => void;
  handleDeleteJob: (id: string) => Promise<void>;
  handleStartRefiner: (transcriptRelativePath?: string) => Promise<void>;
  handlePauseRefiner: () => Promise<void>;
  handleResumeRefiner: () => Promise<void>;
  handleSkipRefiner: () => Promise<void>;
  refinerActionBusy: boolean;
  logsPaused: boolean;
  bufferedCount: number;
  toggleLogsPause: () => void;
  clearLogsForStep: () => void;
} {
  const [jobId, setJobIdState] = useState<string | null>(() => readLastJobId());
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
  const [refinerActionBusy, setRefinerActionBusy] = useState(false);
  /** Bumps on each SSE snapshot so project files + chunk-artifact-groups refetch (disk artifacts). */
  const [jobSnapshotRevision, setJobSnapshotRevision] = useState(0);
  const logBuffer = useLogBuffer();
  const unsubscribeRef = useRef<(() => void) | null>(null);
  const prevJobIdsRef = useRef<Set<string>>(new Set());
  const listHydratedRef = useRef(false);
  const prevChunkVmStateRef = useRef<Map<string, string>>(new Map());
  const chunkVmSoundHydratedRef = useRef(false);

  const setJobId = useCallback((id: string | null) => {
    persistLastJobId(id);
    setJobIdState(id);
    setJob(null);
    setError(null);
    setJobSnapshotRevision(0);
    prevChunkVmStateRef.current = new Map();
    chunkVmSoundHydratedRef.current = false;
  }, []);

  const setActiveStep = useCallback(
    (step: StepId) => {
      setActiveStepState(step);
      localStorage.setItem(ACTIVE_STEP_KEY, step);
      if (jobId) localStorage.setItem(`${ACTIVE_STEP_KEY}-${jobId}`, step);
    },
    [jobId]
  );

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

  const refreshJobSnapshot = useCallback(async () => {
    if (!jobId) return;
    const snap = await fetchJob(jobId);
    if (snap) {
      setJob(snap);
      setJobSnapshotRevision((r) => r + 1);
    }
  }, [jobId]);

  useEffect(() => {
    refreshList();
  }, [refreshList]);

  useEffect(() => {
    prevChunkVmStateRef.current = new Map();
    chunkVmSoundHydratedRef.current = false;
  }, [jobId]);

  /** Chime when any chunk / sub-chunk VM transitions into `completed` (after first snapshot for this job). */
  useEffect(() => {
    const vm = job?.chunks?.chunkVirtualModel;
    if (!vm?.length) return;

    const next = new Map<string, string>();
    for (const e of vm) {
      const key = chunkVmRowKey(e);
      const state = (e.state ?? '').trim().toLowerCase();
      next.set(key, state);

      if (chunkVmSoundHydratedRef.current) {
        const prev = prevChunkVmStateRef.current.get(key);
        if (prev && prev !== 'completed' && state === 'completed') {
          playTranscriptionChunkDoneChime();
        }
      }
    }

    chunkVmSoundHydratedRef.current = true;
    prevChunkVmStateRef.current = next;
  }, [job]);

  /** First list load: validate restored jobId; later loads: auto-select newest newly appeared job. */
  useEffect(() => {
    if (loadingList) return;

    const ids = new Set(jobsList.map((j) => j.id));

    if (!listHydratedRef.current) {
      listHydratedRef.current = true;
      prevJobIdsRef.current = ids;
      if (jobId && !ids.has(jobId)) {
        setJobId(jobsList[0]?.id ?? null);
      }
      return;
    }

    const prev = prevJobIdsRef.current;
    const newOnes = jobsList.filter((j) => !prev.has(j.id));
    prevJobIdsRef.current = ids;

    if (newOnes.length === 0) return;

    const newestNew = [...newOnes].sort((a, b) =>
      (b.createdAt ?? '').localeCompare(a.createdAt ?? '')
    )[0];
    if (newestNew) setJobId(newestNew.id);
  }, [jobsList, loadingList, jobId, setJobId]);

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
          const snap = ev.payload as JobSnapshot;
          refinerUiDebug('SSE snapshot');
          setJob(snap);
          setJobSnapshotRevision((r) => r + 1);
          return;
        }
        if (
          ev.type === 'status' &&
          ev.payload &&
          typeof ev.payload === 'object'
        ) {
          const p = ev.payload as {
            phase?: string;
            status?: string;
            progressPercent?: number;
            progress_percent?: number;
            transcriptionPhaseDetail?: string | null;
          };
          setJob((prev) => {
            if (!prev) return null;
            const next = {
              ...prev,
              phase: p.phase ?? prev.phase,
              status: p.status ?? prev.status,
            };
            const pct = p.progressPercent ?? p.progress_percent;
            const hasTranscriptionTelemetry =
              p.transcriptionPhaseDetail !== undefined || p.phase === 'transcriber';
            if (hasTranscriptionTelemetry) {
              if (pct !== undefined && pct !== null)
                next.transcriptionProgressPercent = pct;
              if (p.transcriptionPhaseDetail !== undefined)
                next.transcriptionPhaseDetail = p.transcriptionPhaseDetail;
            }
            return next;
          });
          // Same tick as snapshot would duplicate bump (harmless). If the server ever sends only
          // `status` without a full snapshot, this still nudges refiner live panels (snapshotRevision).
          const ph = p.phase;
          if (ph === 'refiner' || ph === 'refiner_paused') {
            refinerUiDebug('SSE status (refiner) → snapshotRevision++', ph);
            setJobSnapshotRevision((r) => r + 1);
          }
          return;
        }
        if (ev.type === 'done') {
          unsubscribeRef.current?.();
          fetchJob(jobId).then((s) => {
            if (cancelled || !s) return;
            setJob(s);
            if (s.status !== 'failed' && s.phase === 'completed')
              setActiveStep('result');
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
  }, [jobId, setActiveStep]);

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
        setJobId(newId);
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
    [file, refreshList, setJobId]
  );

  const handleReset = useCallback(() => {
    setJobId(null);
    setFile(null);
    setActiveStepState('upload');
    localStorage.removeItem(ACTIVE_STEP_KEY);
  }, [setJobId]);

  const handleStartRefiner = useCallback(
    async (transcriptRelativePath?: string) => {
      if (!jobId) return;
      setRefinerActionBusy(true);
      setError(null);
      try {
        await postRefinerStart(jobId, transcriptRelativePath);
        await refreshJobSnapshot();
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Failed to start refiner');
      } finally {
        setRefinerActionBusy(false);
      }
    },
    [jobId, refreshJobSnapshot]
  );

  const handlePauseRefiner = useCallback(async () => {
    if (!jobId) return;
    setRefinerActionBusy(true);
    try {
      await postRefinerPause(jobId);
      await refreshJobSnapshot();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to pause refiner');
    } finally {
      setRefinerActionBusy(false);
    }
  }, [jobId, refreshJobSnapshot]);

  const handleResumeRefiner = useCallback(async () => {
    if (!jobId) return;
    setRefinerActionBusy(true);
    try {
      await postRefinerResume(jobId);
      await refreshJobSnapshot();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to resume refiner');
    } finally {
      setRefinerActionBusy(false);
    }
  }, [jobId, refreshJobSnapshot]);

  const handleSkipRefiner = useCallback(async () => {
    if (!jobId) return;
    setRefinerActionBusy(true);
    try {
      await postRefinerSkip(jobId);
      await refreshJobSnapshot();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to skip refiner');
    } finally {
      setRefinerActionBusy(false);
    }
  }, [jobId, refreshJobSnapshot]);

  const handleDeleteJob = useCallback(
    async (id: string) => {
      const wasSelected = jobId === id;
      const listBefore = jobsList;
      let nextId: string | null = null;
      if (wasSelected) {
        const idx = listBefore.findIndex((j) => j.id === id);
        if (idx >= 0) {
          if (idx + 1 < listBefore.length) nextId = listBefore[idx + 1].id;
          else if (idx - 1 >= 0) nextId = listBefore[idx - 1].id;
        }
      }
      try {
        await deleteJob(id);
        if (wasSelected) {
          if (nextId) {
            setJobId(nextId);
            setActiveStepState((prev) => {
              const s = localStorage.getItem(`${ACTIVE_STEP_KEY}-${nextId}`);
              return (s as StepId) || prev;
            });
          } else {
            handleReset();
          }
        }
        await refreshList();
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Failed to delete job');
      }
    },
    [jobId, jobsList, refreshList, setJobId, handleReset]
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
    refreshJobSnapshot,
    refreshList,
    handleSelectJob,
    handleStart,
    handleReset,
    handleDeleteJob,
    handleStartRefiner,
    handlePauseRefiner,
    handleResumeRefiner,
    handleSkipRefiner,
    refinerActionBusy,
    logsPaused: logBuffer.logsPaused,
    bufferedCount: logBuffer.bufferedCount,
    toggleLogsPause,
    clearLogsForStep,
  };
}
