import { describe, expect, it } from 'vitest';
import { buildJobSettings, emptyJobSettingsForm } from './jobSettings';

describe('buildJobSettings', () => {
  it('sends nothing when every field is left as in the shared config', () => {
    expect(buildJobSettings(emptyJobSettingsForm)).toEqual({
      ok: true,
      settings: undefined,
    });
  });

  it('sends only the fields the operator changed', () => {
    expect(
      buildJobSettings({
        silenceCompress: '',
        silenceKeepSec: '1.0',
        model: '',
      })
    ).toEqual({ ok: true, settings: { silence_keep_sec: 1 } });

    expect(
      buildJobSettings({
        silenceCompress: 'off',
        silenceKeepSec: '',
        model: ' whisper-1 ',
      })
    ).toEqual({
      ok: true,
      settings: { silence_compress_enabled: false, model: 'whisper-1' },
    });

    expect(
      buildJobSettings({ silenceCompress: 'on', silenceKeepSec: '', model: '' })
    ).toEqual({ ok: true, settings: { silence_compress_enabled: true } });
  });

  it('reads a pause typed with a comma', () => {
    expect(
      buildJobSettings({
        silenceCompress: '',
        silenceKeepSec: '0,5',
        model: '',
      })
    ).toEqual({ ok: true, settings: { silence_keep_sec: 0.5 } });
  });

  it.each(['0', '0.01', '5.5', '-1', 'abc', '1 s'])(
    'refuses the pause %s (outside 0.05–5 s or not a number)',
    (value) => {
      expect(
        buildJobSettings({
          silenceCompress: '',
          silenceKeepSec: value,
          model: '',
        })
      ).toEqual({ ok: false, errorKey: 'jobSettingsKeepInvalid' });
    }
  );
});
