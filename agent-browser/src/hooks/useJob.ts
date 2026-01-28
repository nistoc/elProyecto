import { useCallback, useEffect, useMemo, useState } from "react";
import {
  cancelChunk,
  cancelSubChunk,
  createJob,
  fetchJob,
  retranscribeSubChunk,
  skipChunk,
  skipRefiner,
  skipRefinerBatch,
  splitChunk,
  startRefiner,
  subscribeToJob,
  pauseAgent,
  resumeAgent,
  rebuildTranscript,
} from "../api";
import type { JobSnapshot, LogEntry, StreamEvent } from "../types";
import { useChunkState } from "./useChunkState";
import { useLogBuffer } from "./useLogBuffer";

export type StepId = "upload" | "transcriber" | "refiner" | "result";

const AGENT_ALIASES = {
  transcriber: "Transcriber Agent",
  refiner: "Refiner Agent",
} as const;

/**
 * Main hook for job lifecycle management.
 * Handles job creation, SSE subscription, state updates.
 */
export function useJob() {
  const [file, setFile] = useState<File | null>(null);
  const [jobId, setJobId] = useState<string | null>(null);
  const [job, setJob] = useState<JobSnapshot | null>(null);
  const [activeStep, setActiveStep] = useState<StepId>("upload");
  const [isSubmitting, setSubmitting] = useState(false);
  const [rebuildingTranscript, setRebuildingTranscript] = useState(false);

  const { applyChunkEvent, applySplitEvent } = useChunkState(setJob);
  const {
    logsPaused,
    bufferedCount,
    handleLog,
    togglePause,
    reset: resetLogBuffer,
  } = useLogBuffer(setJob);

  // Subscribe to job SSE stream
  useEffect(() => {
    if (!jobId) {
      setJob(null);
      return;
    }

    let close = () => {};

    fetchJob(jobId)
      .then((snapshot) => {
        if (!snapshot) {
          // Job not found, clear selection
          console.warn(`Job ${jobId} not found`);
          setJob(null);
          setJobId(null);
          return;
        }
        setJob(snapshot);
        // Only subscribe if job exists
        close = subscribeToJob(jobId, (event: StreamEvent) => {
          switch (event.type) {
            case "snapshot":
              setJob(event.payload);
              break;
            case "log":
              handleLog(event.payload);
              break;
            case "chunk":
              applyChunkEvent(event.payload);
              break;
            case "split":
              applySplitEvent(event.payload);
              break;
            case "status":
              setJob((prev) => (prev ? { ...prev, ...event.payload } : prev));
              break;
            case "done":
              setActiveStep("result");
              break;
          }
        });
      })
      .catch((err) => {
        console.error(`Failed to fetch job ${jobId}:`, err);
        setJob(null);
        setJobId(null);
      });

    return () => close();
  }, [jobId, handleLog, applyChunkEvent, applySplitEvent]);

  // Derived state: step status
  const getStepStatus = useCallback(
    (step: StepId) => {
      if (!job) return step === "upload" ? "waiting" : "waiting";
      if (job.status === "failed") return "failed";

      switch (step) {
        case "upload":
          return jobId ? "done" : "waiting";
        case "transcriber":
          if (job.phase === "transcriber") return "running";
          if (job.phase === "awaiting_refiner" || job.phase === "refiner" || job.phase === "completed")
            return "done";
          return "waiting";
        case "refiner":
          if (job.phase === "refiner") return "running";
          if (job.phase === "completed") return "done";
          if (job.phase === "awaiting_refiner") return "waiting"; // Ready to start
          if (job.phase === "transcriber") return "waiting";
          return "waiting";
        case "result":
          return job.status === "done" ? "done" : "waiting";
        default:
          return "waiting";
      }
    },
    [job, jobId]
  );

  // Derived state: logs filtered by step
  const logsByStep = useMemo(() => {
    const all = job?.logs || [];
    const filter = (needle: string) =>
      all.filter((log) => log.message.includes(needle));

    return {
      upload: [] as LogEntry[],
      transcriber: filter(AGENT_ALIASES.transcriber),
      refiner: filter(AGENT_ALIASES.refiner),
      result: all,
    } as Record<StepId, LogEntry[]>;
  }, [job]);

  // Derived state: result download links
  // Only recalculate when result fields change, not on every job update
  const resultLinks = useMemo(() => {
    const base = import.meta.env.VITE_API_BASE || "http://localhost:3001";
    if (!job?.result) return [];

    const entries: { label: string; href: string }[] = [];
    if (job.result.transcript) {
      entries.push({
        label: "transcript.md",
        href: `${base}/${job.result.transcript}`,
      });
    }
    // Add all transcript_fixed files
    if (job.result.transcriptFixedAll && Array.isArray(job.result.transcriptFixedAll)) {
      job.result.transcriptFixedAll.forEach((fixedPath: string) => {
        const fileName = fixedPath.split('/').pop() || fixedPath.split('\\').pop() || "transcript_fixed.md";
        entries.push({
          label: fileName,
          href: `${base}/${fixedPath}`,
        });
      });
    } else if (job.result.transcriptFixed) {
      // Fallback to single transcriptFixed if transcriptFixedAll is not available
      entries.push({
        label: "transcript_fixed.md",
        href: `${base}/${job.result.transcriptFixed}`,
      });
    }
    if (job.result.rawJson) {
      entries.push({
        label: "response.json",
        href: `${base}/${job.result.rawJson}`,
      });
    }
    return entries;
  }, [
    job?.result?.transcript,
    job?.result?.transcriptFixed,
    job?.result?.transcriptFixedAll,
    job?.result?.rawJson,
  ]);

  // Actions
  const handleFileChange = useCallback((picked: File | null) => {
    setFile(picked);
    setActiveStep("upload");
  }, []);

  const handleStart = useCallback(async () => {
    if (!file) return;
    setSubmitting(true);
    try {
      const { jobId: newJobId } = await createJob(file);
      setJobId(newJobId);
      setActiveStep("transcriber");
    } catch (err) {
      console.error(err);
      alert("Failed to start job. Check server logs.");
    } finally {
      setSubmitting(false);
    }
  }, [file]);

  const handleReset = useCallback(() => {
    setFile(null);
    setJobId(null);
    setJob(null);
    setActiveStep("upload");
    resetLogBuffer();
  }, [resetLogBuffer]);

  /**
   * Switch to a different job by ID.
   * If newJobId is empty string, clears the selection.
   */
  const handleSelectJob = useCallback((newJobId: string) => {
    if (!newJobId) {
      // Clear selection
      setJobId(null);
      setJob(null);
      setFile(null);
      setActiveStep("upload");
      resetLogBuffer();
    } else {
      setJobId(newJobId);
      setFile(null); // Clear file since we're loading an existing job
      setActiveStep("transcriber"); // Start at transcriber step
      resetLogBuffer();
    }
  }, [resetLogBuffer]);

  const handleCancelChunk = useCallback(
    async (idx: number) => {
      if (!jobId) return;
      try {
        await cancelChunk(jobId, idx);
      } catch (err) {
        console.error(err);
        alert("Failed to cancel chunk, see server logs.");
      }
    },
    [jobId]
  );

  /**
   * Split a failed/cancelled chunk into smaller parts.
   */
  const handleSplitChunk = useCallback(
    async (idx: number, parts: number) => {
      if (!jobId) return;
      try {
        await splitChunk(jobId, idx, parts);
      } catch (err) {
        console.error(err);
        alert("Failed to split chunk, see server logs.");
      }
    },
    [jobId]
  );

  /**
   * Cancel a sub-chunk within a split job.
   */
  const handleCancelSubChunk = useCallback(
    async (parentIdx: number, subIdx: number) => {
      if (!jobId) return;
      try {
        await cancelSubChunk(jobId, parentIdx, subIdx);
      } catch (err) {
        console.error(err);
        alert("Failed to cancel sub-chunk, see server logs.");
      }
    },
    [jobId]
  );

  /**
   * Retranscribe a specific sub-chunk from a split job.
   */
  const handleRetranscribeSubChunk = useCallback(
    async (parentIdx: number, subIdx: number) => {
      if (!jobId) return;
      try {
        await retranscribeSubChunk(jobId, parentIdx, subIdx);
      } catch (err) {
        console.error(err);
        alert("Failed to retranscribe sub-chunk, see server logs.");
      }
    },
    [jobId]
  );

  /**
   * Permanently skip a problematic chunk.
   */
  const handleSkipChunk = useCallback(
    async (idx: number) => {
      if (!jobId) return;
      try {
        await skipChunk(jobId, idx);
      } catch (err) {
        console.error(err);
        alert("Failed to skip chunk, see server logs.");
      }
    },
    [jobId]
  );

  /**
   * Start the refiner stage manually.
   */
  const handleStartRefiner = useCallback(async () => {
    if (!jobId) return;
    try {
      await startRefiner(jobId);
    } catch (err) {
      console.error(err);
      alert("Failed to start refiner, see server logs.");
    }
  }, [jobId]);

  /**
   * Rebuild transcript.md from chunk transcripts.
   */
  const handleRebuildTranscript = useCallback(async () => {
    if (!jobId || rebuildingTranscript) return;
    try {
      setRebuildingTranscript(true);
      console.log("[REBUILD] Starting transcript rebuild...");
      await rebuildTranscript(jobId);
      console.log("[REBUILD] Transcript rebuild request sent successfully");
    } catch (err) {
      console.error("[REBUILD] Failed to rebuild transcript:", err);
      alert(`Failed to rebuild transcript: ${err instanceof Error ? err.message : "See server logs"}`);
    } finally {
      setRebuildingTranscript(false);
    }
  }, [jobId, rebuildingTranscript]);

  /**
   * Skip the refiner stage.
   */
  const handleSkipRefiner = useCallback(async () => {
    if (!jobId) return;
    try {
      await skipRefiner(jobId);
    } catch (err) {
      console.error(err);
      alert("Failed to skip refiner, see server logs.");
    }
  }, [jobId]);

  /**
   * Pause transcriber agent.
   */
  const handlePauseTranscriber = useCallback(async () => {
    if (!jobId) return;
    try {
      await pauseAgent(jobId, "transcriber");
    } catch (err) {
      console.error(err);
      alert(err instanceof Error ? err.message : "Failed to pause transcriber, see server logs.");
    }
  }, [jobId]);

  /**
   * Resume transcriber agent.
   */
  const handleResumeTranscriber = useCallback(async () => {
    if (!jobId) return;
    try {
      await resumeAgent(jobId, "transcriber");
    } catch (err) {
      console.error(err);
      alert(err instanceof Error ? err.message : "Failed to resume transcriber, see server logs.");
    }
  }, [jobId]);

  /**
   * Pause refiner agent.
   */
  const handlePauseRefiner = useCallback(async () => {
    if (!jobId) return;
    try {
      await pauseAgent(jobId, "refiner");
    } catch (err) {
      console.error(err);
      alert(err instanceof Error ? err.message : "Failed to pause refiner, see server logs.");
    }
  }, [jobId]);

  /**
   * Resume refiner agent.
   */
  const handleResumeRefiner = useCallback(async () => {
    if (!jobId) return;
    try {
      await resumeAgent(jobId, "refiner");
    } catch (err) {
      console.error(err);
      alert(err instanceof Error ? err.message : "Failed to resume refiner, see server logs.");
    }
  }, [jobId]);

  /**
   * Skip current refiner batch.
   */
  const handleSkipRefinerBatch = useCallback(async () => {
    if (!jobId) return;
    try {
      await skipRefinerBatch(jobId);
    } catch (err) {
      console.error(err);
      alert(err instanceof Error ? err.message : "Failed to skip batch, see server logs.");
    }
  }, [jobId]);

  return {
    // State
    file,
    jobId,
    job,
    activeStep,
    isSubmitting,
    logsPaused,
    bufferedCount,

    // Derived
    getStepStatus,
    logsByStep,
    resultLinks,
    aliases: AGENT_ALIASES,

    // Actions
    setActiveStep,
    handleFileChange,
    handleStart,
    handleReset,
    handleCancelChunk,
    handleSplitChunk,
    handleCancelSubChunk,
    handleRetranscribeSubChunk,
    handleSkipChunk,
    handleStartRefiner,
    handleSkipRefiner,
    handleRebuildTranscript,
    rebuildingTranscript,
    handlePauseTranscriber,
    handleResumeTranscriber,
    handlePauseRefiner,
    handleResumeRefiner,
    handleSkipRefinerBatch,
    toggleLogsPause: togglePause,
    handleSelectJob,
  };
}

