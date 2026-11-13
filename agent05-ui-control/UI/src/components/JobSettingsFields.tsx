import {
  JOB_MODEL_OPTIONS,
  SILENCE_KEEP_MAX_SEC,
  SILENCE_KEEP_MIN_SEC,
  buildJobSettings,
  type JobSettingsForm,
} from '../utils/jobSettings';

interface JobSettingsFieldsProps {
  form: JobSettingsForm;
  onChange: (patch: Partial<JobSettingsForm>) => void;
  t: (key: string) => string;
}

/**
 * Settings of one job (silence compression, pause kept, model). An empty field means
 * "as in the shared config" and is not sent. Used by the upload card and by a run started from a lesson.
 */
export function JobSettingsFields({
  form,
  onChange,
  t,
}: JobSettingsFieldsProps) {
  const built = buildJobSettings(form);

  return (
    <details className="job-settings">
      <summary>{t('jobSettingsTitle')}</summary>
      <p className="job-settings__hint">{t('jobSettingsHint')}</p>
      <label className="job-settings__field">
        <span>{t('jobSettingsCompress')}</span>
        <select
          value={form.silenceCompress}
          onChange={(e) =>
            onChange({
              silenceCompress: e.target
                .value as JobSettingsForm['silenceCompress'],
            })
          }
          data-testid="job-settings-compress"
        >
          <option value="">{t('jobSettingsAsShared')}</option>
          <option value="on">{t('jobSettingsCompressOn')}</option>
          <option value="off">{t('jobSettingsCompressOff')}</option>
        </select>
      </label>
      <label className="job-settings__field">
        <span>{t('jobSettingsKeep')}</span>
        <input
          type="text"
          inputMode="decimal"
          value={form.silenceKeepSec}
          placeholder={t('jobSettingsAsShared')}
          onChange={(e) => onChange({ silenceKeepSec: e.target.value })}
          aria-invalid={!built.ok}
          data-testid="job-settings-keep"
        />
      </label>
      <p className="job-settings__hint">
        {t('jobSettingsKeepHint')
          .replace('{min}', String(SILENCE_KEEP_MIN_SEC))
          .replace('{max}', String(SILENCE_KEEP_MAX_SEC))}
      </p>
      <label className="job-settings__field">
        <span>{t('jobSettingsModel')}</span>
        <select
          value={form.model}
          onChange={(e) => onChange({ model: e.target.value })}
          data-testid="job-settings-model"
        >
          <option value="">{t('jobSettingsAsShared')}</option>
          {JOB_MODEL_OPTIONS.map((m) => (
            <option key={m} value={m}>
              {m}
            </option>
          ))}
        </select>
      </label>
      <p className="job-settings__hint">{t('jobSettingsModelHint')}</p>
      {!built.ok && <p className="job-settings__error">{t(built.errorKey)}</p>}
      <style>{`
        .job-settings {
          margin: 0.75rem 0;
          padding: 0.5rem 0.75rem;
          border: 1px solid var(--color-border);
          border-radius: 6px;
        }
        .job-settings summary { cursor: pointer; font-weight: 500; }
        .job-settings__hint { color: var(--color-text-muted); font-size: 0.875rem; margin: 0.5rem 0; }
        .job-settings__error { color: var(--color-error); font-size: 0.875rem; margin: 0.5rem 0; }
        .job-settings__field {
          display: flex;
          flex-wrap: wrap;
          align-items: center;
          gap: 0.5rem;
          margin: 0.5rem 0 0;
        }
        .job-settings__field span { min-width: 14rem; }
        .job-settings__field input,
        .job-settings__field select {
          padding: 0.25rem 0.5rem;
          border: 1px solid var(--color-border);
          border-radius: 4px;
          background: var(--color-surface);
          color: var(--color-text);
          min-width: 14rem;
        }
        .job-settings__field input[aria-invalid="true"] { border-color: var(--color-error); }
      `}</style>
    </details>
  );
}
