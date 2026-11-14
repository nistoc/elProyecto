/**
 * How much of a job's speech is Spanish and how much Russian, as GET /api/jobs/:id/language-share counts it
 * from the chunk answers in the job folder. The script tells the language: a line of Latin words is Spanish,
 * of Cyrillic words Russian, mixed in between; the seconds of a line are those of its segment.
 */
export interface LanguageShare {
  lines: number;
  spanishLines: number;
  russianLines: number;
  mixedLines: number;
  /** Lines of an answer without time marks: in the line counts, not in the seconds. */
  untimedLines: number;
  speechSeconds: number;
  spanishSeconds: number;
  russianSeconds: number;
  mixedSeconds: number;
}

export interface TrackLanguageShare {
  jobId: string;
  speaker: string;
  /** Null when the job of the track is gone or has no line to count. */
  share?: LanguageShare | null;
}

/** A transcription job has `total` only; a dialogue has one entry per track and their sum. */
export interface JobLanguageShareReport {
  total?: LanguageShare | null;
  tracks?: TrackLanguageShare[] | null;
}

type T = (key: string) => string;

/** 2052 s → "34.2"; the decimal sign is the locale's. */
export function formatMinutes(seconds: number, decimal = '.'): string {
  return (Math.round(seconds / 6) / 10).toFixed(1).replace('.', decimal);
}

function percent(part: number, whole: number): string {
  return `${whole > 0 ? Math.round((100 * part) / whole) : 0} %`;
}

/** "34.2 min: Spanish 69 % · Russian 26 % · mixed lines 5 %", or null when no timed line was counted. */
function shareText(share: LanguageShare, t: T): string | null {
  if (!(share.speechSeconds > 0)) return null;
  const parts = [
    `${t('languageShareSpanish')} ${percent(share.spanishSeconds, share.speechSeconds)}`,
    `${t('languageShareRussian')} ${percent(share.russianSeconds, share.speechSeconds)}`,
  ];
  if (share.mixedSeconds > 0)
    parts.push(
      `${t('languageShareMixed')} ${percent(share.mixedSeconds, share.speechSeconds)}`
    );
  if (share.untimedLines > 0)
    parts.push(`${t('languageShareUntimed')}: ${share.untimedLines}`);
  const minutes = formatMinutes(share.speechSeconds, t('runSettingsDecimal'));
  return `${minutes} ${t('languageShareMin')}: ${parts.join(' · ')}`;
}

/**
 * The lines of the job view. A transcription job gives one:
 * "Speech 34.2 min: Spanish 69 % · Russian 26 % · mixed lines 5 %".
 * A dialogue gives its total and one line per track with the track's part of all speech:
 * "Student — 40 % of the speech, 8.0 min: Spanish 25 % · Russian 75 %".
 * Empty when there is nothing to show.
 */
export function languageShareLines(
  report: JobLanguageShareReport | null | undefined,
  t: T
): string[] {
  const total = report?.total;
  if (!total) return [];
  const totalText = shareText(total, t);
  if (!totalText) return [];

  const tracks = report?.tracks ?? [];
  if (tracks.length === 0) return [`${t('languageShareTitle')} ${totalText}`];

  const lines = [`${t('languageShareTitleDialogue')} ${totalText}`];
  for (const track of tracks) {
    const text = track.share ? shareText(track.share, t) : null;
    lines.push(
      text && track.share
        ? `${track.speaker} — ${percent(track.share.speechSeconds, total.speechSeconds)} ${t('languageShareOfSpeech')}, ${text}`
        : `${track.speaker} — ${t('languageShareNoLines')}`
    );
  }
  return lines;
}
