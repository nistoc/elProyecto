import React, { createContext, useContext, useMemo, useState } from "react";

export type Locale = "en" | "ru" | "es";

type Dictionary = Record<string, string>;
type Messages = Record<Locale, Dictionary>;

const messages: Messages = {
  en: {
    appTitle: "Audio pipeline",
    uploadStep: "Upload",
    uploadDesc: "Pick an audio file to send into the pipeline.",
    transcriberStep: "Transcriber agent",
    refinerStep: "Refiner agent",
    resultStep: "Result",
    chooseFile: "Choose audio",
    startJob: "Start processing",
    language: "Language",
    dropHint: "Drag & drop or click to select",
    fileSelected: "File selected",
    running: "Running",
    waiting: "Waiting",
    done: "Done",
    failed: "Failed",
    viewLogs: "View logs",
    noLogs: "No logs yet.",
    download: "Open",
    jobId: "Job ID",
    status: "Status",
    phase: "Phase",
    clear: "Reset",
  },
  ru: {
    appTitle: "Аудио-пайплайн",
    uploadStep: "Загрузка",
    uploadDesc: "Выберите аудио для обработки.",
    transcriberStep: "Transcriber agent",
    refinerStep: "Refiner agent",
    resultStep: "Результат",
    chooseFile: "Выбрать аудио",
    startJob: "Запустить обработку",
    language: "Язык",
    dropHint: "Перетащите или кликните для выбора",
    fileSelected: "Файл выбран",
    running: "В процессе",
    waiting: "Ожидание",
    done: "Готово",
    failed: "Ошибка",
    viewLogs: "Логи",
    noLogs: "Логов пока нет",
    download: "Открыть",
    jobId: "ID задачи",
    status: "Статус",
    phase: "Этап",
    clear: "Сбросить",
  },
  es: {
    appTitle: "Pipeline de audio",
    uploadStep: "Subir",
    uploadDesc: "Elige un audio para procesar.",
    transcriberStep: "Agente transcriptor",
    refinerStep: "Agente refinador",
    resultStep: "Resultado",
    chooseFile: "Elegir audio",
    startJob: "Iniciar",
    language: "Idioma",
    dropHint: "Arrastra y suelta o haz clic",
    fileSelected: "Archivo seleccionado",
    running: "En progreso",
    waiting: "En espera",
    done: "Listo",
    failed: "Fallo",
    viewLogs: "Registros",
    noLogs: "Aún sin registros",
    download: "Abrir",
    jobId: "ID de tarea",
    status: "Estado",
    phase: "Fase",
    clear: "Reiniciar",
  },
};

export type TranslationKey = keyof (typeof messages)["en"];

type I18nContextValue = {
  locale: Locale;
  setLocale: (locale: Locale) => void;
  t: (key: TranslationKey) => string;
};

const I18nContext = createContext<I18nContextValue | null>(null);

export function I18nProvider({ children }: { children: React.ReactNode }) {
  const [locale, setLocale] = useState<Locale>("en");

  const t = useMemo(() => {
    return (key: TranslationKey) => messages[locale][key] || key;
  }, [locale]);

  const value = useMemo(() => ({ locale, setLocale, t }), [locale, t]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n() {
  const ctx = useContext(I18nContext);
  if (!ctx) {
    throw new Error("useI18n must be used within I18nProvider");
  }
  return ctx;
}
