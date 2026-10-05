import { useCallback, useEffect, useState } from 'react';
import type { RefinerSettings } from '../types';
import type { useI18n } from '../contexts/I18nContext';
import { fetchRefinerSettings, saveRefinerSettings } from '../api';

type Translate = ReturnType<typeof useI18n>['t'];

interface Props {
  t: Translate;
  /** Lock editing while a refiner run is in progress (the run already took its settings). */
  disabled: boolean;
}

/**
 * Operator tuning for the refiner, shared by all jobs: the user prompt template
 * ({context} and {batch} placeholders), batch geometry and model. Saved to the
 * workspace root; the next Run Refiner uses it.
 */
export function RefinerSettingsPanel({ t, disabled }: Props) {
  const [saved, setSaved] = useState<RefinerSettings | null>(null);
  const [prompt, setPrompt] = useState('');
  const [systemPrompt, setSystemPrompt] = useState('');
  const [batchSize, setBatchSize] = useState(5);
  const [contextLines, setContextLines] = useState(2);
  const [model, setModel] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const apply = useCallback((s: RefinerSettings) => {
    setSaved(s);
    setPrompt(s.promptTemplate);
    setSystemPrompt(s.systemPrompt);
    setBatchSize(s.batchSize);
    setContextLines(s.contextLines);
    setModel(s.model);
  }, []);

  useEffect(() => {
    fetchRefinerSettings()
      .then(apply)
      .catch((e: Error) => setError(e.message));
  }, [apply]);

  const dirty =
    saved != null &&
    (prompt !== saved.promptTemplate ||
      systemPrompt !== saved.systemPrompt ||
      batchSize !== saved.batchSize ||
      contextLines !== saved.contextLines ||
      model !== saved.model);

  /** `null` in an override = reset that prompt to the built-in one. */
  const save = async (overrides?: { prompt?: null; system?: null }) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const next = await saveRefinerSettings({
        promptTemplate: overrides && 'prompt' in overrides ? null : prompt,
        batchSize,
        contextLines,
        model,
        systemPrompt: overrides && 'system' in overrides ? null : systemPrompt,
      });
      apply(next);
      setNotice(t('refinerSettingsSaved'));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const missingBatch = !prompt.includes('{batch}');

  return (
    <details className="refiner-settings" open>
      <summary className="refiner-settings__summary">
        <span>{t('refinerSettingsTitle')}</span>
        {saved && (
          <span
            className={
              saved.isCustomPrompt || saved.isCustomSystemPrompt
                ? 'refiner-settings__badge refiner-settings__badge--custom'
                : 'refiner-settings__badge'
            }
          >
            {saved.isCustomPrompt || saved.isCustomSystemPrompt
              ? t('refinerSettingsCustom')
              : t('refinerSettingsDefault')}
          </span>
        )}
      </summary>

      {error && <p className="refiner-settings__error">{error}</p>}
      {!saved && !error && (
        <p className="refiner-settings__hint">{t('loading')}</p>
      )}

      {saved && (
        <>
          <p className="refiner-settings__hint">{t('refinerSettingsHint')}</p>

          <div className="refiner-settings__label-row">
            <label className="refiner-settings__label" htmlFor="refiner-system-prompt">
              {t('refinerSettingsSystem')}
            </label>
            <button
              type="button"
              className="refiner-settings__link"
              disabled={
                disabled ||
                busy ||
                (!saved.isCustomSystemPrompt && systemPrompt === saved.defaultSystemPrompt)
              }
              onClick={() => save({ system: null })}
            >
              {t('refinerSettingsResetOne')}
            </button>
          </div>
          <textarea
            id="refiner-system-prompt"
            className="refiner-settings__prompt"
            value={systemPrompt}
            onChange={(e) => setSystemPrompt(e.target.value)}
            disabled={disabled || busy}
            spellCheck={false}
            rows={3}
          />

          <div className="refiner-settings__label-row">
          <label className="refiner-settings__label" htmlFor="refiner-prompt">
            {t('refinerSettingsPrompt')}
          </label>
            <button
              type="button"
              className="refiner-settings__link"
              disabled={
                disabled ||
                busy ||
                (!saved.isCustomPrompt && prompt === saved.defaultPromptTemplate)
              }
              onClick={() => save({ prompt: null })}
            >
              {t('refinerSettingsResetOne')}
            </button>
          </div>
          <textarea
            id="refiner-prompt"
            className="refiner-settings__prompt"
            value={prompt}
            onChange={(e) => setPrompt(e.target.value)}
            disabled={disabled || busy}
            spellCheck={false}
            rows={16}
          />
          {missingBatch && (
            <p className="refiner-settings__error">
              {t('refinerSettingsNeedBatch')}
            </p>
          )}

          <div className="refiner-settings__row">
            <label>
              {t('refinerSettingsBatchSize')}
              <input
                type="number"
                min={1}
                max={200}
                value={batchSize}
                onChange={(e) => setBatchSize(Number(e.target.value) || 1)}
                disabled={disabled || busy}
              />
            </label>
            <label>
              {t('refinerSettingsContextLines')}
              <input
                type="number"
                min={0}
                max={50}
                value={contextLines}
                onChange={(e) => setContextLines(Math.max(0, Number(e.target.value) || 0))}
                disabled={disabled || busy}
              />
            </label>
            <label>
              {t('refinerSettingsModel')}
              <input
                type="text"
                value={model}
                onChange={(e) => setModel(e.target.value)}
                disabled={disabled || busy}
              />
            </label>
          </div>

          <div className="refiner-settings__actions">
            <button
              type="button"
              className="refiner-actions__run"
              disabled={disabled || busy || !dirty || missingBatch}
              onClick={() => save()}
            >
              {t('refinerSettingsSave')}
            </button>
            {dirty && (
              <span className="refiner-settings__dirty">{t('refinerSettingsUnsaved')}</span>
            )}
            {notice && !dirty && (
              <span className="refiner-settings__notice">{notice}</span>
            )}
          </div>
        </>
      )}

      <style>{`
        .refiner-settings {
          margin: 0 0 1rem 0;
          padding: 0.75rem 1rem;
          border: 1px solid var(--color-border);
          border-radius: 8px;
          background: var(--color-subtle-panel, var(--color-surface));
        }
        .refiner-settings__summary {
          cursor: pointer;
          display: flex;
          align-items: center;
          gap: 0.6rem;
          font-size: 0.9rem;
          font-weight: 500;
          color: var(--color-heading);
        }
        .refiner-settings__badge {
          font-size: 0.72rem;
          font-weight: 400;
          padding: 0.1rem 0.45rem;
          border-radius: 999px;
          border: 1px solid var(--color-border);
          color: var(--color-text-secondary);
        }
        .refiner-settings__badge--custom {
          border-color: var(--color-accent, #2d7ff9);
          color: var(--color-accent, #2d7ff9);
        }
        .refiner-settings__hint { margin: 0.6rem 0; font-size: 0.8rem; color: var(--color-text-secondary); }
        .refiner-settings__label { display: block; font-size: 0.8rem; color: var(--color-text-secondary); }
        .refiner-settings__label-row { display: flex; align-items: baseline; gap: 0.75rem; margin: 0.7rem 0 0.3rem 0; }
        .refiner-settings__link {
          padding: 0;
          border: none;
          background: none;
          font-size: 0.75rem;
          color: var(--color-accent, #2d7ff9);
          cursor: pointer;
        }
        .refiner-settings__link:disabled { color: var(--color-text-muted); cursor: default; }
        .refiner-settings__prompt {
          width: 100%;
          box-sizing: border-box;
          padding: 0.55rem 0.65rem;
          border: 1px solid var(--color-border);
          border-radius: 6px;
          background: var(--color-surface);
          color: var(--color-text);
          font-family: var(--font-mono, monospace);
          font-size: 0.76rem;
          line-height: 1.45;
          resize: vertical;
        }
        .refiner-settings__row { display: flex; gap: 1rem; flex-wrap: wrap; margin: 0.7rem 0; }
        .refiner-settings__row label {
          display: flex;
          flex-direction: column;
          gap: 0.25rem;
          font-size: 0.78rem;
          color: var(--color-text-secondary);
        }
        .refiner-settings__row input {
          width: 9rem;
          padding: 0.3rem 0.45rem;
          border: 1px solid var(--color-border);
          border-radius: 5px;
          background: var(--color-surface);
          color: var(--color-text);
        }
        .refiner-settings__actions { display: flex; align-items: center; gap: 0.6rem; flex-wrap: wrap; }
        .refiner-settings__dirty { font-size: 0.78rem; color: var(--color-warning, #d08700); }
        .refiner-settings__notice { font-size: 0.78rem; color: var(--color-text-secondary); }
        .refiner-settings__error { margin: 0.4rem 0; font-size: 0.8rem; color: var(--color-danger, #d33); }
        .refiner-settings__system { margin-top: 0.8rem; font-size: 0.78rem; }
        .refiner-settings__system summary { cursor: pointer; color: var(--color-text-secondary); }
        .refiner-settings__system pre {
          margin: 0.4rem 0 0 0;
          padding: 0.5rem 0.65rem;
          border: 1px solid var(--color-border);
          border-radius: 6px;
          background: var(--color-surface);
          white-space: pre-wrap;
          font-size: 0.72rem;
          color: var(--color-text);
        }
      `}</style>
    </details>
  );
}
