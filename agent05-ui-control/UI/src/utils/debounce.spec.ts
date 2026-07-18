import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { debounce } from './debounce';

describe('debounce', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('collapses rapid calls into a single trailing invocation', () => {
    const fn = vi.fn();
    const d = debounce(fn, 3000);
    d(1);
    d(2);
    d(3);
    expect(fn).not.toHaveBeenCalled();
    vi.advanceTimersByTime(3000);
    expect(fn).toHaveBeenCalledTimes(1);
    expect(fn).toHaveBeenLastCalledWith(3);
  });

  it('runs again after the window elapses', () => {
    const fn = vi.fn();
    const d = debounce(fn, 3000);
    d('a');
    vi.advanceTimersByTime(3000);
    d('b');
    vi.advanceTimersByTime(3000);
    expect(fn).toHaveBeenCalledTimes(2);
    expect(fn).toHaveBeenNthCalledWith(1, 'a');
    expect(fn).toHaveBeenNthCalledWith(2, 'b');
  });

  it('cancel prevents a pending invocation', () => {
    const fn = vi.fn();
    const d = debounce(fn, 3000);
    d('x');
    d.cancel();
    vi.advanceTimersByTime(3000);
    expect(fn).not.toHaveBeenCalled();
  });
});
