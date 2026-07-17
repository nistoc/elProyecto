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

/** A snapshot is terminal when the job will produce no further updates. */
export function isTerminalSnapshot(
  snap: Pick<JobSnapshot, 'status'> | null | undefined
): boolean {
  return snap?.status === 'done' || snap?.status === 'failed';
}
