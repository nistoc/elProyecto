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
  /** Overlaps, interruptions and pauses of a dialogue; null for a transcription job. */
  conversation?: ConversationReport | null;
}

type T = (key: string) => string;

/** 2052 s → "34.2"; the decimal sign is the locale's. */
export function formatMinutes(seconds: number, decimal = '.'): string {
  return (Math.round(seconds / 6) / 10).toFixed(1).replace('.', decimal);
}

function percent(part: number, whole: number): string {
  return `${whole > 0 ? Math.round((100 * part) / whole) : 0} %`;
}

/**
 * One card of the job view: a title, what is said of the whole, and its parts.
 * The same block read as one line is what the job view printed before it had cards.
 */
export interface StatBlock {
  /** "Speech of the dialogue", "Talk" or the name of a participant. */
  title: string;
  /** What stands between the title and the parts: "19.5 min", "78 % of the speech, 15.3 min". */
  lead?: string;
  parts: string[];
  /** A participant: the title is set off by a dash; a total: by a space. */
  ofParticipant: boolean;
}

/** "Speech 34.2 min: Spanish 69 % · Russian 26 %", "Student — interruptions 12 · short assents 28". */
export function statBlockLine(block: StatBlock): string {
  const joint = block.ofParticipant ? ' — ' : ' ';
  const tail = block.parts.join(' · ');
  if (block.lead)
    return `${block.title}${joint}${block.lead}${tail ? `: ${tail}` : ''}`;
  return tail ? `${block.title}${joint}${tail}` : block.title;
}

/** "34.2 min" and "Spanish 69 %", "Russian 26 %", "mixed lines 5 %"; null when no timed line was counted. */
function shareParts(
  share: LanguageShare,
  t: T
): { minutes: string; parts: string[] } | null {
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
  return { minutes: `${minutes} ${t('languageShareMin')}`, parts };
}

/**
 * The short form next to a run in a list: "es 74 % · ru 18 %" — the parts of the speech said in Spanish and in
 * Russian (what is left to 100 % are mixed lines). Empty when no timed line was counted.
 */
export function languageShareShort(
  share: LanguageShare | null | undefined
): string {
  if (!share || !(share.speechSeconds > 0)) return '';
  return (
    `es ${percent(share.spanishSeconds, share.speechSeconds)} · ` +
    `ru ${percent(share.russianSeconds, share.speechSeconds)}`
  );
}

/**
 * The cards of the job view. A transcription job gives one:
 * "Speech 34.2 min: Spanish 69 % · Russian 26 % · mixed lines 5 %".
 * A dialogue gives its total and one card per track with the track's part of all speech:
 * "Student — 40 % of the speech, 8.0 min: Spanish 25 % · Russian 75 %".
 * Empty when there is nothing to show.
 */
export function languageShareBlocks(
  report: JobLanguageShareReport | null | undefined,
  t: T
): StatBlock[] {
  const total = report?.total;
  if (!total) return [];
  const whole = shareParts(total, t);
  if (!whole) return [];

  const tracks = report?.tracks ?? [];
  const blocks: StatBlock[] = [
    {
      title: t(
        tracks.length === 0
          ? 'languageShareTitle'
          : 'languageShareTitleDialogue'
      ),
      lead: whole.minutes,
      parts: whole.parts,
      ofParticipant: false,
    },
  ];
  for (const track of tracks) {
    const own = track.share ? shareParts(track.share, t) : null;
    blocks.push(
      own && track.share
        ? {
            title: track.speaker,
            lead: `${percent(track.share.speechSeconds, total.speechSeconds)} ${t('languageShareOfSpeech')}, ${own.minutes}`,
            parts: own.parts,
            ofParticipant: true,
          }
        : {
            title: track.speaker,
            lead: t('languageShareNoLines'),
            parts: [],
            ofParticipant: true,
          }
    );
  }
  return blocks;
}

/** The same as lines of text, one per card. */
export function languageShareLines(
  report: JobLanguageShareReport | null | undefined,
  t: T
): string[] {
  return languageShareBlocks(report, t).map(statBlockLine);
}

/** How the talk of a dialogue goes, `conversation` of the same answer; null when it cannot be counted. */
export interface ConversationReport {
  /** A pause is "long" from this many seconds. */
  longPauseSeconds: number;
  spanSeconds: number;
  /** Seconds in which two or more participants speak at once (an upper estimate). */
  overlapSeconds: number;
  silenceSeconds: number;
  speakerChanges: number;
  tracks: TrackConversation[];
}

export interface TrackConversation {
  jobId: string;
  speaker: string;
  lines: number;
  /** Lines begun over the speech of another participant that then went on together with it. */
  interruptions: number;
  /** Short assents said over the speech of another participant. */
  backchannels: number;
  overlapSeconds: number;
  longPausesBeforeAnswer: number;
  longestPauseBeforeAnswerSeconds: number;
  /** The usual wait before this participant answers another one; null when they never did. */
  medianAnswerGapSeconds?: number | null;
  longPausesInOwnSpeech: number;
  longestPauseInOwnSpeechSeconds: number;
}

/** 3.94 → "3.9"; the decimal sign is the locale's. */
export function formatSeconds(seconds: number, decimal = '.'): string {
  return (Math.round(seconds * 10) / 10).toFixed(1).replace('.', decimal);
}

/**
 * The cards of the job view for a dialogue:
 * "Talk 39.9 min: both at once 4.8 min · silence 6.4 min · speaker changes 399" and one per participant:
 * "Student — interruptions 12 · short assents over the other 28 · usual wait before an answer 0.5 s ·
 * pauses from 3 s: before an answer 9 (up to 6.0 s), in own speech 5 (up to 4.5 s)".
 * Empty when there is no talk to show.
 */
export function conversationBlocks(
  report: JobLanguageShareReport | null | undefined,
  t: T
): StatBlock[] {
  const talk = report?.conversation;
  if (!talk || !(talk.spanSeconds > 0)) return [];
  const decimal = t('runSettingsDecimal');
  const min = (seconds: number) =>
    `${formatMinutes(seconds, decimal)} ${t('languageShareMin')}`;
  const sec = (seconds: number) =>
    `${formatSeconds(seconds, decimal)} ${t('runSettingsSecUnit')}`;
  const pauses = (count: number, longest: number) =>
    count > 0 ? `${count} (${t('conversationUpTo')} ${sec(longest)})` : '0';

  const blocks: StatBlock[] = [
    {
      title: t('conversationTitle'),
      lead: min(talk.spanSeconds),
      parts: [
        `${t('conversationOverlap')} ${min(talk.overlapSeconds)}`,
        `${t('conversationSilence')} ${min(talk.silenceSeconds)}`,
        `${t('conversationChanges')} ${talk.speakerChanges}`,
      ],
      ofParticipant: false,
    },
  ];
  for (const track of talk.tracks ?? []) {
    const parts = [
      `${t('conversationInterruptions')} ${track.interruptions}`,
      `${t('conversationBackchannels')} ${track.backchannels}`,
    ];
    if (track.medianAnswerGapSeconds != null)
      parts.push(
        `${t('conversationAnswerWait')} ${sec(track.medianAnswerGapSeconds)}`
      );
    parts.push(
      `${t('conversationLongPauses')} ${talk.longPauseSeconds} ${t('runSettingsSecUnit')}: ` +
        `${t('conversationBeforeAnswer')} ${pauses(track.longPausesBeforeAnswer, track.longestPauseBeforeAnswerSeconds)}, ` +
        `${t('conversationInOwnSpeech')} ${pauses(track.longPausesInOwnSpeech, track.longestPauseInOwnSpeechSeconds)}`
    );
    blocks.push({ title: track.speaker, parts, ofParticipant: true });
  }
  return blocks;
}

/** The same as lines of text, one per card. */
export function conversationLines(
  report: JobLanguageShareReport | null | undefined,
  t: T
): string[] {
  return conversationBlocks(report, t).map(statBlockLine);
}
