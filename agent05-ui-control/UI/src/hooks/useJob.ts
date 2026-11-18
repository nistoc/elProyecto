import { useState, useEffect, useCallback, useRef } from 'react';
import type {
  JobSnapshot,
  JobListItem,
  StreamEvent,
  StepStatus,
  ConnectionState,
  RefineProgressEvent,
  RefineProgressState,
  RefineBatch,
} from '../types';
import { isTerminalSnapshot } from '../utils/streamReconnect';
import type { JobSettings } from '../utils/jobSettings';
import type { RefinePrompt } from '../utils/refinePrompt';
import {
  fetchJob,
  fetchJobsList,
  createJob,
  deleteJob,
  subscribeToJob,
  startRefine,
  fetchRefineBatches,
} from '../api';

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
const LAST_JOB_KEY = 'xtract-manager-last-job-id';
/** How many jobs the list asks for at once: far above what a jobs folder holds. */
const JOBS_LIST_LIMIT = 1000;

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
  /** SSE health: 'connected' | 'reconnecting' | 'polling' (degraded fallback). */
  connectionState: ConnectionState;
  getStepStatus: (step: StepId) => StepStatus;
  setJobId: (id: string | null) => void;
  setActiveStep: (step: StepId) => void;
  setFile: (f: File | null) => void;
  /** Increments when job snapshot updates over SSE; pass as filesRefreshKey / chunk-groups refresh in App. */
  jobSnapshotRevision: number;
  /** GET /api/jobs/:id (disk-enriched VM); use after chunk-actions so sub-chunk rows refresh without full reload. */
  refreshJobSnapshot: () => Promise<void>;
  refreshList: () => Promise<void>;
  /** `whenOnUpload`: the step to show when the upload form would otherwise stay open. */
  handleSelectJob: (id: string, whenOnUpload?: StepId) => void;
  handleStart: (tags?: string[], settings?: JobSettings) => Promise<void>;
  handleReset: () => void;
  handleDeleteJob: (id: string) => Promise<void>;
  /** Manually run the refiner (skip=true marks the job completed without refining; inputFile picks a specific combined transcript; prompt picks the lesson prompt or the saved settings). */
  startRefiner: (
    skip?: boolean,
    inputFile?: string | null,
    prompt?: RefinePrompt
  ) => Promise<void>;
  /** Live before/after refiner progress from SSE, or null when idle. */
  refineProgress: RefineProgressState | null;
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
  const [connectionState, setConnectionState] =
    useState<ConnectionState>('connected');
  /** Bumps on each SSE snapshot so project files + chunk-artifact-groups refetch (disk artifacts). */
  const [jobSnapshotRevision, setJobSnapshotRevision] = useState(0);
  // Bumped to reopen the SSE stream after it closed on a terminal snapshot (e.g. re-running the refiner on a completed job).
  const [streamEpoch, setStreamEpoch] = useState(0);
  const [refineProgress, setRefineProgress] = useState<RefineProgressState | null>(
    null
  );
  const unsubscribeRef = useRef<(() => void) | null>(null);
  const prevJobIdsRef = useRef<Set<string>>(new Set());
  const listHydratedRef = useRef(false);

  const setJobId = useCallback(
    (id: string | null) => {
      persistLastJobId(id);
      setError(null);
      // The job that is already open (its row in the sidebar, its run in the lessons list) stays as it is:
      // the effect that loads a job runs only when the id changes, so a dropped snapshot would not come back.
      if (id === jobId) return;
      setJobIdState(id);
      setJob(null);
      setJobSnapshotRevision(0);
    },
    [jobId]
  );

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
      // All jobs, not the newest 50 the API gives when no limit is named: the list can be ordered
      // by lesson, and a lesson whose jobs are old would be missing from it.
      const list = await fetchJobsList({ limit: JOBS_LIST_LIMIT });
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
    setRefineProgress(null); // clear stale before/after progress when switching jobs
    let cancelled = false;
    // Show the last saved refiner run (survives reloads); a live stream replaces it.
    fetchRefineBatches(jobId)
      .then((saved) => {
        if (cancelled || saved.batches.length === 0) return;
        setRefineProgress((prev) =>
          prev && !prev.fromDisk
            ? prev
            : {
                promptPreview: saved.promptPreview,
                currentBatch: saved.batches.length,
                totalBatches: saved.batches[0]?.total ?? saved.batches.length,
                progressPercent: 100,
                batches: saved.batches,
                fromDisk: true,
              }
        );
      })
      .catch(() => {
        /* no saved run — nothing to show */
      });
    let doneHandled = false;
    const ac = new AbortController();
    setConnectionState('connected');
    const handleDone = () => {
      if (doneHandled) return;
      doneHandled = true;
      unsubscribeRef.current?.();
      fetchJob(jobId).then((s) => {
        if (cancelled || !s) return;
        setJob(s);
        // Only jump to Result once the whole job is done (refiner completed/skipped),
        // not when transcription finishes and the job is waiting for a manual refine.
        if (s.phase === 'completed') setActiveStepState('result');
      });
      // Sidebar list still shows the pre-terminal status otherwise (it does not consume SSE).
      void refreshList();
    };
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
        if (ev.type === 'connection' && ev.payload && typeof ev.payload === 'object') {
          const p = ev.payload as { state?: string };
          if (
            p.state === 'connected' ||
            p.state === 'reconnecting' ||
            p.state === 'polling'
          )
            setConnectionState(p.state);
          return;
        }
        if (ev.type === 'snapshot' && ev.payload) {
          const snap = ev.payload as JobSnapshot;
          setJob(snap);
          setJobSnapshotRevision((r) => r + 1);
          // Terminality is defined by the data, not only by the 'done' event (which can be missed).
          if (isTerminalSnapshot(snap)) handleDone();
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
        if (ev.type === 'refine_progress' && ev.payload) {
          const p = ev.payload as RefineProgressEvent;
          setRefineProgress((prev) => {
            // Merge incremental batch deltas by index (idempotent across reconnects).
            const byIndex = new Map<number, RefineBatch>();
            const base = prev?.fromDisk ? null : prev; // a live run supersedes the saved one
            (base?.batches ?? []).forEach((b) => byIndex.set(b.index, b));
            (p.batches ?? []).forEach((b) => byIndex.set(b.index, b));
            const batches = Array.from(byIndex.values()).sort(
              (a, b) => a.index - b.index
            );
            return {
              promptPreview: p.promptPreview ?? base?.promptPreview ?? null,
              currentBatch: p.currentBatch ?? base?.currentBatch ?? 0,
              totalBatches: p.totalBatches ?? base?.totalBatches ?? 0,
              progressPercent: p.progressPercent ?? base?.progressPercent ?? 0,
              batches,
            };
          });
          return;
        }
        if (ev.type === 'done') {
          handleDone();
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
  }, [jobId, refreshList, streamEpoch]);

  const handleSelectJob = useCallback(
    (id: string, whenOnUpload?: StepId) => {
      setJobId(id);
      setActiveStepState((prev) => {
        const s = localStorage.getItem(`${ACTIVE_STEP_KEY}-${id}`);
        const step = (s as StepId) || prev;
        return step === 'upload' && whenOnUpload ? whenOnUpload : step;
      });
    },
    [setJobId]
  );

  const handleStart = useCallback(
    async (tags?: string[], settings?: JobSettings) => {
      if (!file) return;
      setError(null);
      setIsSubmitting(true);
      try {
        const { jobId: newId } = await createJob(file, tags, settings);
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

  /** Manually start the refiner (or skip it) for the current job; updates arrive over SSE. */
  const startRefiner = useCallback(
    async (skip = false, inputFile?: string | null, prompt?: RefinePrompt) => {
      if (!jobId) return;
      setError(null);
      if (!skip) setRefineProgress(null); // fresh run → drop the previous before/after table
      try {
        await startRefine(jobId, skip, inputFile, prompt);
        // The stream may already be closed (job was completed): reopen it to get live progress and the final snapshot.
        setStreamEpoch((n) => n + 1);
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Failed to start refiner');
      }
    },
    [jobId]
  );

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
    connectionState,
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
    startRefiner,
    refineProgress,
  };
}
