import { useCallback } from "react";
import type {
  ChunkEventPayload,
  ChunkState,
  JobSnapshot,
  SplitEventPayload,
  SplitJob,
  SubChunk,
} from "../types";

/**
 * Hook for managing chunk state updates from SSE events.
 */
export function useChunkState(
  setJob: React.Dispatch<React.SetStateAction<JobSnapshot | null>>
) {
  const applyChunkEvent = useCallback(
    (payload: ChunkEventPayload) => {
      setJob((prev) => {
        if (!prev) return prev;

        const base: ChunkState = prev.chunks || {
          total: payload.total ?? 0,
          active: [],
          completed: [],
          cancelled: [],
          failed: [],
          skipped: [],
          splitJobs: {},
        };

        const next: ChunkState = {
          total:
            typeof payload.total === "number" && payload.total >= 0
              ? payload.total
              : base.total,
          active: [...base.active],
          completed: [...base.completed],
          cancelled: [...base.cancelled],
          failed: [...base.failed],
          skipped: [...(base.skipped || [])],
          splitJobs: { ...base.splitJobs },
        };

        if (typeof payload.idx === "number") {
          const idx = payload.idx;
          const remove = (arr: number[]) => arr.filter((x) => x !== idx);

          next.active = remove(next.active);
          next.completed = remove(next.completed);
          next.cancelled = remove(next.cancelled);
          next.failed = remove(next.failed);
          next.skipped = remove(next.skipped || []);

          switch (payload.status) {
            case "started":
              next.active.push(idx);
              break;
            case "completed":
              next.completed.push(idx);
              break;
            case "cancelled":
              next.cancelled.push(idx);
              break;
            case "failed":
              next.failed.push(idx);
              break;
            case "skipped":
              next.skipped = next.skipped || [];
              next.skipped.push(idx);
              break;
            default:
              break;
          }
        }

        return { ...prev, chunks: next };
      });
    },
    [setJob]
  );

  /**
   * Apply a split event to update split job state.
   */
  const applySplitEvent = useCallback(
    (payload: SplitEventPayload) => {
      setJob((prev) => {
        if (!prev) return prev;

        const base: ChunkState = prev.chunks || {
          total: 0,
          active: [],
          completed: [],
          cancelled: [],
          failed: [],
          skipped: [],
          splitJobs: {},
        };

        const splitJobs = { ...(base.splitJobs || {}) };
        const parentIdx = payload.parentIdx;

        switch (payload.type) {
          case "split_started":
            splitJobs[parentIdx] = {
              parentIdx,
              parts: payload.parts || 2,
              status: "splitting",
              subChunks: [],
            };
            break;

          case "split_progress":
            if (splitJobs[parentIdx]) {
              const current = splitJobs[parentIdx];
              
              // Update sub-chunks if provided
              if (payload.subChunks) {
                current.subChunks = payload.subChunks;
                current.status = "transcribing";
              }
              
              // Handle individual sub-chunk events
              if (payload.event && typeof payload.subIdx === "number") {
                const subIdx = payload.subIdx;
                const subChunks = [...current.subChunks];
                
                if (subChunks[subIdx]) {
                  switch (payload.event) {
                    case "sub_chunk_started":
                      subChunks[subIdx] = { ...subChunks[subIdx], status: "started" };
                      break;
                    case "sub_chunk_completed":
                      subChunks[subIdx] = { ...subChunks[subIdx], status: "completed" };
                      break;
                    case "sub_chunk_failed":
                      subChunks[subIdx] = { ...subChunks[subIdx], status: "failed" };
                      break;
                    case "sub_chunk_cancelled":
                      subChunks[subIdx] = { ...subChunks[subIdx], status: "cancelled" };
                      break;
                  }
                  current.subChunks = subChunks;
                }
              }
              
              // Handle merging event
              if (payload.event === "merging") {
                current.status = "merging";
              }
              
              splitJobs[parentIdx] = { ...current };
            }
            break;

          case "split_completed":
            if (splitJobs[parentIdx]) {
              splitJobs[parentIdx] = {
                ...splitJobs[parentIdx],
                status: "completed",
                mergedText: payload.mergedText,
              };
              
              // Move parent chunk from failed/cancelled to completed
              const next = {
                ...base,
                splitJobs,
                failed: base.failed.filter(i => i !== parentIdx),
                cancelled: base.cancelled.filter(i => i !== parentIdx),
                completed: base.completed.includes(parentIdx)
                  ? base.completed
                  : [...base.completed, parentIdx],
              };
              return { ...prev, chunks: next };
            }
            break;

          case "split_failed":
            if (splitJobs[parentIdx]) {
              splitJobs[parentIdx] = {
                ...splitJobs[parentIdx],
                status: "failed",
                error: payload.error,
              };
            }
            break;
        }

        return { ...prev, chunks: { ...base, splitJobs } };
      });
    },
    [setJob]
  );

  return { applyChunkEvent, applySplitEvent };
}

