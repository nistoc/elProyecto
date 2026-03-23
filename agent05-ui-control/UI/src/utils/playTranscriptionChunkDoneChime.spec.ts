import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { playTranscriptionChunkDoneChime } from './playTranscriptionChunkDoneChime';

describe('playTranscriptionChunkDoneChime', () => {
  const closeSpy = vi.fn().mockResolvedValue(undefined);
  const contexts: { createOscillator: ReturnType<typeof vi.fn>; createGain: ReturnType<typeof vi.fn> }[] =
    [];

  beforeEach(() => {
    closeSpy.mockClear();
    contexts.length = 0;

    class MockOscillator {
      type = 'sine';
      frequency = { setValueAtTime: vi.fn() };
      connect = vi.fn();
      start = vi.fn();
      stop = vi.fn();
    }

    class MockGain {
      gain = {
        setValueAtTime: vi.fn(),
        exponentialRampToValueAtTime: vi.fn(),
      };
      connect = vi.fn();
    }

    class MockAudioContext {
      currentTime = 0;
      destination = {};
      createOscillator = vi.fn(() => new MockOscillator());
      createGain = vi.fn(() => new MockGain());
      close = closeSpy;

      constructor() {
        contexts.push(this);
      }
    }

    vi.stubGlobal('window', {
      AudioContext: MockAudioContext,
      setTimeout: (fn: () => void, _ms?: number) => {
        fn();
        return 0 as unknown as ReturnType<typeof setTimeout>;
      },
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('plays three triads (nine oscillators, three master gains) and closes the context', () => {
    playTranscriptionChunkDoneChime();

    expect(contexts).toHaveLength(1);
    const ctx = contexts[0]!;
    expect(ctx.createOscillator).toHaveBeenCalledTimes(9);
    expect(ctx.createGain).toHaveBeenCalledTimes(3);
    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('does not throw when AudioContext is missing', () => {
    vi.stubGlobal('window', { setTimeout });
    expect(() => playTranscriptionChunkDoneChime()).not.toThrow();
  });
});
