import { describe, expect, it } from 'vitest';
import {
  formatMinutes,
  languageShareLines,
  type LanguageShare,
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
