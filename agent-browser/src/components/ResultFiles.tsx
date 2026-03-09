import React, { useEffect, useState } from "react";
import { fetchJobFiles, fetchFileContent, saveFileContent, type ProjectFiles } from "../api";
import { getCachedFiles, setCachedFiles, invalidateOnFileChange } from "../utils/filesCache";

// Helper to normalize path separators
const normalizePath = (path: string): string => path.replace(/\\/g, '/');

interface ResultFilesProps {
  jobId: string;
}

export function ResultFiles({ jobId }: ResultFilesProps) {
  const [files, setFiles] = useState<ProjectFiles | null>(null);
  const [jobDir, setJobDir] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editingFile, setEditingFile] = useState<{ path: string; name: string } | null>(null);
  const [fileContent, setFileContent] = useState<string>("");
  const [originalContent, setOriginalContent] = useState<string>("");
  const [saving, setSaving] = useState(false);
  const [loadingContent, setLoadingContent] = useState(false);

  useEffect(() => {
    if (!jobId) return;

    const loadFiles = async () => {
      try {
        // Check cache first
        const cached = getCachedFiles(jobId);
        if (cached) {
          setFiles(cached.files);
          setJobDir(cached.jobDir);
          setLoading(false);
          return;
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
    };

    loadFiles();
  }, [jobId]);

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

  const formatLineCount = (count: number): string => {
    if (count === 1) return "1 строка";
    if (count >= 2 && count <= 4) return `${count} строки`;
    return `${count} строк`;
  };

  const isTextFile = (name: string): boolean => {
    return /\.(txt|md|json|log|text|flag)$/i.test(name);
  };

  const getFileUrl = (url: string): string => {
    const API_BASE = import.meta.env.VITE_API_BASE || "http://localhost:3001";
    return `${API_BASE}${url}`;
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
  }: {
    title: string;
    files: Array<{ name: string; path: string; size: number; url: string; lineCount?: number }>;
  }) => {
    if (fileList.length === 0) return null;

    return (
      <div className="project-files__section">
        <h3 className="project-files__section-title">{title}</h3>
        <div className="project-files__list">
          {fileList.map((file) => {
            const isText = isTextFile(file.name);
            
            return (
              <div 
                key={file.path} 
                className={`project-files__item ${isText ? 'project-files__item--text' : ''}`}
                onClick={() => isText && handleFileDoubleClick(file)}
                title={isText ? "Клик для редактирования" : undefined}
              >
                <div className="project-files__item-header">
                  <span className="project-files__item-name">{file.name}</span>
                  <div className="project-files__item-meta">
                    {file.lineCount !== undefined && (
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
          <h3>Транскрипты</h3>
        </div>
        <div className="project-files__loading">
          <div className="spinner"></div>
          <span>Загрузка транскриптов...</span>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="card">
        <div className="card__header">
          <h3>Транскрипты</h3>
        </div>
        <div className="project-files__error">{error}</div>
      </div>
    );
  }

  if (!files) {
    return null;
  }

  // Filter to show only transcript files
  const transcriptFiles = {
    transcripts: files.transcripts,
  };

  const hasAnyFiles = transcriptFiles.transcripts.length > 0;

  if (!hasAnyFiles) {
    return (
      <div className="card project-files">
        <div className="card__header">
          <h3>Транскрипты</h3>
        </div>
        {jobDir && (
          <div className="project-files__path">
            <span className="project-files__path-label">Путь к папке:</span>
            <code className="project-files__path-value">{jobDir}</code>
          </div>
        )}
        <div className="project-files__empty">Транскрипты не найдены</div>
      </div>
    );
  }

  return (
    <div className="card project-files">
      <div className="card__header">
        <h3>Транскрипты</h3>
      </div>
      {jobDir && (
        <div className="project-files__path">
          <span className="project-files__path-label">Путь к папке:</span>
          <code className="project-files__path-value">{jobDir}</code>
        </div>
      )}
      <div className="project-files__content">
        <FileSection title="Транскрипты" files={transcriptFiles.transcripts} />
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
