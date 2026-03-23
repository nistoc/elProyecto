interface UploadCardProps {
  file: File | null;
  onFileChange: (file: File | null) => void;
  onStart: (tags?: string[]) => void;
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
      {error && <p className="upload-card__error">{error}</p>}
      <button
        type="button"
        onClick={() => onStart()}
        disabled={disabled || !file}
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
