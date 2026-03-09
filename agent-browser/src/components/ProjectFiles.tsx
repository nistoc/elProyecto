import React, { useEffect, useState, useCallback } from "react";
import { fetchJobFiles, fetchFileContent, saveFileContent, type ProjectFiles } from "../api";
import { getCachedFiles, setCachedFiles, invalidateOnFileChange, invalidateCache } from "../utils/filesCache";
import { useAudioPlayer } from "../contexts/AudioPlayerContext";

// Helper to normalize path separators
const normalizePath = (path: string): string => path.replace(/\\/g, '/');

interface ProjectFilesProps {
  jobId: string;
  selectedChunkIndex?: number | null;
}

export function ProjectFiles({ jobId, selectedChunkIndex }: ProjectFilesProps) {
  const [files, setFiles] = useState<ProjectFiles | null>(null);
  const [jobDir, setJobDir] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const { play: playAudio, currentUrl } = useAudioPlayer();
  const [editingFile, setEditingFile] = useState<{ path: string; name: string } | null>(null);
  const [fileContent, setFileContent] = useState<string>("");
  const [originalContent, setOriginalContent] = useState<string>("");
  const [saving, setSaving] = useState(false);
  const [loadingContent, setLoadingContent] = useState(false);

  const loadFiles = useCallback(async (forceRefresh: boolean = false) => {
    if (!jobId) return;

    try {
      // If force refresh, invalidate cache first
      if (forceRefresh) {
        invalidateCache(jobId);
      }

      // Check cache first (if not forcing refresh)
      if (!forceRefresh) {
        const cached = getCachedFiles(jobId);
        if (cached) {
          setFiles(cached.files);
          setJobDir(cached.jobDir);
          setLoading(false);
          return;
        }
      }

      setLoading(true);
      setError(null);
      const data = await fetchJobFiles(jobId);
      setFiles(data.files);
      setJobDir(data.jobDir);
      // Cache the result
      setCachedFiles(jobId, data.files, data.jobDir);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load files");
    } finally {
      setLoading(false);
    }
  }, [jobId]);

  useEffect(() => {
    loadFiles(false);
  }, [loadFiles]);

  // Handle Esc key to close editor
  useEffect(() => {
    if (!editingFile) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !saving && !loadingContent) {
        handleCloseEditor();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => {
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, [editingFile, saving, loadingContent]);

  const formatSize = (bytes: number): string => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const formatDuration = (seconds: number): string => {
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    const secs = Math.floor(seconds % 60);
    
    if (hours > 0) {
      return `${hours}:${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
    }
    return `${minutes}:${secs.toString().padStart(2, '0')}`;
  };

  const formatLineCount = (count: number): string => {
    if (count === 1) return "1 строка";
    if (count >= 2 && count <= 4) return `${count} строки`;
    return `${count} строк`;
  };

  const isAudioFile = (name: string): boolean => {
    return /\.(m4a|mp3|wav|ogg|flac)$/i.test(name);
  };

  const isTextFile = (name: string): boolean => {
    return /\.(txt|md|json|log|text|flag)$/i.test(name);
  };

  const getFileUrl = (url: string): string => {
    const API_BASE = import.meta.env.VITE_API_BASE || "http://localhost:3001";
    return `${API_BASE}${url}`;
  };

  const handlePlayAudio = (url: string, fileName?: string) => {
    playAudio(url, fileName);
  };

  const handleFileDoubleClick = async (file: { path: string; name: string; url: string }) => {
    if (!isTextFile(file.name) || !jobDir) return;

    try {
      setLoadingContent(true);
      // Extract relative path from file.path (remove jobDir prefix)
      const normalizedJobDir = normalizePath(jobDir);
      const normalizedFilePath = normalizePath(file.path);
      const relativePath = normalizedFilePath.replace(normalizedJobDir + '/', '');
      const content = await fetchFileContent(jobId, relativePath);
      setEditingFile({ path: relativePath, name: file.name });
      setFileContent(content);
      setOriginalContent(content);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load file content");
    } finally {
      setLoadingContent(false);
    }
  };

  const handleCloseEditor = () => {
    setEditingFile(null);
    setFileContent("");
    setOriginalContent("");
  };

  const handleSaveFile = async () => {
    if (!editingFile) return;

    try {
      setSaving(true);
      await saveFileContent(jobId, editingFile.path, fileContent);
      setOriginalContent(fileContent);
      handleCloseEditor();
      // Invalidate cache and reload files to update line count
      invalidateOnFileChange(jobId);
      const data = await fetchJobFiles(jobId);
      setFiles(data.files);
      // Update cache with fresh data
      setCachedFiles(jobId, data.files, data.jobDir);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to save file");
    } finally {
      setSaving(false);
    }
  };

  const hasChanges = fileContent !== originalContent;

  const FileSection = ({
    title,
    files: fileList,
    showIndex = false,
    showParentIndex = false,
  }: {
    title: string;
    files: Array<{ name: string; path: string; size: number; url: string; index?: number; parentIndex?: number; subIndex?: number; hasTranscript?: boolean; isTranscript?: boolean; duration?: number; lineCount?: number }>;
    showIndex?: boolean;
    showParentIndex?: boolean;
  }) => {
    if (fileList.length === 0) return null;

    return (
      <div className="project-files__section">
        <h3 className="project-files__section-title">{title}</h3>
        <div className="project-files__list">
          {fileList.map((file) => {
            const fullUrl = getFileUrl(file.url);
            const isAudio = isAudioFile(file.name);
            const isPlaying = currentUrl === fullUrl;

            const hasTranscript = file.hasTranscript !== undefined ? file.hasTranscript : true;
            const isTranscriptFile = file.isTranscript === true;
            
            const isText = isTextFile(file.name);
            
            return (
              <div 
                key={file.path} 
                className={`project-files__item ${!hasTranscript ? 'project-files__item--no-transcript' : ''} ${isText ? 'project-files__item--text' : ''}`}
                onClick={() => isText && handleFileDoubleClick(file)}
                title={isText ? "Клик для редактирования" : undefined}
              >
                <div className="project-files__item-header">
                  {isAudio && !isTranscriptFile && (
                    <button
                      className={`project-files__play-btn ${isPlaying ? "playing" : ""}`}
                      onClick={(e) => {
                        e.stopPropagation();
                        handlePlayAudio(fullUrl, file.name);
                      }}
                      title={isPlaying ? "Остановить" : "Воспроизвести"}
                    >
                      {isPlaying ? "⏸" : "▶"}
                    </button>
                  )}
                  <span className="project-files__item-name">
                    {showParentIndex && file.parentIndex !== undefined
                      ? `[${file.parentIndex + 1}.${file.subIndex !== undefined ? file.subIndex + 1 : '?'}] ${isTranscriptFile ? '📄 ' : ''}${file.name}`
                      : showIndex && file.index !== undefined
                      ? `[${file.index + 1}] ${file.name}`
                      : file.name}
                  </span>
                  {showParentIndex && !isTranscriptFile && !hasTranscript && (
                    <span className="project-files__item-badge project-files__item-badge--warning" title="Транскрипт отсутствует">
                      ⚠ Нет транскрипта
                    </span>
                  )}
                  {showParentIndex && isTranscriptFile && (
                    <span className="project-files__item-badge project-files__item-badge--info" title="Транскрипт для этого sub-chunk">
                      📄 Транскрипт
                    </span>
                  )}
                  <div className="project-files__item-meta">
                    {isAudio && file.duration !== undefined && (
                      <span className="project-files__item-duration" title="Длительность">
                        ⏱ {formatDuration(file.duration)}
                      </span>
                    )}
                    {!isAudio && file.lineCount !== undefined && (
                      <span className="project-files__item-lines" title="Количество строк">
                        📄 {formatLineCount(file.lineCount)}
                      </span>
                    )}
                    <span className="project-files__item-size">
                      {formatSize(file.size)}
                    </span>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    );
  };

  if (loading) {
    return (
      <div className="card">
        <div className="card__header">
          <h3>Файлы проекта</h3>
        </div>
        <div className="project-files__loading">
          <div className="spinner"></div>
          <span>Загрузка файлов...</span>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="card">
        <div className="card__header">
          <h3>Файлы проекта</h3>
        </div>
        <div className="project-files__error">{error}</div>
      </div>
    );
  }

  if (!files) {
    return null;
  }

  // Filter files by selected chunk index
  let filteredFiles = files;
  if (selectedChunkIndex !== null && selectedChunkIndex !== undefined) {
    filteredFiles = {
      original: files.original, // Always show original
      chunks: files.chunks.filter((f) => f.index === selectedChunkIndex),
      chunkJson: files.chunkJson.filter((f) => f.index === selectedChunkIndex),
      transcripts: files.transcripts, // Show all transcripts
      intermediate: files.intermediate.filter((f) => {
        // Filter intermediate results by chunk index in filename
        const match = f.name.match(/(\d+)/);
        return match && parseInt(match[1], 10) === selectedChunkIndex;
      }),
      converted: files.converted, // Show all converted files
      splitChunks: files.splitChunks.filter((f) => f.parentIndex === selectedChunkIndex),
    };
  }

  const hasAnyFiles =
    filteredFiles.original.length > 0 ||
    filteredFiles.chunks.length > 0 ||
    filteredFiles.chunkJson.length > 0 ||
    filteredFiles.transcripts.length > 0 ||
    filteredFiles.intermediate.length > 0 ||
    filteredFiles.converted.length > 0 ||
    filteredFiles.splitChunks.length > 0;

  if (!hasAnyFiles) {
    return (
      <div className="card project-files">
        <div className="card__header">
          <h3>Файлы проекта</h3>
        </div>
        {jobDir && (
          <div className="project-files__path">
            <span className="project-files__path-label">Путь к папке:</span>
            <code className="project-files__path-value">{jobDir}</code>
          </div>
        )}
        <div className="project-files__empty">Файлы проекта не найдены</div>
      </div>
    );
  }

  return (
    <div className="card project-files">
      <div className="card__header" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <h3>Файлы проекта</h3>
        <button
          className="btn btn--sm"
          onClick={() => loadFiles(true)}
          disabled={loading}
          title="Обновить список файлов"
          style={{ display: "flex", alignItems: "center", gap: "4px" }}
        >
          {loading ? (
            <>
              <div className="spinner spinner--sm"></div>
              <span>Загрузка...</span>
            </>
          ) : (
            <>
              ↻
              <span>Обновить</span>
            </>
          )}
        </button>
      </div>
      {jobDir && (
        <div className="project-files__path">
          <span className="project-files__path-label">Путь к папке:</span>
          <code className="project-files__path-value">{jobDir}</code>
        </div>
      )}
      {selectedChunkIndex !== null && selectedChunkIndex !== undefined && (
        <div className="project-files__filter-info">
          Показаны файлы для чанка #{selectedChunkIndex + 1}
        </div>
      )}
      <div className="project-files__content">
        <FileSection title="Исходный файл" files={filteredFiles.original} />
        <FileSection
          title="Аудио чанки"
          files={filteredFiles.chunks}
          showIndex={true}
        />
        <FileSection
          title="JSON чанков"
          files={filteredFiles.chunkJson}
          showIndex={true}
        />
        <FileSection title="Транскрипты" files={filteredFiles.transcripts} />
        <FileSection
          title="Промежуточные результаты"
          files={filteredFiles.intermediate}
        />
        <FileSection title="Конвертированные WAV" files={filteredFiles.converted} />
        <FileSection
          title="Раздробленные чанки (sub-chunks)"
          files={filteredFiles.splitChunks}
          showParentIndex={true}
        />
      </div>

      {/* File editor modal */}
      {editingFile && (
        <div className="modal-overlay" onClick={handleCloseEditor}>
          <div className="modal modal--editor" onClick={(e) => e.stopPropagation()}>
            <div className="modal__header">
              <h3>Редактирование: {editingFile.name}</h3>
            </div>
            <div className="modal__body modal__body--editor">
              {loadingContent ? (
                <div className="modal__loading">
                  <div className="spinner"></div>
                  <span>Загрузка...</span>
                </div>
              ) : (
                <textarea
                  className="file-editor__textarea"
                  value={fileContent}
                  onChange={(e) => setFileContent(e.target.value)}
                  spellCheck={false}
                  autoFocus
                />
              )}
            </div>
            <div className="modal__actions">
              <button
                className="btn btn--ghost"
                onClick={handleCloseEditor}
                disabled={saving}
              >
                {hasChanges ? "Отмена" : "Закрыть"}
              </button>
              <button
                className="btn btn--primary"
                onClick={handleSaveFile}
                disabled={saving || !hasChanges || loadingContent}
              >
                {saving && <div className="spinner spinner--sm"></div>}
                {saving ? "Сохранение..." : "Сохранить"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
