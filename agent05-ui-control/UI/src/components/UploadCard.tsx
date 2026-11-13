import { useState } from 'react';
import {
  JOB_MODEL_OPTIONS,
  SILENCE_KEEP_MAX_SEC,
  SILENCE_KEEP_MIN_SEC,
  buildJobSettings,
  emptyJobSettingsForm,
  type JobSettings,
  type JobSettingsForm,
} from '../utils/jobSettings';

interface UploadCardProps {
  file: File | null;
  onFileChange: (file: File | null) => void;
  onStart: (tags?: string[], settings?: JobSettings) => void;
  disabled: boolean;
  t: (key: string) => string;
  error?: string | null;
}

export function UploadCard({
  file,
  onFileChange,
  onStart,
  disabled,
  t,
  error,
}: UploadCardProps) {
  // Settings of this one job; an empty field means "as in the shared config" and is not sent.
  const [settingsForm, setSettingsForm] =
    useState<JobSettingsForm>(emptyJobSettingsForm);
  const built = buildJobSettings(settingsForm);
  const patch = (p: Partial<JobSettingsForm>) =>
    setSettingsForm((prev) => ({ ...prev, ...p }));

  return (
    <div className="upload-card">
      <label className="upload-card__label">
        <span>{t('selectFile')}</span>
        <input
          type="file"
          accept="audio/*"
          onChange={(e) => onFileChange(e.target.files?.[0] ?? null)}
          className="upload-card__input"
        />
      </label>
      {file && <p className="upload-card__filename">{file.name}</p>}
      {!file && <p className="upload-card__hint">{t('noFileSelected')}</p>}

      <details className="upload-card__settings">
        <summary>{t('jobSettingsTitle')}</summary>
        <p className="upload-card__hint">{t('jobSettingsHint')}</p>
        <label className="upload-card__field">
          <span>{t('jobSettingsCompress')}</span>
          <select
            value={settingsForm.silenceCompress}
            onChange={(e) =>
              patch({
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
        <label className="upload-card__field">
          <span>{t('jobSettingsKeep')}</span>
          <input
            type="text"
            inputMode="decimal"
            value={settingsForm.silenceKeepSec}
            placeholder={t('jobSettingsAsShared')}
            onChange={(e) => patch({ silenceKeepSec: e.target.value })}
            aria-invalid={!built.ok}
            data-testid="job-settings-keep"
          />
        </label>
        <p className="upload-card__hint">
          {t('jobSettingsKeepHint')
            .replace('{min}', String(SILENCE_KEEP_MIN_SEC))
            .replace('{max}', String(SILENCE_KEEP_MAX_SEC))}
        </p>
        <label className="upload-card__field">
          <span>{t('jobSettingsModel')}</span>
          <select
            value={settingsForm.model}
            onChange={(e) => patch({ model: e.target.value })}
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
        <p className="upload-card__hint">{t('jobSettingsModelHint')}</p>
        {!built.ok && <p className="upload-card__error">{t(built.errorKey)}</p>}
      </details>

      {error && <p className="upload-card__error">{error}</p>}
      <button
        type="button"
        onClick={() => built.ok && onStart(undefined, built.settings)}
        disabled={disabled || !file || !built.ok}
        className="upload-card__btn"
      >
        {t('start')}
      </button>
      <style>{`
        .upload-card {
          padding: 1.5rem;
          border: 1px solid var(--color-border);
          border-radius: 8px;
          background: var(--color-surface);
          color: var(--color-text);
        }
        .upload-card__label { display: block; margin-bottom: 0.5rem; }
        .upload-card__input { margin-left: 0.5rem; }
        .upload-card__filename { font-weight: 500; margin: 0.5rem 0; }
        .upload-card__hint { color: var(--color-text-muted); font-size: 0.875rem; margin: 0.5rem 0; }
        .upload-card__error { color: var(--color-error); font-size: 0.875rem; margin: 0.5rem 0; }
        .upload-card__settings {
          margin: 0.75rem 0;
          padding: 0.5rem 0.75rem;
          border: 1px solid var(--color-border);
          border-radius: 6px;
        }
        .upload-card__settings summary { cursor: pointer; font-weight: 500; }
        .upload-card__field {
          display: flex;
          flex-wrap: wrap;
          align-items: center;
          gap: 0.5rem;
          margin: 0.5rem 0 0;
        }
        .upload-card__field span { min-width: 14rem; }
        .upload-card__field input,
        .upload-card__field select {
          padding: 0.25rem 0.5rem;
          border: 1px solid var(--color-border);
          border-radius: 4px;
          background: var(--color-surface);
          color: var(--color-text);
          min-width: 14rem;
        }
        .upload-card__field input[aria-invalid="true"] { border-color: var(--color-error); }
        .upload-card__btn {
          margin-top: 1rem;
          padding: 0.5rem 1rem;
          background: var(--color-primary);
          color: var(--color-on-primary);
          border: none;
          border-radius: 6px;
          cursor: pointer;
        }
        .upload-card__btn:hover:not(:disabled) { background: var(--color-primary-hover); }
        .upload-card__btn:disabled { opacity: 0.5; cursor: not-allowed; }
      `}</style>
    </div>
  );
}
