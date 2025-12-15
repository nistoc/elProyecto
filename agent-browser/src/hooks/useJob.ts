import { useCallback, useEffect, useMemo, useState } from "react";
import { cancelChunk, createJob, fetchJob, subscribeToJob } from "../api";
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

  const { applyChunkEvent } = useChunkState(setJob);
  const {
    logsPaused,
    bufferedCount,
    handleLog,
    togglePause,
    reset: resetLogBuffer,
  } = useLogBuffer(setJob);

  // Subscribe to job SSE stream
  useEffect(() => {
    if (!jobId) return;

    let close = () => {};

    fetchJob(jobId)
      .then((snapshot) => setJob(snapshot))
      .catch(() => null)
      .finally(() => {
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
            case "status":
              setJob((prev) => (prev ? { ...prev, ...event.payload } : prev));
              break;
            case "done":
              setActiveStep("result");
              break;
          }
        });
      });

    return () => close();
  }, [jobId, handleLog, applyChunkEvent]);

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
          if (job.phase === "refiner" || job.phase === "completed")
            return "done";
          return "waiting";
        case "refiner":
          if (job.phase === "refiner") return "running";
          if (job.phase === "completed") return "done";
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
    if (job.result.transcriptFixed) {
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
  }, [job]);

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
    toggleLogsPause: togglePause,
  };
}

