/**
 * Success chime when a chunk VM reaches `completed` — three short chords, louder and longer
 * so it’s easier to notice (browser may still block audio until user gesture).
 */
const CHORD_PEAK = 0.16;

function playChord(
  ctx: AudioContext,
  start: number,
  duration: number,
  frequencies: readonly number[]
): void {
  const master = ctx.createGain();
  master.connect(ctx.destination);
  master.gain.setValueAtTime(0.0001, start);
  master.gain.exponentialRampToValueAtTime(CHORD_PEAK, start + 0.05);
  master.gain.exponentialRampToValueAtTime(0.0001, start + duration);

  const stopAt = start + duration + 0.02;
  for (const f of frequencies) {
    const o = ctx.createOscillator();
    o.type = 'sine';
    o.frequency.setValueAtTime(f, start);
    o.connect(master);
    o.start(start);
    o.stop(stopAt);
  }
}

export function playTranscriptionChunkDoneChime(): void {
  try {
    const AudioCtx =
      typeof window !== 'undefined'
        ? window.AudioContext ||
          (window as unknown as { webkitAudioContext?: typeof AudioContext })
            .webkitAudioContext
        : undefined;
    if (!AudioCtx) return;

    const ctx = new AudioCtx();
    const t0 = ctx.currentTime;

    // Three rising major triads; start times are staggered (not equal gaps).
    playChord(ctx, t0 + 0, 0.38, [440, 554.37, 659.25]); // A major, ~0.38s
    playChord(ctx, t0 + 0.5, 0.1, [493.88, 622.25, 739.99]); // B major, short ~0.1s
    playChord(ctx, t0 + 1.0, 0.42, [523.25, 659.25, 783.99]); // C major, ~0.42s

    window.setTimeout(() => {
      void ctx.close();
    }, 5650);
  } catch {
    /* autoplay / AudioContext */
  }
}
