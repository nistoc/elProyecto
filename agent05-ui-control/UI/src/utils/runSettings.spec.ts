import { describe, expect, it } from 'vitest';
import {
  formatPauseSec,
  runSettingsLine,
  runSettingsShort,
  type JobRunSettings,
} from './runSettings';

const words: Record<string, string> = {
  runSettingsTitle: 'Run settings',
  runSettingsTitleArtifacts: 'Run settings (from the folder)',
  runSettingsTitlePlanned: 'Set for this job (not run yet)',
  runSettingsCompressOn: 'pauses cut',
  runSettingsCompressOff: 'pauses not cut',
  runSettingsPause: 'pause',
  runSettingsSecUnit: 's',
  runSettingsDecimal: '.',
  runSettingsModel: 'model',
  runSettingsModelUnknown: 'model not recorded',
  runSettingsSetForJob: 'set for this job',
  runSettingsAllShared: 'all from the shared config',
  runSettingsRestShared: 'the rest from the shared config',
  runSettingsKeyCompress: 'compression',
  runSettingsKeyPause: 'pause',
  runSettingsKeyModel: 'model',
  runSettingsShortNoCompress: 'no compression',
};
const t = (key: string) => words[key] ?? key;
const tComma = (key: string) => (key === 'runSettingsDecimal' ? ',' : t(key));

function settings(over: Partial<JobRunSettings>): JobRunSettings {
  return {
    source: 'run',
    model: 'gpt-4o-transcribe-diarize',
    silenceCompress: true,
    silenceKeepSec: 1,
    fromJobSettings: [],
    ...over,
  };
}

describe('formatPauseSec', () => {
  it('keeps one decimal for whole and tenth values and two for finer ones', () => {
    expect(formatPauseSec(1)).toBe('1.0');
    expect(formatPauseSec(0.2)).toBe('0.2');
    expect(formatPauseSec(0.25)).toBe('0.25');
    expect(formatPauseSec(2.5)).toBe('2.5');
    expect(formatPauseSec(0.30000000000000004)).toBe('0.3');
  });

  it('uses the decimal sign of the locale', () => {
    expect(formatPauseSec(1, ',')).toBe('1,0');
    expect(formatPauseSec(0.25, ',')).toBe('0,25');
  });
});

describe('runSettingsLine', () => {
  it('shows nothing when the folder tells nothing', () => {
    expect(runSettingsLine(null, t)).toBeNull();
    expect(runSettingsLine(undefined, t)).toBeNull();
  });

  it('shows the record of the transcriber with everything from the shared config', () => {
    expect(runSettingsLine(settings({}), t)).toBe(
      'Run settings: pauses cut · pause 1.0 s · model gpt-4o-transcribe-diarize · all from the shared config'
    );
  });

  it('names what the operator set for the job', () => {
    expect(
      runSettingsLine(
        settings({
          model: 'whisper-1',
          silenceKeepSec: 0.2,
          fromJobSettings: ['model', 'silence_keep_sec', 'something_else'],
        }),
        tComma
      )
    ).toBe(
      'Run settings: pauses cut · pause 0,2 s · model whisper-1 · set for this job: model, pause'
    );
  });

  it('shows no pause for a run without compression', () => {
    expect(
      runSettingsLine(
        settings({
          silenceCompress: false,
          silenceKeepSec: null,
          fromJobSettings: ['silence_compress_enabled'],
        }),
        t
      )
    ).toBe(
      'Run settings: pauses not cut · model gpt-4o-transcribe-diarize · set for this job: compression'
    );
  });

  it('shows no pause without compression even when a pause value came along', () => {
    expect(
      runSettingsLine(
        settings({ silenceCompress: false, silenceKeepSec: 0.2 }),
        t
      )
    ).toBe(
      'Run settings: pauses not cut · model gpt-4o-transcribe-diarize · all from the shared config'
    );
  });

  it('says where the values of an old job come from and that its model was not recorded', () => {
    expect(
      runSettingsLine(
        settings({ source: 'artifacts', model: null, silenceKeepSec: 0.2 }),
        t
      )
    ).toBe(
      'Run settings (from the folder): pauses cut · pause 0.2 s · model not recorded'
    );
  });

  it('shows only what the operator set while the job has not run', () => {
    expect(
      runSettingsLine(
        {
          source: 'job_settings',
          model: null,
          silenceCompress: null,
          silenceKeepSec: 2.5,
          fromJobSettings: ['silence_keep_sec'],
        },
        t
      )
    ).toBe(
      'Set for this job (not run yet): pause 2.5 s · the rest from the shared config'
    );
  });
});

describe('runSettingsShort', () => {
  it('is empty without settings', () => {
    expect(runSettingsShort(null, t)).toBe('');
  });

  it('gives the pause and the model', () => {
    expect(runSettingsShort(settings({ model: 'whisper-1' }), tComma)).toBe(
      'pause 1,0 s · whisper-1'
    );
  });

  it('gives the pause alone when the model was not recorded', () => {
    expect(
      runSettingsShort(settings({ source: 'artifacts', model: null }), t)
    ).toBe('pause 1.0 s');
  });

  it('says that pauses were not cut', () => {
    expect(
      runSettingsShort(
        settings({ silenceCompress: false, silenceKeepSec: null, model: null }),
        t
      )
    ).toBe('no compression');
  });
});
