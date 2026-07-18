import type { JobSnapshot } from '../types';

export const MAX_RECONNECT_DELAY_MS = 30_000;
export const RECONNECT_JITTER_MS = 500;

/** Exponential backoff with jitter: min(30s, 1s * 2^attempt) + [0, 500) ms. */
export function computeReconnectDelay(
  attempt: number,
  random: () => number = Math.random
): number {
  const base = Math.min(MAX_RECONNECT_DELAY_MS, 1000 * 2 ** attempt);
  return base + random() * RECONNECT_JITTER_MS;
}

/**
 * A snapshot is terminal when the job will produce no further updates.
 * Note: transcription finishing leaves the job at phase=awaiting_refiner with status=done —
 * that is NOT terminal, because the refiner is started manually afterwards. Only a completed
 * phase (refiner done or skipped) or a failed status ends the job.
 */
export function isTerminalSnapshot(
  snap: Pick<JobSnapshot, 'status' | 'phase'> | null | undefined
): boolean {
  return snap?.status === 'failed' || snap?.phase === 'completed';
}
