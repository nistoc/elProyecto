/**
 * Settings of one job, chosen at upload. They go into the `settings` form field of POST /api/jobs;
 * the API writes them to job_settings.json in the job folder and the transcriber puts them on top of
 * its shared config. A field left at "as in the shared config" is not sent at all.
 */
export interface JobSettings {
  silence_compress_enabled?: boolean;
  silence_keep_sec?: number;
  model?: string;
}

/** What the upload form holds: '' means "as in the shared config". */
export interface JobSettingsForm {
  silenceCompress: '' | 'on' | 'off';
  silenceKeepSec: string;
  model: string;
}

export const emptyJobSettingsForm: JobSettingsForm = {
  silenceCompress: '',
  silenceKeepSec: '',
  model: '',
};

/** Limits of silence_keep_sec — the same the API and the transcriber check. */
export const SILENCE_KEEP_MIN_SEC = 0.05;
export const SILENCE_KEEP_MAX_SEC = 5;

/**
 * Models offered in the form: each gives lines with time marks, which the dialogue of tracks needs.
 * gpt-transcribe answers plain text; the transcriber asks it for a chunk whole and in pieces and
 * puts the times of the pieces on the text.
 */
export const JOB_MODEL_OPTIONS = [
  'gpt-4o-transcribe-diarize',
  'gpt-transcribe',
  'whisper-1',
] as const;

export type JobSettingsBuildResult =
  | { ok: true; settings: JobSettings | undefined }
  | { ok: false; errorKey: 'jobSettingsKeepInvalid' };

/** Turns the form into the settings to send; `undefined` when nothing differs from the shared config. */
export function buildJobSettings(
  form: JobSettingsForm
): JobSettingsBuildResult {
  const settings: JobSettings = {};

  if (form.silenceCompress === 'on') settings.silence_compress_enabled = true;
  if (form.silenceCompress === 'off') settings.silence_compress_enabled = false;

  const keepText = form.silenceKeepSec.trim().replace(',', '.');
  if (keepText !== '') {
    const keep = Number(keepText);
    if (
      !Number.isFinite(keep) ||
      keep < SILENCE_KEEP_MIN_SEC ||
      keep > SILENCE_KEEP_MAX_SEC
    ) {
      return { ok: false, errorKey: 'jobSettingsKeepInvalid' };
    }
    settings.silence_keep_sec = keep;
  }

  const model = form.model.trim();
  if (model !== '') settings.model = model;

  return {
    ok: true,
    settings: Object.keys(settings).length > 0 ? settings : undefined,
  };
}
