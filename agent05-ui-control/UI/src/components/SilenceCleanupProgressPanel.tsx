import { useEffect, useMemo, useState } from 'react';
import type { JobSnapshot } from '../types';

export type SilencePanelLocale = 'en' | 'ru' | 'es';

function shouldShowSilenceCleanupPanel(job: JobSnapshot): boolean {
  const phase = (job.phase ?? '').toLowerCase();
  const status = (job.status ?? '').toLowerCase();
  if (phase !== 'transcriber' || status !== 'running') return false;

  const detail = (job.transcriptionPhaseDetail ?? '').trim();
  const detailLc = detail.toLowerCase();
  if (/chunking|transcribing|merging/i.test(detail)) return false;

  const footer = (job.transcriptionFooterHint ?? '').trim();
  const silenceDetail =
    detailLc.includes('converting audio') ||
    detailLc.includes('detecting silence') ||
    detailLc.includes('compressing silence');
  if (silenceDetail) return true;
  if (footer.startsWith('Silence:') || footer.startsWith('Audio:')) return true;
  return false;
}

function titleKeyForDetail(detail: string): string {
  const d = detail.toLowerCase();
  if (d.includes('converting audio')) return 'silencePanelTitleConverting';
  if (d.includes('detecting silence')) return 'silencePanelTitleDetecting';
  if (d.includes('compressing silence')) return 'silencePanelTitleCompressing';
  return 'silencePanelTitleGeneric';
}

function isIndeterminatePhase(detail: string, footer: string): boolean {
  const d = detail.toLowerCase();
  if (d.includes('converting audio') || d.includes('detecting silence')) return true;
  const f = footer.toLowerCase();
  if (
    f.includes('detecting') ||
    f.includes('converting to wav') ||
    f.includes('preparing segments') ||
    f.includes('merging wav')
  )
    return true;
  return false;
}

/** Maps Agent04 pre-chunk percent (1–4) to panel width when we have no segment counts. */
function localBarPercentFromGlobal(globalPercent: number | null | undefined): number {
  if (globalPercent == null || Number.isNaN(globalPercent)) return 0;
  const p = Math.max(1, Math.min(4, globalPercent));
  return Math.round(((p - 1) / 3) * 100);
}

/** Progress from footer `segment current/total` (speech extractions during silence compression). */
function barPercentFromSegmentRatio(
  segmentMatch: RegExpMatchArray | null,
  globalPercent: number | null | undefined
): number {
  if (segmentMatch == null) return localBarPercentFromGlobal(globalPercent);
  const cur = parseInt(segmentMatch[1], 10);
  const tot = parseInt(segmentMatch[2], 10);
  if (!Number.isFinite(cur) || !Number.isFinite(tot) || tot <= 0) {
    return localBarPercentFromGlobal(globalPercent);
  }
  const ratio = Math.min(1, Math.max(0, cur / tot));
  return Math.round(ratio * 100);
}

function formatMmSs(totalSeconds: number): string {
  const sec = Math.max(0, Math.floor(totalSeconds));
  const m = Math.floor(sec / 60);
  const s = sec % 60;
  return `${m}:${s.toString().padStart(2, '0')}`;
}

/** Parses `Silence: segment N/M (~ 5:30 timeline)` or `~ 01:05:30 timeline` from Agent04 footer. */
function parseTimelinePositionSec(footer: string): number | null {
  const m = footer.match(
    /\(\s*~\s*(\d+):(\d+)(?::(\d+))?\s*timeline\s*\)/i
  );
  if (!m) return null;
  if (m[3] !== undefined) {
    const h = parseInt(m[1], 10);
    const min = parseInt(m[2], 10);
    const s = parseInt(m[3], 10);
    if (![h, min, s].every((x) => Number.isFinite(x))) return null;
    return h * 3600 + min * 60 + s;
  }
  const min = parseInt(m[1], 10);
  const s = parseInt(m[2], 10);
  if (![min, s].every((x) => Number.isFinite(x))) return null;
  return min * 60 + s;
}

export function SilenceCleanupProgressPanel({
  job,
  t,
}: {
  job: JobSnapshot;
  t: (key: string) => string;
  /** Reserved for locale-specific formatting (e.g. time). */
  locale?: SilencePanelLocale;
}) {
  const [stepStartedAt, setStepStartedAt] = useState(() => Date.now());
  const [, setTick] = useState(0);

  const detail = (job.transcriptionPhaseDetail ?? '').trim();
  const footer = (job.transcriptionFooterHint ?? '').trim();

  useEffect(() => {
    setStepStartedAt(Date.now());
  }, [detail]);

  useEffect(() => {
    const id = window.setInterval(() => setTick((x) => x + 1), 1000);
    return () => window.clearInterval(id);
  }, []);

  const visible = useMemo(() => shouldShowSilenceCleanupPanel(job), [job]);

  const segmentMatch = useMemo(
    () => footer.match(/segment\s+(\d+)\s*\/\s*(\d+)/i),
    [footer]
  );
  const indeterminate = useMemo(
    () => isIndeterminatePhase(detail, footer),
    [detail, footer]
  );
  const barPct = useMemo(
    () =>
      indeterminate
        ? 0
        : barPercentFromSegmentRatio(
            segmentMatch,
            job.transcriptionProgressPercent ?? null
          ),
    [indeterminate, segmentMatch, job.transcriptionProgressPercent]
  );

  const titleKey = titleKeyForDetail(detail);
  const elapsedSec = Math.floor((Date.now() - stepStartedAt) / 1000);

  const timeline = job.transcriptionSilenceTimeline;
  const durationSec =
    timeline != null && timeline.sourceDurationSec > 0
      ? timeline.sourceDurationSec
      : 0;
  const regions = timeline?.regions ?? [];
  const playheadSec = parseTimelinePositionSec(footer);
  const playheadPct =
    durationSec > 0 &&
    playheadSec != null &&
    Number.isFinite(playheadSec) &&
    playheadSec >= 0
      ? Math.min(100, Math.max(0, (playheadSec / durationSec) * 100))
      : null;
  const showTimelineStrip = durationSec > 0;

  if (!visible) return null;

  return (
    <div
      className="silence-panel"
      role="region"
      aria-label={t('silencePanelAriaLabel')}
    >
      <div className="silence-panel__head">
        <h5 className="silence-panel__title">{t(titleKey)}</h5>
        <span className="silence-panel__elapsed" aria-live="polite">
          {t('silencePanelStepElapsed').replace(
            '{elapsed}',
            formatMmSs(elapsedSec)
          )}
        </span>
      </div>
      {detail ? (
        <p className="silence-panel__technical" title={detail}>
          {detail}
        </p>
      ) : null}
      {showTimelineStrip ? (
        <>
          <div
            className="silence-panel__timeline"
            role="img"
            aria-label={t('silencePanelTimelineCaption')}
          >
            {regions.map((r, i) => {
              const start = Math.max(0, r.startSec);
              const end = Math.max(start, r.endSec);
              const left = Math.min(100, Math.max(0, (start / durationSec) * 100));
              const right = Math.min(
                100,
                Math.max(left, (end / durationSec) * 100)
              );
              const w = Math.max(0, right - left);
              if (w <= 0) return null;
              return (
                <div
                  key={`${i}-${start}-${end}`}
                  className="silence-panel__timeline-region"
                  style={{ left: `${left}%`, width: `${w}%` }}
                />
              );
            })}
            {playheadPct != null ? (
              <div
                className="silence-panel__timeline-head"
                style={{ left: `${playheadPct}%` }}
                title={
                  playheadSec != null ? formatMmSs(playheadSec) : undefined
                }
              />
            ) : null}
          </div>
          <p className="silence-panel__timeline-caption">
            {t('silencePanelTimelineCaption')}
          </p>
          {regions.length >= 500 ? (
            <p className="silence-panel__timeline-approx">
              {t('silencePanelTimelineApproximate')}
            </p>
          ) : null}
        </>
      ) : null}
      <div
        className="silence-panel__track"
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={indeterminate ? undefined : barPct}
        aria-valuetext={
          indeterminate
            ? t('silencePanelIndeterminate')
            : segmentMatch != null
              ? t('silencePanelProgressAria').replace(
                  '{current}',
                  segmentMatch[1]
                ).replace('{total}', segmentMatch[2]).replace(
                  '{percent}',
                  String(barPct)
                )
              : `${barPct}%`
        }
      >
        {indeterminate ? (
          <div className="silence-panel__fill silence-panel__fill--indeterminate" />
        ) : (
          <div
            className="silence-panel__fill"
            style={{ width: `${barPct}%` }}
          />
        )}
      </div>
      {segmentMatch != null ? (
        <p className="silence-panel__segments">
          {t('silencePanelSegments')
            .replace('{current}', segmentMatch[1])
            .replace('{total}', segmentMatch[2])}
        </p>
      ) : null}
      {footer ? <p className="silence-panel__footer">{footer}</p> : null}
      <p className="silence-panel__hint">{t('silencePanelHint')}</p>
      <style>{`
        .silence-panel {
          margin: 0.65rem 0 0.75rem 0;
          padding: 0.55rem 0.65rem;
          border: 1px solid var(--color-border);
          border-radius: 6px;
          background: var(--color-surface);
          box-sizing: border-box;
        }
        .silence-panel__head {
          display: flex;
          flex-wrap: wrap;
          align-items: baseline;
          justify-content: space-between;
          gap: 0.35rem 0.75rem;
          margin-bottom: 0.35rem;
        }
        .silence-panel__title {
          margin: 0;
          font-size: 0.8125rem;
          font-weight: 600;
          color: var(--color-heading);
        }
        .silence-panel__elapsed {
          font-size: 0.72rem;
          font-variant-numeric: tabular-nums;
          color: var(--color-text-secondary);
        }
        .silence-panel__technical {
          margin: 0 0 0.45rem 0;
          font-size: 0.68rem;
          line-height: 1.35;
          color: var(--color-text-secondary);
          word-break: break-word;
        }
        .silence-panel__timeline {
          position: relative;
          height: 10px;
          margin: 0 0 0.25rem 0;
          border-radius: 4px;
          background: var(--color-border);
          overflow: hidden;
        }
        .silence-panel__timeline-region {
          position: absolute;
          top: 0;
          bottom: 0;
          background: rgba(183, 28, 28, 0.42);
          border-radius: 1px;
          box-sizing: border-box;
          min-width: 1px;
        }
        .silence-panel__timeline-head {
          position: absolute;
          top: -1px;
          bottom: -1px;
          width: 2px;
          margin-left: -1px;
          background: var(--color-heading);
          opacity: 0.9;
          z-index: 2;
          pointer-events: none;
        }
        .silence-panel__timeline-caption {
          margin: 0 0 0.35rem 0;
          font-size: 0.65rem;
          line-height: 1.35;
          color: var(--color-text-secondary);
        }
        .silence-panel__timeline-approx {
          margin: 0 0 0.35rem 0;
          font-size: 0.62rem;
          line-height: 1.3;
          color: var(--color-text-secondary);
          font-style: italic;
        }
        .silence-panel__track {
          position: relative;
          height: 6px;
          border-radius: 4px;
          background: var(--color-border);
          overflow: hidden;
        }
        .silence-panel__fill {
          height: 100%;
          border-radius: 4px;
          background: linear-gradient(
            90deg,
            var(--color-accent, #2e7d32),
            var(--color-accent-muted, #43a047)
          );
          transition: width 0.35s ease-out;
        }
        .silence-panel__fill--indeterminate {
          width: 40%;
          animation: silence-panel-indet 1.2s ease-in-out infinite;
        }
        @keyframes silence-panel-indet {
          0% {
            transform: translateX(-100%);
          }
          100% {
            transform: translateX(350%);
          }
        }
        .silence-panel__segments {
          margin: 0.45rem 0 0 0;
          font-size: 0.75rem;
          font-variant-numeric: tabular-nums;
          color: var(--color-heading);
        }
        .silence-panel__footer {
          margin: 0.35rem 0 0 0;
          font-size: 0.72rem;
          line-height: 1.4;
          color: var(--color-text-secondary);
          word-break: break-word;
        }
        .silence-panel__hint {
          margin: 0.45rem 0 0 0;
          font-size: 0.68rem;
          line-height: 1.35;
          color: var(--color-text-secondary);
        }
      `}</style>
    </div>
  );
}
