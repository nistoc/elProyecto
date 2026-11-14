/**
 * What a job was transcribed with, as the API reads it from the job folder
 * (`runSettings` of a job snapshot, `settings` of a lesson run). A value the folder does not tell is null.
 */
export interface JobRunSettings {
  /**
   * run — the transcriber's own record, exact; artifacts — read from what the run left in the folder
   * (a job from before the record existed); job_settings — only the operator's settings, not run yet.
   */
  source: string;
  model?: string | null;
  silenceCompress?: boolean | null;
  /** How much of a pause was kept; set only when pauses were cut out. */
  silenceKeepSec?: number | null;
  /** Keys the operator set for this job: silence_compress_enabled, silence_keep_sec, model. */
  fromJobSettings?: string[] | null;
}

type T = (key: string) => string;

const KEY_LABELS: Record<string, string> = {
  silence_compress_enabled: 'runSettingsKeyCompress',
  silence_keep_sec: 'runSettingsKeyPause',
  model: 'runSettingsKeyModel',
};

/** 1 → "1.0", 0.2 → "0.2", 0.25 → "0.25"; the decimal sign is the locale's. */
export function formatPauseSec(seconds: number, decimal = '.'): string {
  const rounded = (Math.round(seconds * 100) / 100).toString();
  const text = rounded.includes('.') ? rounded : `${rounded}.0`;
  return text.replace('.', decimal);
}

function pauseText(seconds: number, t: T): string {
  return `${t('runSettingsPause')} ${formatPauseSec(seconds, t('runSettingsDecimal'))} ${t('runSettingsSecUnit')}`;
}

/**
 * The line of the job view, for example
 * "Run settings: pauses cut · pause 1.0 s · model whisper-1 · set for this job: pause, model".
 * Null when there is nothing to show.
 */
export function runSettingsLine(
  settings: JobRunSettings | null | undefined,
  t: T
): string | null {
  if (!settings) return null;
  const planned = settings.source === 'job_settings';
  const parts: string[] = [];

  if (settings.silenceCompress === true) parts.push(t('runSettingsCompressOn'));
  if (settings.silenceCompress === false)
    parts.push(t('runSettingsCompressOff'));
  if (settings.silenceCompress !== false && settings.silenceKeepSec != null)
    parts.push(pauseText(settings.silenceKeepSec, t));

  if (settings.model) parts.push(`${t('runSettingsModel')} ${settings.model}`);
  else if (!planned) parts.push(t('runSettingsModelUnknown'));

  const keys = (settings.fromJobSettings ?? []).filter((k) => KEY_LABELS[k]);
  if (planned) {
    // Everything shown is what the operator set; the rest comes from the shared config at start.
    parts.push(t('runSettingsRestShared'));
  } else if (keys.length > 0) {
    parts.push(
      `${t('runSettingsSetForJob')}: ${keys.map((k) => t(KEY_LABELS[k])).join(', ')}`
    );
  } else if (settings.source === 'run') {
    parts.push(t('runSettingsAllShared'));
  }

  const title =
    settings.source === 'run'
      ? t('runSettingsTitle')
      : planned
        ? t('runSettingsTitlePlanned')
        : t('runSettingsTitleArtifacts');
  return `${title}: ${parts.join(' · ')}`;
}

/**
 * The short form next to a run in a list, for example "pause 1.0 s · whisper-1" or "no compression".
 * Empty when the folder tells nothing.
 */
export function runSettingsShort(
  settings: JobRunSettings | null | undefined,
  t: T
): string {
  if (!settings) return '';
  const parts: string[] = [];
  if (settings.silenceCompress === false)
    parts.push(t('runSettingsShortNoCompress'));
  else if (settings.silenceKeepSec != null)
    parts.push(pauseText(settings.silenceKeepSec, t));
  if (settings.model) parts.push(settings.model);
  return parts.join(' · ');
}
