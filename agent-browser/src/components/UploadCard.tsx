import React from "react";
import type { TranslationKey } from "../i18n";

type Props = {
  file: File | null;
  onFileChange: (f: File | null) => void;
  onStart: () => void;
  disabled?: boolean;
  t: (k: TranslationKey) => string;
};

export function UploadCard({
  file,
  onFileChange,
  onStart,
  disabled,
  t,
}: Props) {
  const handleInput = (e: React.ChangeEvent<HTMLInputElement>) => {
    const selected = e.target.files?.[0];
    onFileChange(selected ?? null);
  };

  return (
    <div className="card upload-card">
      <label className="file-drop">
        <input
          type="file"
          accept="audio/*"
          onChange={handleInput}
          disabled={disabled}
        />
        <div className="file-drop__body">
          <div className="file-drop__title">
            {file ? `${t("fileSelected")}: ${file.name}` : t("dropHint")}
          </div>
          <div className="file-drop__subtitle">{t("chooseFile")}</div>
        </div>
      </label>
      <div className="actions">
        <button
          className="primary"
          onClick={onStart}
          disabled={!file || disabled}
        >
          {t("startJob")}
        </button>
      </div>
    </div>
  );
}

