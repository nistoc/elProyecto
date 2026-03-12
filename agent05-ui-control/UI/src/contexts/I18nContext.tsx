import {
  createContext,
  useContext,
  useState,
  useCallback,
  type ReactNode,
} from 'react';

type Locale = 'en' | 'ru' | 'es';

const messages: Record<Locale, Record<string, string>> = {
  en: {
    appTitle: 'XtractManager',
    clear: 'Clear',
    upload: 'Upload',
    transcriber: 'Transcriber',
    refiner: 'Refiner',
    result: 'Result',
    jobs: 'Jobs',
    refresh: 'Refresh',
    selectFile: 'Select audio file',
    start: 'Start',
    noFileSelected: 'No file selected',
    status: 'Status',
    phase: 'Phase',
    filename: 'Filename',
    copyFilename: 'Copy filename',
    waiting: 'Waiting',
    running: 'Running',
    done: 'Done',
    failed: 'Failed',
    deleteJob: 'Delete job',
    confirmDelete: 'Delete this job?',
    cancel: 'Cancel',
    delete: 'Delete',
    logs: 'Logs',
    pause: 'Pause',
    resume: 'Resume',
    clearLogs: 'Clear logs',
  },
  ru: {
    appTitle: 'XtractManager',
    clear: 'Очистить',
    upload: 'Загрузка',
    transcriber: 'Транскрайбер',
    refiner: 'Рефайнер',
    result: 'Результат',
    jobs: 'Задания',
    refresh: 'Обновить',
    selectFile: 'Выберите аудиофайл',
    start: 'Запустить',
    noFileSelected: 'Файл не выбран',
    status: 'Статус',
    phase: 'Фаза',
    filename: 'Имя файла',
    copyFilename: 'Копировать имя файла',
    waiting: 'Ожидание',
    running: 'Выполняется',
    done: 'Готово',
    failed: 'Ошибка',
    deleteJob: 'Удалить задание',
    confirmDelete: 'Удалить это задание?',
    cancel: 'Отмена',
    delete: 'Удалить',
    logs: 'Логи',
    pause: 'Пауза',
    resume: 'Продолжить',
    clearLogs: 'Очистить логи',
  },
  es: {
    appTitle: 'XtractManager',
    clear: 'Limpiar',
    upload: 'Subir',
    transcriber: 'Transcriptor',
    refiner: 'Refinador',
    result: 'Resultado',
    jobs: 'Trabajos',
    refresh: 'Actualizar',
    selectFile: 'Seleccione archivo de audio',
    start: 'Iniciar',
    noFileSelected: 'Ningún archivo seleccionado',
    status: 'Estado',
    phase: 'Fase',
    filename: 'Nombre de archivo',
    copyFilename: 'Copiar nombre',
    waiting: 'Esperando',
    running: 'En curso',
    done: 'Hecho',
    failed: 'Error',
    deleteJob: 'Eliminar trabajo',
    confirmDelete: '¿Eliminar este trabajo?',
    cancel: 'Cancelar',
    delete: 'Eliminar',
    logs: 'Registros',
    pause: 'Pausar',
    resume: 'Reanudar',
    clearLogs: 'Borrar registros',
  },
};

type TFunc = (key: string) => string;

const I18nContext = createContext<{
  t: TFunc;
  locale: Locale;
  setLocale: (l: Locale) => void;
} | null>(null);

export function I18nProvider({ children }: { children: ReactNode }) {
  const [locale, setLocale] = useState<Locale>('en');
  const t = useCallback<TFunc>((key) => messages[locale][key] ?? key, [locale]);
  return (
    <I18nContext.Provider value={{ t, locale, setLocale }}>
      {children}
    </I18nContext.Provider>
  );
}

export function useI18n() {
  const ctx = useContext(I18nContext);
  if (!ctx) throw new Error('useI18n must be used within I18nProvider');
  return ctx;
}
