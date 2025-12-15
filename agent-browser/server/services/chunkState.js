import { getJob } from "./jobStore.js";
import { broadcast } from "./broadcaster.js";

export const CHUNK_MARKER = "@@CHUNK_EVENT";

/**
 * Update chunks state from event payload.
 */
export function updateChunksState(jobId, payload) {
  const job = getJob(jobId);
  if (!job) return;

  if (!job.chunks) {
    job.chunks = {
      total: 0,
      active: [],
      completed: [],
      cancelled: [],
      failed: [],
    };
  }

  const state = job.chunks;

  if (typeof payload.total === "number" && payload.total >= 0) {
    state.total = payload.total;
  }

  if (typeof payload.idx !== "number") {
    broadcast(jobId, { type: "chunk", payload });
    return;
  }

  const idx = payload.idx;

  // Remove from all arrays first
  const remove = (arr) => {
    const pos = arr.indexOf(idx);
    if (pos >= 0) arr.splice(pos, 1);
  };

  remove(state.active);
  remove(state.completed);
  remove(state.cancelled);
  remove(state.failed);

  // Add to appropriate array
  switch (payload.status) {
    case "started":
      state.active.push(idx);
      break;
    case "completed":
      state.completed.push(idx);
      break;
    case "cancelled":
      state.cancelled.push(idx);
      break;
    case "failed":
      state.failed.push(idx);
      break;
    default:
      break;
  }

  broadcast(jobId, { type: "chunk", payload });
}

/**
 * Try to parse and process a chunk event from stdout line.
 */
export function tryProcessChunkEvent(jobId, line) {
  const markerIndex = line.indexOf(CHUNK_MARKER);
  if (markerIndex === -1) return false;

  const data = line.slice(markerIndex + CHUNK_MARKER.length).trim();
  try {
    const payload = JSON.parse(data);
    updateChunksState(jobId, payload);
    return true;
  } catch (err) {
    console.warn("Failed to parse chunk event", err);
    return false;
  }
}

