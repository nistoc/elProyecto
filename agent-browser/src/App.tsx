import React from "react";
import { I18nProvider, useI18n } from "./i18n";
import { useJob } from "./hooks";
import type { StepId } from "./hooks";
import {
  StepCard,
  UploadCard,
  ChunkControlPanel,
  LogsSection,
  ResultSection,
} from "./components";

function AppShell() {
  const { t, locale, setLocale } = useI18n();
  const {
    file,
    jobId,
    job,
    activeStep,
    isSubmitting,
    logsPaused,
    bufferedCount,
    getStepStatus,
    logsByStep,
    resultLinks,
    aliases,
    setActiveStep,
    handleFileChange,
    handleStart,
    handleReset,
    handleCancelChunk,
    toggleLogsPause,
  } = useJob();

  const steps: { id: StepId; title: string; desc: string; badge?: string }[] = [
    { id: "upload", title: t("uploadStep"), desc: t("uploadDesc") },
    {
      id: "transcriber",
      title: t("transcriberStep"),
      desc: aliases.transcriber,
      badge: aliases.transcriber,
    },
    {
      id: "refiner",
      title: t("refinerStep"),
      desc: aliases.refiner,
      badge: aliases.refiner,
    },
    { id: "result", title: t("resultStep"), desc: "" },
  ];

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
              status={getStepStatus(step.id)}
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
            <>
              <ChunkControlPanel
                state={job?.chunks}
                onCancel={handleCancelChunk}
                disabled={!jobId}
              />
              <LogsSection
                title={aliases.transcriber}
                logs={logsByStep.transcriber}
                emptyLabel={t("noLogs")}
                paused={logsPaused}
                bufferedCount={bufferedCount}
                onTogglePause={toggleLogsPause}
              />
            </>
          )}

          {activeStep === "refiner" && (
            <LogsSection
              title={aliases.refiner}
              logs={logsByStep.refiner}
              emptyLabel={t("noLogs")}
              paused={logsPaused}
              bufferedCount={bufferedCount}
              onTogglePause={toggleLogsPause}
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

export default function App() {
  return (
    <I18nProvider>
      <AppShell />
    </I18nProvider>
  );
}
