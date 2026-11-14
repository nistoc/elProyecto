/**
 * Lessons as the API reads them from the recordings folder (GET /api/sessions):
 * lesson → period (one Zoom recording) → track (one audio file) → runs (jobs that transcribed it).
 */
import type { JobRunSettings } from './runSettings';

export interface LessonRun {
  jobId: string;
  status: string;
  phase: string;
  createdAt?: string | null;
  hasTranscript: boolean;
  hasRefined: boolean;
  /** What the job was transcribed with; null when its folder does not tell. */
  settings?: JobRunSettings | null;
}

export interface LessonDialogue {
  jobId: string;
  title?: string | null;
  createdAt?: string | null;
  trackJobIds: string[];
  merges: number;
}

export interface LessonTrack {
  /** Path of the file from the recordings root; a run is started with it. */
  id: string;
  /** mixed | participant */
  kind: string;
  participant?: string | null;
  fileName: string;
  sizeBytes: number;
  durationSec?: number | null;
  runs: LessonRun[];
}

export interface LessonPeriod {
  id: string;
  /** Local time of the recording start, YYYY-MM-DDTHH:mm:ss. */
  startedAt?: string | null;
  title?: string | null;
  /** ready | unconverted | empty */
  state: string;
  hasVideo: boolean;
  hasChat: boolean;
  hasSubtitles: boolean;
  hasOldTranscript: boolean;
  rawZoomBytes: number;
  tracks: LessonTrack[];
  dialogues: LessonDialogue[];
}

export interface LessonSession {
  id: string;
  /** YYYY-MM-DD */
  date: string;
  title?: string | null;
  /** The period folders lie in the root, not in a day folder. */
  loose: boolean;
  periods: LessonPeriod[];
}

export interface LessonsResponse {
  enabled: boolean;
  rootPath?: string | null;
  estimateUsdPerMinute: number;
  sessions: LessonSession[];
}

/** 2404 → "40:04", 6372 → "1:46:12"; "—" when the length is unknown. */
export function formatDuration(sec?: number | null): string {
  if (sec == null || !Number.isFinite(sec) || sec < 0) return '—';
  const total = Math.round(sec);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const ss = String(s).padStart(2, '0');
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${ss}` : `${m}:${ss}`;
}

/** 1275000000 → "1.3 GB"; sizes are for a glance, so one decimal is enough. */
export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return '0';
  if (bytes >= 1e9) return `${(bytes / 1e9).toFixed(1)} GB`;
  if (bytes >= 1e6) return `${Math.round(bytes / 1e6)} MB`;
  return `${Math.max(1, Math.round(bytes / 1e3))} KB`;
}

/** Length of a period: the mixed track when there is one, else the longest track. */
export function periodDurationSec(period: LessonPeriod): number | null {
  const known = period.tracks.filter(
    (t) => t.durationSec != null && Number.isFinite(t.durationSec)
  );
  if (known.length === 0) return null;
  const mixed = known.find((t) => t.kind === 'mixed');
  if (mixed) return mixed.durationSec as number;
  return Math.max(...known.map((t) => t.durationSec as number));
}

export function sessionDurationSec(session: LessonSession): number | null {
  const parts = session.periods
    .map(periodDurationSec)
    .filter((d): d is number => d != null);
  return parts.length === 0 ? null : parts.reduce((a, b) => a + b, 0);
}

/**
 * Upper bound of what a run of this track may cost: minutes × price per minute, rounded up to a cent.
 * Null when the length of the file is unknown.
 */
export function estimateRunUsd(
  durationSec: number | null | undefined,
  usdPerMinute: number
): number | null {
  if (durationSec == null || !Number.isFinite(durationSec) || durationSec <= 0)
    return null;
  if (!Number.isFinite(usdPerMinute) || usdPerMinute <= 0) return null;
  return Math.ceil((durationSec / 60) * usdPerMinute * 100 - 1e-9) / 100;
}

export function formatUsd(value: number | null | undefined): string {
  return value == null ? '—' : `$${value.toFixed(2)}`;
}

/** "19:03" out of the period start; the title when the name of the recording has no time. */
export function periodTimeLabel(period: LessonPeriod): string {
  const m = /T(\d{2}:\d{2})/.exec(period.startedAt ?? '');
  if (m) return m[1];
  return period.title?.trim() || period.id;
}

export interface SessionCounts {
  periods: number;
  unconverted: number;
  tracks: number;
  tracksWithRuns: number;
  runs: number;
  dialogues: number;
}

export function sessionCounts(session: LessonSession): SessionCounts {
  const tracks = session.periods.flatMap((p) => p.tracks);
  const runIds = new Set(tracks.flatMap((t) => t.runs.map((r) => r.jobId)));
  const dialogueIds = new Set(
    session.periods.flatMap((p) => p.dialogues.map((d) => d.jobId))
  );
  return {
    periods: session.periods.length,
    unconverted: session.periods.filter((p) => p.state === 'unconverted')
      .length,
    tracks: tracks.length,
    tracksWithRuns: tracks.filter((t) => t.runs.length > 0).length,
    runs: runIds.size,
    dialogues: dialogueIds.size,
  };
}

/** One participant track that can go into a dialogue: its runs that have a transcript, oldest first. */
export interface DialogueCandidate {
  trackId: string;
  speaker: string;
  runs: LessonRun[];
  /** The run taken by default: the newest one with a transcript. */
  jobId: string;
}

/** Participant tracks of the period that already have a transcript; a dialogue needs at least two. */
export function dialogueCandidates(period: LessonPeriod): DialogueCandidate[] {
  return period.tracks
    .filter((t) => t.kind === 'participant')
    .map((t) => {
      const runs = t.runs.filter((r) => r.hasTranscript);
      return {
        trackId: t.id,
        speaker: t.participant?.trim() || t.fileName,
        runs,
        jobId: runs.length > 0 ? runs[runs.length - 1].jobId : '',
      };
    })
    .filter((c) => c.runs.length > 0);
}

export function canBuildDialogue(period: LessonPeriod): boolean {
  return dialogueCandidates(period).length >= 2;
}

/** "2026-10-23 20:21" — the title a new dialogue gets unless the operator types another. */
export function defaultDialogueTitle(
  session: LessonSession,
  period: LessonPeriod
): string {
  const m = /T(\d{2}:\d{2})/.exec(period.startedAt ?? '');
  return m ? `${session.date} ${m[1]}` : session.date;
}
