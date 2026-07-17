import { describe, expect, it } from 'vitest';
import {
  computeReconnectDelay,
  isTerminalSnapshot,
  MAX_RECONNECT_DELAY_MS,
  RECONNECT_JITTER_MS,
} from './streamReconnect';

describe('computeReconnectDelay', () => {
  const noJitter = () => 0;

  it('grows exponentially without jitter', () => {
    expect(computeReconnectDelay(1, noJitter)).toBe(2000);
    expect(computeReconnectDelay(2, noJitter)).toBe(4000);
    expect(computeReconnectDelay(3, noJitter)).toBe(8000);
    expect(computeReconnectDelay(4, noJitter)).toBe(16000);
  });

  it('caps at 30s', () => {
    expect(computeReconnectDelay(5, noJitter)).toBe(MAX_RECONNECT_DELAY_MS);
    expect(computeReconnectDelay(10, noJitter)).toBe(MAX_RECONNECT_DELAY_MS);
  });

  it('adds jitter strictly below 500ms', () => {
    const maxJitter = () => 0.999;
    const d = computeReconnectDelay(1, maxJitter);
    expect(d).toBeGreaterThanOrEqual(2000);
    expect(d).toBeLessThan(2000 + RECONNECT_JITTER_MS);
  });
});

describe('isTerminalSnapshot', () => {
  it('done and failed are terminal', () => {
    expect(isTerminalSnapshot({ status: 'done' })).toBe(true);
    expect(isTerminalSnapshot({ status: 'failed' })).toBe(true);
  });

  it('running, queued and missing snapshots are not terminal', () => {
    expect(isTerminalSnapshot({ status: 'running' })).toBe(false);
    expect(isTerminalSnapshot({ status: 'queued' })).toBe(false);
    expect(isTerminalSnapshot(null)).toBe(false);
    expect(isTerminalSnapshot(undefined)).toBe(false);
  });
});
