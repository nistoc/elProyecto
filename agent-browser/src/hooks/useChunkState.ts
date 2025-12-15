import { useCallback } from "react";
import type { ChunkEventPayload, ChunkState, JobSnapshot } from "../types";

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
        };

        if (typeof payload.idx === "number") {
          const idx = payload.idx;
          const remove = (arr: number[]) => arr.filter((x) => x !== idx);

          next.active = remove(next.active);
          next.completed = remove(next.completed);
          next.cancelled = remove(next.cancelled);
          next.failed = remove(next.failed);

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
            default:
              break;
          }
        }

        return { ...prev, chunks: next };
      });
    },
    [setJob]
  );

  return { applyChunkEvent };
}

