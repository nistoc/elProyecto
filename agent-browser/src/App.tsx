import React, { useEffect, useMemo, useState } from "react";
import { createJob, fetchJob, subscribeToJob } from "./api";
import { I18nProvider, TranslationKey, useI18n } from "./i18n";
import { LogEntry, JobSnapshot } from "./types";
import { StepCard, StepStatus } from "./components/StepCard";
import { LogPanel } from "./components/LogPanel";

type StepId = "upload" | "transcriber" | "refiner" | "result";

const alias = {
  transcriber: "Transcriber Agent",
  refiner: "Refiner Agent",
};

function AppShell() {
  const { t, locale, setLocale } = useI18n();
  const [file, setFile] = useState<File | null>(null);
  const [jobId, setJobId] = useState<string | null>(null);
  const [job, setJob] = useState<JobSnapshot | null>(null);
  const [activeStep, setActiveStep] = useState<StepId>("upload");
  const [isSubmitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!jobId) return;
    let close = () => {};

    fetchJob(jobId)
      .then((snapshot) => setJob(snapshot))
      .catch(() => null)
      .finally(() => {
        close = subscribeToJob(jobId, (event) => {
          if (event.type === "snapshot") {
            setJob(event.payload);
            return;
          }
          if (event.type === "log") {
            setJob((prev) =>
              prev
                ? { ...prev, logs: [...prev.logs, event.payload] }
                : prev,
            );
            return;
          }
          if (event.type === "status") {
            setJob((prev) => (prev ? { ...prev, ...event.payload } : prev));
          }
          if (event.type === "done") {
            setActiveStep("result");
          }
        });
      });

    return () => close();
  }, [jobId]);

  const stepStatus = (step: StepId): StepStatus => {
    if (!job) return step === "upload" ? "waiting" : "waiting";
    if (job.status === "failed") return "failed";
    if (step === "upload") return jobId ? "done" : "waiting";
    if (step === "transcriber") {
      if (job.phase === "transcriber") return "running";
      if (job.phase === "refiner" || job.phase === "completed") return "done";
    }
    if (step === "refiner") {
      if (job.phase === "refiner") return "running";
      if (job.phase === "completed") return "done";
      if (job.phase === "transcriber") return "waiting";
    }
    if (step === "result") {
      return job.status === "done" ? "done" : "waiting";
    }
    return "waiting";
  };

  const logsByStep = useMemo(() => {
    const all = job?.logs || [];
    const filter = (needle: string) =>
      all.filter((log) => log.message.includes(needle));
    return {
      upload: [],
      transcriber: filter(alias.transcriber),
      refiner: filter(alias.refiner),
      result: all,
    } as Record<StepId, LogEntry[]>;
  }, [job]);

  const handleFileChange = (picked: File | null) => {
    setFile(picked);
    setActiveStep("upload");
  };

  const handleStart = async () => {
    if (!file) return;
    setSubmitting(true);
    try {
      const { jobId } = await createJob(file);
      setJobId(jobId);
      setActiveStep("transcriber");
    } catch (err) {
      console.error(err);
      alert("Failed to start job. Check server logs.");
    } finally {
      setSubmitting(false);
    }
  };

  const handleReset = () => {
    setFile(null);
    setJobId(null);
    setJob(null);
    setActiveStep("upload");
  };

  const steps: { id: StepId; title: string; desc: string; badge?: string }[] = [
    { id: "upload", title: t("uploadStep"), desc: t("uploadDesc") },
    {
      id: "transcriber",
      title: t("transcriberStep"),
      desc: alias.transcriber,
      badge: alias.transcriber,
    },
    {
      id: "refiner",
      title: t("refinerStep"),
      desc: alias.refiner,
      badge: alias.refiner,
    },
    { id: "result", title: t("resultStep"), desc: "" },
  ];

  const resultLinks = useMemo(() => {
    const base = import.meta.env.VITE_API_BASE || "http://localhost:3001";
    if (!job?.result) return [];
    const entries: { label: string; href: string }[] = [];
    if (job.result.transcript) {
      entries.push({
        label: "transcript.md",
        href: `${base}/${job.result.transcript}`,
      });
    }
    if (job.result.transcriptFixed) {
      entries.push({
        label: "transcript_fixed.md",
        href: `${base}/${job.result.transcriptFixed}`,
      });
    }
    if (job.result.rawJson) {
      entries.push({
        label: "response.json",
        href: `${base}/${job.result.rawJson}`,
      });
    }
    return entries;
  }, [job]);

  return (
    <div className="page">
      <header className="topbar">
        <div>
          <div className="eyebrow">agents</div>
          <h1>{t("appTitle")}</h1>
        </div>
        <div className="topbar__actions">
          <label className="language">
            {t("language")}
            <select
              value={locale}
              onChange={(e) => setLocale(e.target.value as typeof locale)}
            >
              <option value="en">EN</option>
              <option value="ru">RU</option>
              <option value="es">ES</option>
            </select>
          </label>
          <button className="ghost" onClick={handleReset}>
            {t("clear")}
          </button>
        </div>
      </header>

      <main className="layout">
        <section className="steps">
          {steps.map((step) => (
            <StepCard
              key={step.id}
              title={step.title}
              description={step.desc}
              status={stepStatus(step.id)}
              active={activeStep === step.id}
              badge={step.badge}
              onSelect={() => setActiveStep(step.id)}
            />
          ))}
        </section>

        <section className="content">
          {activeStep === "upload" && (
            <UploadCard
              file={file}
              onFileChange={handleFileChange}
              onStart={handleStart}
              disabled={isSubmitting}
              t={t}
            />
          )}

          {activeStep === "transcriber" && (
            <LogsSection
              title={alias.transcriber}
              logs={logsByStep.transcriber}
              emptyLabel={t("noLogs")}
            />
          )}
          {activeStep === "refiner" && (
            <LogsSection
              title={alias.refiner}
              logs={logsByStep.refiner}
              emptyLabel={t("noLogs")}
            />
          )}
          {activeStep === "result" && (
            <ResultSection
              t={t}
              jobId={jobId || ""}
              job={job}
              links={resultLinks}
            />
          )}
        </section>
      </main>
    </div>
  );
}

function UploadCard({
  file,
  onFileChange,
  onStart,
  disabled,
  t,
}: {
  file: File | null;
  onFileChange: (f: File | null) => void;
  onStart: () => void;
  disabled?: boolean;
  t: (k: TranslationKey) => string;
}) {
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

function LogsSection({
  title,
  logs,
  emptyLabel,
}: {
  title: string;
  logs: LogEntry[];
  emptyLabel: string;
}) {
  const lastLine = logs.length ? logs[logs.length - 1].message : "";
  return (
    <div className="card log-card">
      <div className="card__header">
        <h3>{title}</h3>
      </div>
      <LogPanel logs={logs} emptyLabel={emptyLabel} />
      <div className="log-latest">
        <span className="log-latest__label">Latest:</span>
        <span className="log-latest__text">
          {lastLine || "—"}
        </span>
      </div>
    </div>
  );
}

function ResultSection({
  t,
  jobId,
  job,
  links,
}: {
  t: (k: TranslationKey) => string;
  jobId: string;
  job: JobSnapshot | null;
  links: { label: string; href: string }[];
}) {
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
            <a key={link.href} className="chip" href={link.href} target="_blank">
              {t("download")} {link.label}
            </a>
          ))}
        </div>
      </div>
    </div>
  );
}

export default function App() {
  return (
    <I18nProvider>
      <AppShell />
    </I18nProvider>
  );
}

