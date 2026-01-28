import React, { useState } from "react";
import type { TranslationKey } from "../i18n";
import type { JobSnapshot } from "../types";
import { startRefiner } from "../api";

type Props = {
  t: (k: TranslationKey) => string;
  jobId: string;
  job: JobSnapshot | null;
  links: { label: string; href: string }[];
};

export function ResultSection({ t, jobId, job, links }: Props) {
  const [startingRefiner, setStartingRefiner] = useState(false);

  const handleStartRefiner = async () => {
    if (!jobId) return;
    
    try {
      setStartingRefiner(true);
      await startRefiner(jobId);
    } catch (err) {
      console.error(err);
      alert(err instanceof Error ? err.message : "Failed to start refiner, see server logs.");
    } finally {
      setStartingRefiner(false);
    }
  };

  // Check if transcript exists (job has transcript in result or phase indicates transcription is done)
  const hasTranscript = job?.result?.transcript || 
                       job?.phase === "awaiting_refiner" || 
                       job?.phase === "completed" ||
                       job?.status === "done";

  return (
    <div className="card">
      <div className="card__header">
        <h3>{t("resultStep")}</h3>
      </div>
      <div className="result-grid">
        <div>
          <div className="meta-row">
            <span>{t("jobId")}</span>
            <code>{jobId || "—"}</code>
          </div>
          <div className="meta-row">
            <span>{t("status")}</span>
            <code>{job?.status || "—"}</code>
          </div>
          <div className="meta-row">
            <span>{t("phase")}</span>
            <code>{job?.phase || "—"}</code>
          </div>
        </div>
        <div className="result-links">
          {links.length === 0 && <div className="muted">{t("noLogs")}</div>}
          {links.map((link) => (
            <a
              key={link.href}
              className="chip"
              href={link.href}
              target="_blank"
              rel="noopener noreferrer"
            >
              {t("download")} {link.label}
            </a>
          ))}
        </div>
      </div>
      {hasTranscript && (
        <div className="result-actions" style={{ marginTop: "16px", paddingTop: "16px", borderTop: "1px solid #1f2937" }}>
          <button
            className="btn btn--primary"
            onClick={handleStartRefiner}
            disabled={startingRefiner || job?.phase === "refiner"}
            style={{ display: "flex", alignItems: "center", gap: "8px" }}
          >
            {startingRefiner && <div className="spinner spinner--sm"></div>}
            {startingRefiner 
              ? "Запуск рефайнера..." 
              : job?.phase === "refiner" 
              ? "Рефайнер выполняется..." 
              : "Запустить Refiner Agent"}
          </button>
          <p style={{ marginTop: "8px", fontSize: "12px", color: "#94a3b8" }}>
            Каждый запуск сохраняется в отдельный файл (transcript_fixed_1.md, transcript_fixed_2.md и т.д.)
          </p>
        </div>
      )}
    </div>
  );
}

