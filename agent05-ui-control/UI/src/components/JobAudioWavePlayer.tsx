import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type PointerEvent as ReactPointerEvent,
} from 'react';

const CANVAS_HEIGHT = 40;
const NUM_PEAKS = 280;

function formatWaveTime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return '0:00';
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = Math.floor(seconds % 60);
  if (h > 0)
    return `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
  return `${m}:${s.toString().padStart(2, '0')}`;
}

function computePeaks(buffer: AudioBuffer, numPeaks: number): Float32Array {
  const channels = buffer.numberOfChannels;
  const length = buffer.length;
  const peaks = new Float32Array(numPeaks);
  if (length === 0) return peaks;
  const block = Math.max(1, Math.floor(length / numPeaks));
  for (let i = 0; i < numPeaks; i++) {
    const start = i * block;
    const end = Math.min(start + block, length);
    let max = 0;
    for (let ch = 0; ch < channels; ch++) {
      const data = buffer.getChannelData(ch);
      for (let s = start; s < end; s++) {
        const v = Math.abs(data[s]);
        if (v > max) max = v;
      }
    }
    peaks[i] = max;
  }
  let m = 1e-8;
  for (let i = 0; i < numPeaks; i++) if (peaks[i] > m) m = peaks[i];
  for (let i = 0; i < numPeaks; i++) peaks[i] /= m;
  return peaks;
}

function drawWaveform(
  canvas: HTMLCanvasElement,
  peaks: Float32Array | null,
  progress: number,
  cssVarLine: string,
  cssVarFill: string,
  cssVarPlayhead: string
) {
  const ctx = canvas.getContext('2d');
  if (!ctx) return;
  const w = canvas.width;
  const h = canvas.height;
  const dpr = window.devicePixelRatio || 1;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  const cssW = w / dpr;
  const cssH = h / dpr;
  ctx.clearRect(0, 0, cssW, cssH);
  const cy = cssH / 2;
  const maxBar = (cssH / 2) * 0.92;

  if (peaks && peaks.length > 0) {
    const n = peaks.length;
    const barW = Math.max(1, cssW / n);
    ctx.fillStyle = cssVarFill;
    for (let i = 0; i < n; i++) {
      const x = (i / n) * cssW;
      const bh = peaks[i] * maxBar;
      ctx.fillRect(x, cy - bh, Math.max(0.75, barW - 0.25), bh * 2);
    }
  }

  ctx.strokeStyle = cssVarLine;
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(0, cy);
  ctx.lineTo(cssW, cy);
  ctx.stroke();

  const x = Math.min(cssW, Math.max(0, progress * cssW));
  ctx.strokeStyle = cssVarPlayhead;
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(x, 0);
  ctx.lineTo(x, cssH);
  ctx.stroke();

  ctx.fillStyle = cssVarPlayhead;
  ctx.beginPath();
  ctx.arc(x, cy, 5, 0, Math.PI * 2);
  ctx.fill();
}

export function JobAudioWavePlayer({
  src,
  t,
  className,
}: {
  src: string;
  t: (key: string) => string;
  className?: string;
}) {
  const audioRef = useRef<HTMLAudioElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const wrapRef = useRef<HTMLDivElement>(null);
  const peaksRef = useRef<Float32Array | null>(null);
  const [peaks, setPeaks] = useState<Float32Array | null>(null);
  const [loadState, setLoadState] = useState<'idle' | 'loading' | 'error'>(
    'idle'
  );
  const [playing, setPlaying] = useState(false);
  const [duration, setDuration] = useState(0);
  const [currentTime, setCurrentTime] = useState(0);
  const rafRef = useRef<number>(0);
  const draggingRef = useRef(false);

  const progress = duration > 0 ? currentTime / duration : 0;

  useEffect(() => {
    peaksRef.current = peaks;
  }, [peaks]);

  useEffect(() => {
    let cancelled = false;
    setLoadState('loading');
    setPeaks(null);
    peaksRef.current = null;

    (async () => {
      try {
        const res = await fetch(src);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const ab = await res.arrayBuffer();
        if (cancelled) return;
        const Ctx =
          window.AudioContext ||
          (
            window as unknown as {
              webkitAudioContext: typeof AudioContext;
            }
          ).webkitAudioContext;
        const ctx = new Ctx();
        const buffer = await ctx.decodeAudioData(ab.slice(0));
        await ctx.close();
        if (cancelled) return;
        const p = computePeaks(buffer, NUM_PEAKS);
        setPeaks(p);
        setLoadState('idle');
      } catch {
        if (!cancelled) setLoadState('error');
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [src]);

  const paint = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const cs = getComputedStyle(canvas);
    const line = cs.getPropertyValue('--pf-wave-line').trim() || '#64748b';
    const fill = cs.getPropertyValue('--pf-wave-fill').trim() || '#3b82f6';
    const ph = cs.getPropertyValue('--pf-wave-playhead').trim() || '#f97316';
    drawWaveform(canvas, peaksRef.current, progress, line, fill, ph);
  }, [progress]);

  useEffect(() => {
    const canvas = canvasRef.current;
    const wrap = wrapRef.current;
    if (!canvas || !wrap) return;

    const resize = () => {
      const rect = wrap.getBoundingClientRect();
      const cssW = Math.max(120, rect.width);
      const dpr = window.devicePixelRatio || 1;
      canvas.width = Math.floor(cssW * dpr);
      canvas.height = Math.floor(CANVAS_HEIGHT * dpr);
      canvas.style.width = `${cssW}px`;
      canvas.style.height = `${CANVAS_HEIGHT}px`;
      paint();
    };

    const ro = new ResizeObserver(resize);
    ro.observe(wrap);
    resize();
    return () => ro.disconnect();
  }, [paint, peaks]);

  useEffect(() => {
    paint();
  }, [paint, progress, peaks]);

  useEffect(() => {
    if (!playing) return;
    const a = audioRef.current;
    if (!a) return;
    const tick = () => {
      setCurrentTime(a.currentTime);
      rafRef.current = requestAnimationFrame(tick);
    };
    rafRef.current = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(rafRef.current);
  }, [playing]);

  const seekToRatio = useCallback((ratio: number) => {
    const a = audioRef.current;
    if (!a || !duration) return;
    const r = Math.min(1, Math.max(0, ratio));
    a.currentTime = r * duration;
    setCurrentTime(a.currentTime);
  }, [duration]);

  const onPointerDown = useCallback(
    (e: ReactPointerEvent<HTMLDivElement>) => {
      if (loadState !== 'idle' || !duration) return;
      draggingRef.current = true;
      (e.target as HTMLElement).setPointerCapture(e.pointerId);
      const wrap = wrapRef.current;
      if (!wrap) return;
      const rect = wrap.getBoundingClientRect();
      const ratio = (e.clientX - rect.left) / rect.width;
      seekToRatio(ratio);
    },
    [loadState, duration, seekToRatio]
  );

  const onPointerMove = useCallback(
    (e: React.PointerEvent<HTMLDivElement>) => {
      if (!draggingRef.current || !duration) return;
      const wrap = wrapRef.current;
      if (!wrap) return;
      const rect = wrap.getBoundingClientRect();
      const ratio = (e.clientX - rect.left) / rect.width;
      seekToRatio(ratio);
    },
    [duration, seekToRatio]
  );

  const onPointerUp = useCallback((e: ReactPointerEvent<HTMLDivElement>) => {
    if (draggingRef.current) {
      draggingRef.current = false;
      try {
        (e.target as HTMLElement).releasePointerCapture(e.pointerId);
      } catch {
        /* ignore */
      }
    }
  }, []);

  const togglePlay = useCallback(() => {
    const a = audioRef.current;
    if (!a || loadState !== 'idle') return;
    if (playing) {
      void a.pause();
    } else {
      void a.play().catch(() => {});
    }
  }, [playing, loadState]);

  return (
    <div className={`pf-wave ${className ?? ''}`}>
      <audio
        ref={audioRef}
        src={src}
        preload="metadata"
        className="pf-wave__audio"
        onPlay={() => setPlaying(true)}
        onPause={() => setPlaying(false)}
        onEnded={() => {
          setPlaying(false);
          setCurrentTime(0);
        }}
        onLoadedMetadata={(e) => {
          const a = e.currentTarget;
          setDuration(a.duration || 0);
        }}
        onTimeUpdate={(e) => {
          if (!draggingRef.current) setCurrentTime(e.currentTarget.currentTime);
        }}
      />
      <button
        type="button"
        className="pf-wave__play"
        onClick={togglePlay}
        disabled={loadState !== 'idle'}
        aria-label={playing ? t('audioWavePause') : t('audioWavePlay')}
      >
        {playing ? '⏸' : '▶'}
      </button>
      <div className="pf-wave__track-row">
        <div
          ref={wrapRef}
          className="pf-wave__track"
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerCancel={onPointerUp}
          role="slider"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(progress * 100)}
          aria-label={t('audioWaveSeek')}
          aria-disabled={loadState !== 'idle' || !duration}
        >
          {loadState === 'idle' && duration > 0 && (
            <span
              className="pf-wave__cursor-time"
              style={{ left: `${progress * 100}%` }}
            >
              {formatWaveTime(currentTime)}
            </span>
          )}
          <canvas
            ref={canvasRef}
            className="pf-wave__canvas"
            height={CANVAS_HEIGHT}
          />
          {loadState === 'loading' && (
            <span className="pf-wave__overlay">{t('audioWaveLoading')}</span>
          )}
          {loadState === 'error' && (
            <span className="pf-wave__overlay pf-wave__overlay--err">
              {t('audioWaveError')}
            </span>
          )}
        </div>
        <span
          className="pf-wave__duration"
          title={t('audioWaveTotalDuration')}
        >
          {formatWaveTime(duration)}
        </span>
      </div>
    </div>
  );
}
