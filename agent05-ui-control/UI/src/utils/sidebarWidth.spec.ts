import { describe, expect, it } from 'vitest';
import {
  SIDEBAR_DEFAULT_WIDTH,
  SIDEBAR_MAX_WIDTH,
  SIDEBAR_MIN_WIDTH,
  clampSidebarWidth,
  parseStoredSidebarWidth,
  sidebarDetail,
  sidebarMaxWidth,
} from './sidebarWidth';

describe('sidebarMaxWidth', () => {
  it('leaves the job view the larger part of the window', () => {
    expect(sidebarMaxWidth(1000)).toBe(600);
    expect(sidebarMaxWidth(1366)).toBe(819);
  });

  it('stops at the upper bound on a very wide screen', () => {
    expect(sidebarMaxWidth(3840)).toBe(SIDEBAR_MAX_WIDTH);
  });

  it('never goes under the least width, also in a tiny window', () => {
    expect(sidebarMaxWidth(240)).toBe(SIDEBAR_MIN_WIDTH);
    expect(sidebarMaxWidth(0)).toBe(SIDEBAR_MAX_WIDTH);
  });
});

describe('clampSidebarWidth', () => {
  it('keeps a width that fits', () => {
    expect(clampSidebarWidth(420, 1920)).toBe(420);
    expect(clampSidebarWidth(420.6, 1920)).toBe(421);
  });

  it('pulls a width back between the bounds', () => {
    expect(clampSidebarWidth(50, 1920)).toBe(SIDEBAR_MIN_WIDTH);
    expect(clampSidebarWidth(5000, 1920)).toBe(1152);
    expect(clampSidebarWidth(900, 1000)).toBe(600);
  });

  it('falls back to the usual width for what is not a number', () => {
    expect(clampSidebarWidth(Number.NaN, 1920)).toBe(SIDEBAR_DEFAULT_WIDTH);
  });
});

describe('parseStoredSidebarWidth', () => {
  it('reads a kept width', () => {
    expect(parseStoredSidebarWidth('512')).toBe(512);
  });

  it('gives null for nothing and for rubbish', () => {
    expect(parseStoredSidebarWidth(null)).toBeNull();
    expect(parseStoredSidebarWidth('')).toBeNull();
    expect(parseStoredSidebarWidth('wide')).toBeNull();
    expect(parseStoredSidebarWidth('-3')).toBeNull();
  });
});

describe('sidebarDetail', () => {
  it('tells more as the list gets wider', () => {
    expect(sidebarDetail(SIDEBAR_DEFAULT_WIDTH)).toBe(0);
    expect(sidebarDetail(379)).toBe(0);
    expect(sidebarDetail(380)).toBe(1);
    expect(sidebarDetail(559)).toBe(1);
    expect(sidebarDetail(560)).toBe(2);
    expect(sidebarDetail(899)).toBe(2);
    expect(sidebarDetail(900)).toBe(3);
    expect(sidebarDetail(1400)).toBe(3);
  });
});
