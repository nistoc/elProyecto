import React from "react";
import type { TranslationKey } from "../i18n";
import type { JobSnapshot } from "../types";

type Props = {
  t: (k: TranslationKey) => string;
  jobId: string;
  job: JobSnapshot | null;
  links: { label: string; href: string }[];
};

export function ResultSection({ t, jobId, job, links }: Props) {
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
    </div>
  );
}

