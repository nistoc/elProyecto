import { getJob } from "./jobStore.js";

/**
 * SSE subscribers management.
 */
const subscribers = new Map();

/**
 * Broadcast event to all subscribers of a job.
 */
export function broadcast(jobId, event) {
  const listeners = subscribers.get(jobId) || [];
  listeners.forEach((fn) => fn(event));
}

/**
 * Add a subscriber for job events.
 * Returns unsubscribe function.
 */
export function subscribe(jobId, sendFn) {
  const current = subscribers.get(jobId) || [];
  subscribers.set(jobId, [...current, sendFn]);

  return () => {
    const arr = subscribers.get(jobId) || [];
    subscribers.set(
      jobId,
      arr.filter((fn) => fn !== sendFn)
    );
  };
}

/**
 * Push a log entry to job and broadcast it.
 */
export function pushLog(jobId, message, level = "info") {
  const job = getJob(jobId);
  if (!job) return;

  const entry = { ts: Date.now(), level, message };
  job.logs.push(entry);

  // Keep the log size reasonable
  if (job.logs.length > 800) {
    job.logs.splice(0, job.logs.length - 800);
  }

  broadcast(jobId, { type: "log", payload: entry });
}

/**
 * Update job and broadcast status change.
 */
export function updateJobAndBroadcast(jobId, patch) {
  const job = getJob(jobId);
  if (!job) return;
  Object.assign(job, patch);
  broadcast(jobId, { type: "status", payload: patch });
}

