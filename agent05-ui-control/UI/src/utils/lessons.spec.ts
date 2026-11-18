import { describe, expect, it } from 'vitest';
import {
  canBuildDialogue,
  defaultDialogueTitle,
  dialogueCandidates,
  estimateRunUsd,
  formatBytes,
  formatDuration,
  formatUsd,
  indexLessonJobs,
  periodDurationSec,
  periodTimeLabel,
  runUsdPerMinute,
  sessionCounts,
  sessionDurationSec,
  trackDefaultModel,
  type LessonPeriod,
  type LessonRun,
  type LessonSession,
  type LessonTrack,
  type RunPricing,
} from './lessons';

// All names here are made up.
function run(jobId: string, hasTranscript = true): LessonRun {
  return {
    jobId,
    status: 'completed',
    phase: 'idle',
    createdAt: `2026-10-24T10:00:0${jobId.length % 10}Z`,
    hasTranscript,
    hasRefined: false,
  };
}

function track(
  kind: string,
  participant: string | null,
  durationSec: number | null,
  runs: LessonRun[] = []
): LessonTrack {
  return {
    id: `day/period/${participant ?? 'mixed'}.m4a`,
    kind,
    participant,
    fileName: `${participant ?? 'mixed'}.m4a`,
    sizeBytes: 100,
    durationSec,
    runs,
  };
}

function period(patch: Partial<LessonPeriod> = {}): LessonPeriod {
  return {
    id: 'day/period',
    startedAt: '2026-10-23T20:21:45',
    title: 'Maria Lopez’s Zoom Meeting',
    state: 'ready',
    hasVideo: true,
    hasChat: false,
    hasSubtitles: false,
    hasOldTranscript: false,
    rawZoomBytes: 0,
    tracks: [],
    dialogues: [],
    ...patch,
  };
}

function session(periods: LessonPeriod[]): LessonSession {
  return { id: '2026.10.23', date: '2026-10-23', loose: false, periods };
}

describe('formatDuration', () => {
  it('prints minutes and seconds, hours when there are any', () => {
    expect(formatDuration(2404)).toBe('40:04');
    expect(formatDuration(6372)).toBe('1:46:12');
    expect(formatDuration(6.3)).toBe('0:06');
    expect(formatDuration(59.6)).toBe('1:00');
    expect(formatDuration(0)).toBe('0:00');
  });

  it('prints a dash when the length is unknown', () => {
    expect(formatDuration(null)).toBe('—');
    expect(formatDuration(undefined)).toBe('—');
    expect(formatDuration(Number.NaN)).toBe('—');
    expect(formatDuration(-1)).toBe('—');
  });
});

describe('formatBytes', () => {
  it('keeps one decimal for gigabytes and none below', () => {
    expect(formatBytes(1_275_000_000)).toBe('1.3 GB');
    expect(formatBytes(24_893_245)).toBe('25 MB');
    expect(formatBytes(1_500)).toBe('2 KB');
    expect(formatBytes(10)).toBe('1 KB');
    expect(formatBytes(0)).toBe('0');
  });
});

describe('estimateRunUsd', () => {
  it('is minutes times the price, rounded up to a cent', () => {
    expect(estimateRunUsd(2404, 0.017)).toBe(0.69);
    expect(estimateRunUsd(1251.5, 0.017)).toBe(0.36);
    expect(estimateRunUsd(60, 0.02)).toBe(0.02);
    expect(estimateRunUsd(6, 0.017)).toBe(0.01);
  });

  it('is unknown without a length or a price', () => {
    expect(estimateRunUsd(null, 0.017)).toBeNull();
    expect(estimateRunUsd(0, 0.017)).toBeNull();
    expect(estimateRunUsd(100, 0)).toBeNull();
    expect(formatUsd(null)).toBe('—');
    expect(formatUsd(0.69)).toBe('$0.69');
    expect(formatUsd(1.2)).toBe('$1.20');
  });
});

describe('model and price of a run', () => {
  const pricing: RunPricing = {
    estimateUsdPerMinute: 0.017,
    participantTrackModel: 'gpt-transcribe',
    participantTrackEstimateUsdPerMinute: 0.01,
  };
  const participant = track('participant', 'Maria Lopez', 600);
  const mixed = track('mixed', null, 600);

  it('gives a participant track the model of participant tracks, the mixed track none', () => {
    expect(trackDefaultModel(participant, pricing)).toBe('gpt-transcribe');
    expect(trackDefaultModel(mixed, pricing)).toBeNull();
    expect(
      trackDefaultModel(participant, {
        ...pricing,
        participantTrackModel: null,
      })
    ).toBeNull();
    expect(trackDefaultModel(participant, null)).toBeNull();
  });

  it('counts by the price of the model the run will go to', () => {
    // No model chosen: the track decides.
    expect(runUsdPerMinute(participant, '', pricing)).toBe(0.01);
    expect(runUsdPerMinute(mixed, '', pricing)).toBe(0.017);
    // The operator chose a model: it decides for any track.
    expect(
      runUsdPerMinute(participant, 'gpt-4o-transcribe-diarize', pricing)
    ).toBe(0.017);
    expect(runUsdPerMinute(mixed, 'gpt-transcribe', pricing)).toBe(0.01);
    expect(runUsdPerMinute(participant, 'whisper-1', pricing)).toBe(0.017);
  });

  it('falls back to the common price when participant tracks have no model or price of their own', () => {
    expect(
      runUsdPerMinute(participant, '', {
        ...pricing,
        participantTrackModel: null,
      })
    ).toBe(0.017);
    expect(
      runUsdPerMinute(participant, '', {
        ...pricing,
        participantTrackEstimateUsdPerMinute: null,
      })
    ).toBe(0.017);
    expect(
      runUsdPerMinute(participant, '', { estimateUsdPerMinute: 0.02 })
    ).toBe(0.02);
    expect(runUsdPerMinute(participant, '', null)).toBe(0);
  });
});

describe('period and session length', () => {
  it('takes the mixed track, else the longest one', () => {
    const withMixed = period({
      tracks: [
        track('participant', 'Maria Lopez', 2500),
        track('mixed', null, 2404),
      ],
    });
    const withoutMixed = period({
      tracks: [
        track('participant', 'Maria Lopez', 6.3),
        track('participant', 'Olga Iva', 7.1),
      ],
    });
    const unknown = period({ tracks: [track('mixed', null, null)] });

    expect(periodDurationSec(withMixed)).toBe(2404);
    expect(periodDurationSec(withoutMixed)).toBe(7.1);
    expect(periodDurationSec(unknown)).toBeNull();
    expect(periodDurationSec(period())).toBeNull();
    expect(
      sessionDurationSec(session([withMixed, withoutMixed, unknown]))
    ).toBeCloseTo(2411.1, 6);
    expect(sessionDurationSec(session([unknown]))).toBeNull();
  });
});

describe('periodTimeLabel', () => {
  it('is the time of the start, or the title when the name has no time', () => {
    expect(periodTimeLabel(period())).toBe('20:21');
    expect(
      periodTimeLabel(period({ startedAt: null, title: 'Guest.m4a' }))
    ).toBe('Guest.m4a');
    expect(periodTimeLabel(period({ startedAt: null, title: null }))).toBe(
      'day/period'
    );
  });
});

describe('sessionCounts', () => {
  it('counts periods, tracks with runs, distinct runs and dialogues', () => {
    const first = period({
      tracks: [
        track('mixed', null, 100),
        track('participant', 'Maria Lopez', 100, [run('a'), run('bb')]),
        track('participant', 'Olga Iva', 100, [run('ccc')]),
      ],
      dialogues: [
        { jobId: 'd1', trackJobIds: ['a', 'ccc'], merges: 1 },
        { jobId: 'd2', trackJobIds: ['bb', 'ccc'], merges: 2 },
      ],
    });
    const raw = period({ id: 'day/raw', state: 'unconverted' });

    expect(sessionCounts(session([first, raw]))).toEqual({
      periods: 2,
      unconverted: 1,
      tracks: 3,
      tracksWithRuns: 2,
      runs: 3,
      dialogues: 2,
    });
  });
});

describe('dialogue of a period', () => {
  it('offers participant tracks that have a transcript, newest run first in line', () => {
    const p = period({
      tracks: [
        track('mixed', null, 100, [run('mixedjob')]),
        track('participant', 'Maria Lopez', 100, [
          run('old'),
          run('newer'),
          run('unfinished', false),
        ]),
        track('participant', 'Olga Iva', 100, [run('only')]),
        track('participant', 'Guest', 100, [run('nothing', false)]),
        track('participant', null, 100),
      ],
    });

    const candidates = dialogueCandidates(p);

    expect(candidates.map((c) => [c.speaker, c.jobId])).toEqual([
      ['Maria Lopez', 'newer'],
      ['Olga Iva', 'only'],
    ]);
    expect(candidates[0].runs.map((r) => r.jobId)).toEqual(['old', 'newer']);
    expect(canBuildDialogue(p)).toBe(true);
  });

  it('needs two participant tracks with a transcript', () => {
    const oneTrack = period({
      tracks: [
        track('mixed', null, 100, [run('mixedjob')]),
        track('participant', 'Maria Lopez', 100, [run('a')]),
        track('participant', 'Olga Iva', 100),
      ],
    });

    expect(canBuildDialogue(oneTrack)).toBe(false);
    expect(canBuildDialogue(period())).toBe(false);
  });

  it('is titled by the date and the time of the period', () => {
    const s = session([period()]);

    expect(defaultDialogueTitle(s, s.periods[0])).toBe('2026-10-23 20:21');
    expect(defaultDialogueTitle(s, period({ startedAt: null }))).toBe(
      '2026-10-23'
    );
  });
});

describe('indexLessonJobs', () => {
  it('ties every run and dialogue to its lesson and recording', () => {
    const p = period({
      tracks: [
        track('mixed', null, 2400, [run('aaa')]),
        track('participant', 'Maria Lopez', 2400, [run('bbb'), run('cc')]),
      ],
      dialogues: [
        {
          jobId: 'ddd',
          title: 'Evening talk',
          trackJobIds: ['bbb'],
          merges: 1,
        },
      ],
    });
    const index = indexLessonJobs({
      enabled: true,
      estimateUsdPerMinute: 0.006,
      sessions: [session([p])],
    });
    expect([...index.keys()]).toEqual(['aaa', 'bbb', 'cc', 'ddd']);
    expect(index.get('aaa')).toMatchObject({
      date: '2026-10-23',
      time: '20:21',
      kind: 'mixed',
      label: '',
      hasTranscript: true,
      hasRefined: false,
    });
    expect(index.get('cc')).toMatchObject({
      kind: 'participant',
      label: 'Maria Lopez',
    });
    expect(index.get('ddd')).toEqual({
      date: '2026-10-23',
      time: '20:21',
      kind: 'dialogue',
      label: 'Evening talk',
    });
  });

  it('is empty when there are no lessons to read', () => {
    expect(indexLessonJobs(null).size).toBe(0);
    expect(
      indexLessonJobs({ enabled: false, estimateUsdPerMinute: 0, sessions: [] })
        .size
    ).toBe(0);
  });
});
