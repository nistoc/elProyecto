import React, { useState, useMemo } from "react";
import { I18nProvider, useI18n } from "./i18n";
import { useJob } from "./hooks";
import type { StepId } from "./hooks";
import {
  StepCard,
  UploadCard,
  ChunkControlPanel,
  LogsSection,
  ResultSection,
  JobsList,
  ProjectFiles,
  ResultFiles,
  AudioPlayer,
  RefiningTextPreview,
} from "./components";
import { AudioPlayerProvider } from "./contexts/AudioPlayerContext";

function AppShell() {
  const { t, locale, setLocale } = useI18n();
  const [selectedChunkIndex, setSelectedChunkIndex] = useState<number | null>(null);
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
    handleSplitChunk,
    handleCancelSubChunk,
    handleRetranscribeSubChunk,
    handleSkipChunk,
    handleStartRefiner,
    handleSkipRefiner,
    handleRebuildTranscript,
    rebuildingTranscript,
    handlePauseTranscriber,
    handleResumeTranscriber,
    handlePauseRefiner,
    handleResumeRefiner,
    handleSkipRefinerBatch,
    toggleLogsPause,
    handleSelectJob,
    clearLogsForStep,
  } = useJob();

  // Clear selected chunk when job changes
  React.useEffect(() => {
    setSelectedChunkIndex(null);
  }, [jobId]);

  // Filter out refining text from logs (text between [REFINE_TEXT_START] and [REFINE_TEXT_END])
  // This text should only be shown in RefiningTextPreview, not in the logs panel
  const filteredRefinerLogs = useMemo(() => {
    const logs = logsByStep.refiner;
    const filtered = [];
    let inRefineBlock = false;
    
    for (const log of logs) {
      const msg = log.message;
      
      if (msg.includes("[REFINE_TEXT_START]")) {
        inRefineBlock = true;
        continue;
      }
      if (msg.includes("[REFINE_TEXT_END]")) {
        inRefineBlock = false;
        continue;
      }
      if (!inRefineBlock) {
        filtered.push(log);
      }
    }
    
    return filtered;
  }, [logsByStep.refiner]);

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
        <aside className="sidebar">
          <JobsList
            currentJobId={jobId}
            onSelectJob={handleSelectJob}
          />
        </aside>

        <section className="content">
          <div className="steps">
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
          </div>
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
                jobId={jobId || undefined}
                onCancel={handleCancelChunk}
                onSplit={handleSplitChunk}
                onCancelSubChunk={handleCancelSubChunk}
                onRetranscribeSubChunk={handleRetranscribeSubChunk}
                onSkip={handleSkipChunk}
                disabled={!jobId}
                selectedChunkIndex={selectedChunkIndex}
                onChunkSelect={setSelectedChunkIndex}
              />
              <LogsSection
                title={aliases.transcriber}
                logs={logsByStep.transcriber}
                emptyLabel={t("noLogs")}
                paused={logsPaused}
                bufferedCount={bufferedCount}
                onTogglePause={toggleLogsPause}
                onClearLogs={() => clearLogsForStep("transcriber")}
                agentPaused={job?.agentPaused === "transcriber" ? "transcriber" : null}
                onPauseAgent={handlePauseTranscriber}
                onResumeAgent={handleResumeTranscriber}
                showAgentControls={jobId && (job?.phase === "transcriber" || job?.status === "running")}
              />
              {jobId && (
                <ProjectFiles jobId={jobId} selectedChunkIndex={selectedChunkIndex} />
              )}
            </>
          )}

          {activeStep === "refiner" && (
            <>
              {/* Show refiner control panel when awaiting manual start */}
              {job?.phase === "awaiting_refiner" && (
                <div className="card refiner-control">
                  <div className="card__header">
                    <h3>Refiner Stage</h3>
                  </div>
                  <p className="refiner-control__desc">
                    Transcription is complete. You can now:
                  </p>
                  <ul className="refiner-control__options">
                    <li>Review and split any failed chunks (see Transcriber tab)</li>
                    <li>Download the raw transcript</li>
                    <li>Start the Refiner to improve the transcript</li>
                  </ul>
                  <div className="refiner-control__actions">
                    <button
                      className="primary"
                      onClick={handleStartRefiner}
                      disabled={!jobId}
                    >
                      Start Refiner
                    </button>
                    <button
                      className="ghost"
                      onClick={handleSkipRefiner}
                      disabled={!jobId}
                    >
                      Skip Refiner
                    </button>
                  </div>
                </div>
              )}
              {/* Show rebuild transcript and start refiner buttons for completed projects */}
              {jobId && (job?.phase === "completed" || job?.status === "done" || job?.phase === "awaiting_refiner") && (
                <div className="card" style={{ marginBottom: "16px" }}>
                  <div className="card__header">
                    <h3>Управление транскриптом</h3>
                  </div>
                  <div style={{ padding: "16px" }}>
                    <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
                      <button
                        className="btn btn--secondary"
                        onClick={handleRebuildTranscript}
                        disabled={rebuildingTranscript}
                        style={{ display: "flex", alignItems: "center", gap: "8px" }}
                      >
                        {rebuildingTranscript && <div className="spinner spinner--sm"></div>}
                        {rebuildingTranscript ? "Пересборка..." : "🔄 Пересобрать transcript.md"}
                      </button>
                      <button
                        className="btn btn--primary"
                        onClick={handleStartRefiner}
                        disabled={job?.phase === "refiner"}
                        style={{ display: "flex", alignItems: "center", gap: "8px" }}
                      >
                        {job?.phase === "refiner" ? (
                          <>
                            <div className="spinner spinner--sm"></div>
                            Рефайнер выполняется...
                          </>
                        ) : (
                          "Запустить Refiner Agent"
                        )}
                      </button>
                    </div>
                    <p style={{ marginTop: "12px", fontSize: "12px", color: "#94a3b8" }}>
                      Пересоберите transcript.md после редактирования отдельных транскриптов чанков. Каждый запуск Refiner сохраняется в отдельный файл (transcript_fixed_1.md, transcript_fixed_2.md и т.д.)
                    </p>
                  </div>
                </div>
              )}
              {jobId && (job?.phase === "refiner" || job?.status === "running") && (
                <div className="card" style={{ marginBottom: "16px" }}>
                  <div className="card__header" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                    <h3>Управление агентом</h3>
                    <button
                      className="btn"
                      onClick={job?.agentPaused === "refiner" ? handleResumeRefiner : handlePauseRefiner}
                      style={{ display: "flex", alignItems: "center", gap: "8px" }}
                    >
                      {job?.agentPaused === "refiner" ? "▶ Возобновить" : "⏸ Пауза"}
                    </button>
                  </div>
                  {job?.agentPaused === "refiner" && (
                    <div style={{ padding: "12px", color: "#facc15", fontSize: "14px" }}>
                      ⚠ Агент на паузе: текущие запросы дорабатываются, новые запросы не отправляются
                    </div>
                  )}
                </div>
              )}
              {/* Text preview showing what's being sent to refining */}
              {jobId && job?.phase === "refiner" && (
                <RefiningTextPreview
                  logs={logsByStep.refiner}
                  onSkipBatch={handleSkipRefinerBatch}
                  isRefining={job?.phase === "refiner"}
                />
              )}
              <LogsSection
                title={aliases.refiner}
                logs={filteredRefinerLogs}
                emptyLabel={t("noLogs")}
                paused={logsPaused}
                bufferedCount={bufferedCount}
                onTogglePause={toggleLogsPause}
                onClearLogs={() => clearLogsForStep("refiner")}
              />
            </>
          )}

          {activeStep === "result" && (
            <>
              <ResultSection
                t={t}
                jobId={jobId || ""}
                job={job}
                links={resultLinks}
              />
              {jobId && (
                <ResultFiles jobId={jobId} />
              )}
            </>
          )}
        </section>
      </main>
      <AudioPlayer />
    </div>
  );
}

export default function App() {
  return (
    <I18nProvider>
      <AudioPlayerProvider>
        <AppShell />
      </AudioPlayerProvider>
    </I18nProvider>
  );
}
