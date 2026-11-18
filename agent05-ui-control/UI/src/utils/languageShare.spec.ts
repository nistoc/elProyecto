import { describe, expect, it } from 'vitest';
import {
  conversationBlocks,
  conversationLines,
  formatMinutes,
  formatSeconds,
  languageShareBlocks,
  languageShareLines,
  languageShareShort,
  statBlockLine,
  type ConversationReport,
  type LanguageShare,
  type TrackConversation,
} from './languageShare';

const words: Record<string, string> = {
  languageShareTitle: 'Speech',
  languageShareTitleDialogue: 'Speech of the dialogue',
  languageShareMin: 'min',
  languageShareSpanish: 'Spanish',
  languageShareRussian: 'Russian',
  languageShareMixed: 'mixed lines',
  languageShareUntimed: 'lines without time, not counted',
  languageShareOfSpeech: 'of the speech',
  languageShareNoLines: 'no lines with time',
  conversationTitle: 'Talk',
  conversationOverlap: 'both at once',
  conversationSilence: 'silence',
  conversationChanges: 'speaker changes',
  conversationInterruptions: 'interruptions',
  conversationBackchannels: 'short assents over the other',
  conversationAnswerWait: 'usual wait before an answer',
  conversationLongPauses: 'pauses from',
  conversationBeforeAnswer: 'before an answer',
  conversationInOwnSpeech: 'in own speech',
  conversationUpTo: 'up to',
  runSettingsSecUnit: 's',
  runSettingsDecimal: '.',
};
const t = (key: string) => words[key] ?? key;
const tComma = (key: string) => (key === 'runSettingsDecimal' ? ',' : t(key));

const share = (
  spanish: number,
  russian: number,
  mixed = 0,
  untimedLines = 0
): LanguageShare => ({
  lines: 10,
  spanishLines: 5,
  russianLines: 4,
  mixedLines: 1,
  untimedLines,
  speechSeconds: spanish + russian + mixed,
  spanishSeconds: spanish,
  russianSeconds: russian,
  mixedSeconds: mixed,
});

describe('formatMinutes', () => {
  it('gives minutes with one decimal in the sign of the locale', () => {
    expect(formatMinutes(2052)).toBe('34.2');
    expect(formatMinutes(2052, ',')).toBe('34,2');
    expect(formatMinutes(60)).toBe('1.0');
    expect(formatMinutes(0)).toBe('0.0');
  });
});

describe('languageShareLines', () => {
  it('gives one line for a transcription job', () => {
    expect(languageShareLines({ total: share(1380, 520, 100) }, t)).toEqual([
      'Speech 33.3 min: Spanish 69 % · Russian 26 % · mixed lines 5 %',
    ]);
  });

  it('writes the minutes with the decimal sign of the locale', () => {
    expect(languageShareLines({ total: share(90, 90) }, tComma)).toEqual([
      'Speech 3,0 min: Spanish 50 % · Russian 50 %',
    ]);
  });

  it('leaves mixed lines out when there are none and tells of lines without time', () => {
    expect(languageShareLines({ total: share(60, 0, 0, 2) }, t)).toEqual([
      'Speech 1.0 min: Spanish 100 % · Russian 0 % · lines without time, not counted: 2',
    ]);
  });

  it('gives the total of a dialogue and a line per track with its part of the speech', () => {
    const report = {
      total: share(840, 360),
      tracks: [
        { jobId: 'a', speaker: 'Student', share: share(120, 360) },
        { jobId: 'b', speaker: 'Teacher', share: share(720, 0) },
        { jobId: 'c', speaker: 'Guest', share: null },
      ],
    };
    expect(languageShareLines(report, t)).toEqual([
      'Speech of the dialogue 20.0 min: Spanish 70 % · Russian 30 %',
      'Student — 40 % of the speech, 8.0 min: Spanish 25 % · Russian 75 %',
      'Teacher — 60 % of the speech, 12.0 min: Spanish 100 % · Russian 0 %',
      'Guest — no lines with time',
    ]);
  });

  it('gives nothing when nothing was counted', () => {
    expect(languageShareLines(null, t)).toEqual([]);
    expect(languageShareLines({ total: null, tracks: [] }, t)).toEqual([]);
    expect(languageShareLines({ total: share(0, 0, 0, 3) }, t)).toEqual([]);
  });
});

const talker = (
  speaker: string,
  over: Partial<TrackConversation> = {}
): TrackConversation => ({
  jobId: speaker.toLowerCase(),
  speaker,
  lines: 100,
  interruptions: 0,
  backchannels: 0,
  overlapSeconds: 0,
  longPausesBeforeAnswer: 0,
  longestPauseBeforeAnswerSeconds: 0,
  medianAnswerGapSeconds: null,
  longPausesInOwnSpeech: 0,
  longestPauseInOwnSpeechSeconds: 0,
  ...over,
});

const talk = (tracks: TrackConversation[]): ConversationReport => ({
  longPauseSeconds: 3,
  spanSeconds: 2394,
  overlapSeconds: 288,
  silenceSeconds: 384,
  speakerChanges: 399,
  tracks,
});

describe('formatSeconds', () => {
  it('gives seconds with one decimal in the sign of the locale', () => {
    expect(formatSeconds(3.94)).toBe('3.9');
    expect(formatSeconds(0.5, ',')).toBe('0,5');
    expect(formatSeconds(6)).toBe('6.0');
  });
});

describe('conversationLines', () => {
  it('tells how the talk of a dialogue goes and a line per participant', () => {
    const report = {
      conversation: talk([
        talker('Student', {
          interruptions: 12,
          backchannels: 28,
          medianAnswerGapSeconds: 0.5,
          longPausesBeforeAnswer: 9,
          longestPauseBeforeAnswerSeconds: 6.04,
          longPausesInOwnSpeech: 5,
          longestPauseInOwnSpeechSeconds: 4.5,
        }),
        talker('Teacher', { interruptions: 3, medianAnswerGapSeconds: 0.62 }),
      ]),
    };
    expect(conversationLines(report, t)).toEqual([
      'Talk 39.9 min: both at once 4.8 min · silence 6.4 min · speaker changes 399',
      'Student — interruptions 12 · short assents over the other 28 · usual wait before an answer 0.5 s · ' +
        'pauses from 3 s: before an answer 9 (up to 6.0 s), in own speech 5 (up to 4.5 s)',
      'Teacher — interruptions 3 · short assents over the other 0 · usual wait before an answer 0.6 s · ' +
        'pauses from 3 s: before an answer 0, in own speech 0',
    ]);
  });

  it('leaves the usual wait out for a participant who never answered', () => {
    const lines = conversationLines(
      { conversation: talk([talker('Guest'), talker('Host')]) },
      t
    );
    expect(lines[1]).toBe(
      'Guest — interruptions 0 · short assents over the other 0 · pauses from 3 s: before an answer 0, in own speech 0'
    );
  });

  it('writes minutes and seconds with the decimal sign of the locale', () => {
    const lines = conversationLines(
      {
        conversation: talk([
          talker('Student', {
            medianAnswerGapSeconds: 0.5,
            longPausesInOwnSpeech: 1,
            longestPauseInOwnSpeechSeconds: 3.5,
          }),
        ]),
      },
      tComma
    );
    expect(lines[0]).toBe(
      'Talk 39,9 min: both at once 4,8 min · silence 6,4 min · speaker changes 399'
    );
    expect(lines[1]).toContain('usual wait before an answer 0,5 s');
    expect(lines[1]).toContain('in own speech 1 (up to 3,5 s)');
  });

  it('gives nothing for a job without a talk', () => {
    expect(conversationLines(null, t)).toEqual([]);
    expect(conversationLines({ total: share(60, 60) }, t)).toEqual([]);
    expect(conversationLines({ conversation: null }, t)).toEqual([]);
    expect(
      conversationLines({ conversation: { ...talk([]), spanSeconds: 0 } }, t)
    ).toEqual([]);
  });
});

describe('languageShareShort', () => {
  it('gives the parts said in Spanish and in Russian', () => {
    expect(languageShareShort(share(740, 180, 80))).toBe('es 74 % · ru 18 %');
    expect(languageShareShort(share(60, 0))).toBe('es 100 % · ru 0 %');
  });

  it('gives nothing when no timed line was counted', () => {
    expect(languageShareShort(null)).toBe('');
    expect(languageShareShort(undefined)).toBe('');
    expect(languageShareShort(share(0, 0, 0, 3))).toBe('');
  });
});

describe('cards of the job view', () => {
  it('keeps the title, the whole and the parts of a share apart', () => {
    const report = {
      total: share(840, 360),
      tracks: [
        { jobId: 'a', speaker: 'Student', share: share(120, 360) },
        { jobId: 'c', speaker: 'Guest', share: null },
      ],
    };
    expect(languageShareBlocks(report, t)).toEqual([
      {
        title: 'Speech of the dialogue',
        lead: '20.0 min',
        parts: ['Spanish 70 %', 'Russian 30 %'],
        ofParticipant: false,
      },
      {
        title: 'Student',
        lead: '40 % of the speech, 8.0 min',
        parts: ['Spanish 25 %', 'Russian 75 %'],
        ofParticipant: true,
      },
      {
        title: 'Guest',
        lead: 'no lines with time',
        parts: [],
        ofParticipant: true,
      },
    ]);
  });

  it('gives a participant of a talk a card without a whole', () => {
    const blocks = conversationBlocks(
      { conversation: talk([talker('Guest', { interruptions: 2 })]) },
      t
    );
    expect(blocks[0]).toEqual({
      title: 'Talk',
      lead: '39.9 min',
      parts: ['both at once 4.8 min', 'silence 6.4 min', 'speaker changes 399'],
      ofParticipant: false,
    });
    expect(blocks[1].title).toBe('Guest');
    expect(blocks[1].lead).toBeUndefined();
    expect(blocks[1].parts[0]).toBe('interruptions 2');
  });

  it('reads a card as the line the job view used to print', () => {
    expect(
      statBlockLine({
        title: 'Speech',
        lead: '1.0 min',
        parts: ['Spanish 100 %'],
        ofParticipant: false,
      })
    ).toBe('Speech 1.0 min: Spanish 100 %');
    expect(
      statBlockLine({
        title: 'Guest',
        lead: 'nothing',
        parts: [],
        ofParticipant: true,
      })
    ).toBe('Guest — nothing');
    expect(
      statBlockLine({ title: 'Guest', parts: ['a', 'b'], ofParticipant: true })
    ).toBe('Guest — a · b');
    expect(
      statBlockLine({ title: 'Talk', parts: [], ofParticipant: false })
    ).toBe('Talk');
  });

  it('gives no cards when nothing was counted', () => {
    expect(languageShareBlocks(null, t)).toEqual([]);
    expect(conversationBlocks({ total: share(60, 60) }, t)).toEqual([]);
  });
});
