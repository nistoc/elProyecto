import React, { useState, useRef, useEffect, useCallback } from "react";
import { useAudioPlayer } from "../contexts/AudioPlayerContext";

const WAVEFORM_BARS = 300;
const WAVEFORM_HEIGHT = 48;

function formatTime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return "0:00";
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${m}:${s.toString().padStart(2, "0")}`;
}

/** Decode audio from URL and return peak amplitudes for each bar (0..1). */
async function decodeWaveformPeaks(url: string, bars: number): Promise<{ peaks: number[]; duration: number }> {
  const response = await fetch(url);
  const arrayBuffer = await response.arrayBuffer();
  const audioContext = new AudioContext();
  const audioBuffer = await audioContext.decodeAudioData(arrayBuffer);
  const channel = audioBuffer.getChannelData(0);
  const duration = audioBuffer.duration;
  const samplesPerBar = channel.length / bars;
  const peaks: number[] = [];
  for (let i = 0; i < bars; i++) {
    const start = Math.floor(i * samplesPerBar);
    const end = Math.min(Math.floor((i + 1) * samplesPerBar), channel.length);
    let max = 0;
    for (let j = start; j < end; j++) {
      const v = Math.abs(channel[j]);
      if (v > max) max = v;
    }
    peaks.push(max);
  }
  const maxPeak = Math.max(...peaks, 1e-6);
  const normalized = peaks.map((p) => p / maxPeak);
  return { peaks: normalized, duration };
}

export function AudioPlayer() {
  const { currentUrl, currentTitle, isPlaying, duration, currentTime, stop, toggle, seek } = useAudioPlayer();
  const [waveformPeaks, setWaveformPeaks] = useState<number[]>([]);
  const [waveformDuration, setWaveformDuration] = useState(0);
  const [waveformLoading, setWaveformLoading] = useState(false);
  const waveformRef = useRef<HTMLCanvasElement | null>(null);

  const [isDragging, setIsDragging] = useState(false);
  const [dragTime, setDragTime] = useState(0);
  const waveformWrapRef = useRef<HTMLDivElement | null>(null);

  const displayTime = isDragging ? dragTime : currentTime;
  const displayDuration = duration || waveformDuration || 0;
  const seekPercent = displayDuration > 0 ? (displayTime / displayDuration) * 100 : 0;

  // Load waveform when URL changes
  useEffect(() => {
    if (!currentUrl) {
      setWaveformPeaks([]);
      setWaveformDuration(0);
      return;
    }
    setWaveformLoading(true);
    decodeWaveformPeaks(currentUrl, WAVEFORM_BARS)
      .then(({ peaks, duration: d }) => {
        setWaveformPeaks(peaks);
        setWaveformDuration(d);
      })
      .catch(() => {
        setWaveformPeaks([]);
        setWaveformDuration(0);
      })
      .finally(() => setWaveformLoading(false));
  }, [currentUrl]);

  // Draw waveform on canvas
  useEffect(() => {
    const canvas = waveformRef.current;
    if (!canvas || waveformPeaks.length === 0) return;
    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    const w = rect.width;
    const h = rect.height;
    canvas.width = w * dpr;
    canvas.height = h * dpr;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    ctx.scale(dpr, dpr);
    const barWidth = w / waveformPeaks.length;
    const halfH = h / 2;
    ctx.fillStyle = "#334155";
    waveformPeaks.forEach((peak, i) => {
      const x = i * barWidth;
      const barH = Math.max(1, peak * halfH);
      ctx.fillRect(x, halfH - barH, Math.max(1, barWidth - 0.5), barH * 2);
    });
  }, [waveformPeaks]);

  const getTimeFromMouse = useCallback(
    (clientX: number): number => {
      const wrap = waveformWrapRef.current;
      if (!wrap || displayDuration <= 0) return 0;
      const rect = wrap.getBoundingClientRect();
      const x = clientX - rect.left;
      const p = Math.max(0, Math.min(1, x / rect.width));
      return p * displayDuration;
    },
    [displayDuration]
  );

  const handleSeekPointerDown = useCallback(
    (e: React.MouseEvent | React.TouchEvent) => {
      const clientX = "touches" in e ? e.touches[0].clientX : e.clientX;
      const time = getTimeFromMouse(clientX);
      setDragTime(time);
      setIsDragging(true);
    },
    [getTimeFromMouse]
  );

  const dragTimeRef = useRef(0);
  dragTimeRef.current = dragTime;

  useEffect(() => {
    if (!isDragging) return;
    const onMove = (e: MouseEvent | TouchEvent) => {
      const clientX = "touches" in e ? (e as TouchEvent).touches[0]?.clientX ?? 0 : (e as MouseEvent).clientX;
      setDragTime(getTimeFromMouse(clientX));
    };
    const onUp = () => {
      seek(dragTimeRef.current);
      setIsDragging(false);
    };
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
    window.addEventListener("touchmove", onMove, { passive: true });
    window.addEventListener("touchend", onUp);
    return () => {
      window.removeEventListener("mousemove", onMove);
      window.removeEventListener("mouseup", onUp);
      window.removeEventListener("touchmove", onMove);
      window.removeEventListener("touchend", onUp);
    };
  }, [isDragging, seek, getTimeFromMouse]);

  if (!currentUrl) {
    return null;
  }

  return (
    <div className="audio-player">
      <div className="audio-player__content">
        <div className="audio-player__info">
          <span className="audio-player__title">{currentTitle || "Аудио файл"}</span>
          <div className="audio-player__waveform-row">
            <div
              ref={waveformWrapRef}
              className="audio-player__waveform-wrap"
              onMouseDown={handleSeekPointerDown}
              onTouchStart={handleSeekPointerDown}
              role="slider"
              aria-label="Перемотка"
              aria-valuemin={0}
              aria-valuemax={displayDuration}
              aria-valuenow={displayTime}
            >
              <canvas
                ref={waveformRef}
                className="audio-player__waveform"
                width={800}
                height={WAVEFORM_HEIGHT}
                style={{ height: WAVEFORM_HEIGHT }}
              />
              {waveformLoading && <div className="audio-player__waveform-loading">…</div>}
              {!waveformLoading && waveformPeaks.length > 0 && displayDuration > 0 && (
                <>
                  <div
                    className="audio-player__seek-track"
                    aria-hidden
                  >
                    <div
                      className="audio-player__seek-fill"
                      style={{ width: `${seekPercent}%` }}
                    />
                    <div
                      className="audio-player__seek-thumb"
                      style={{ left: `${seekPercent}%` }}
                      onMouseDown={(e) => {
                        e.preventDefault();
                        e.stopPropagation();
                        handleSeekPointerDown(e);
                      }}
                      onTouchStart={(e) => {
                        e.preventDefault();
                        handleSeekPointerDown(e);
                      }}
                    />
                  </div>
                </>
              )}
            </div>
            <span className="audio-player__time">
              {formatTime(displayTime)} / {formatTime(displayDuration)}
            </span>
          </div>
        </div>
        <div className="audio-player__controls">
          <button
            className="audio-player__btn"
            onClick={toggle}
            title={isPlaying ? "Пауза" : "Воспроизвести"}
          >
            {isPlaying ? "⏸" : "▶"}
          </button>
          <button className="audio-player__btn" onClick={stop} title="Остановить">
            ⏹
          </button>
        </div>
      </div>
    </div>
  );
}
