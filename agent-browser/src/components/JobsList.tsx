import React, { useEffect, useState, useRef, useCallback } from "react";
import { fetchJobsList, deleteJob, type JobListItem } from "../api";
import type { JobStatus, JobPhase } from "../types";
import { invalidateAllCache } from "../utils/filesCache";

interface JobsListProps {
  currentJobId: string | null;
  onSelectJob: (jobId: string) => void;
}

export function JobsList({ currentJobId, onSelectJob }: JobsListProps) {
  const [jobs, setJobs] = useState<JobListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null);
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const scrollPositionRef = useRef<number>(0);
  const selectedJobRef = useRef<string | null>(null);

  const loadJobs = useCallback(async (preserveScroll = false) => {
    try {
      setLoading(true);
      setError(null);
      
      // Save current scroll position
      if (preserveScroll && scrollContainerRef.current) {
        scrollPositionRef.current = scrollContainerRef.current.scrollTop;
      }
      
      // Save selected job ID
      if (currentJobId) {
        selectedJobRef.current = currentJobId;
      }
      
      const jobsList = await fetchJobsList();
      setJobs(jobsList);
      
      // Invalidate files cache when refreshing jobs list (files might have changed)
      invalidateAllCache();
      
      // Restore scroll position or scroll to selected item
      // Use double requestAnimationFrame to ensure DOM is fully updated
      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          if (scrollContainerRef.current) {
            if (selectedJobRef.current && preserveScroll) {
              // Find and scroll to selected item (only if preserving scroll)
              const selectedElement = scrollContainerRef.current.querySelector(
                `[data-job-id="${selectedJobRef.current}"]`
              ) as HTMLElement;
              if (selectedElement) {
                // Check if element is already visible
                const container = scrollContainerRef.current;
                const elementTop = selectedElement.offsetTop - container.offsetTop;
                const elementBottom = elementTop + selectedElement.offsetHeight;
                const containerTop = container.scrollTop;
                const containerBottom = containerTop + container.clientHeight;
                
                // Only scroll if element is not fully visible
                if (elementTop < containerTop || elementBottom > containerBottom) {
                  selectedElement.scrollIntoView({ block: "nearest", behavior: "auto" });
                }
              } else if (preserveScroll) {
                // Selected item not found, restore scroll position
                scrollContainerRef.current.scrollTop = scrollPositionRef.current;
              }
            } else if (preserveScroll) {
              // No selected item, restore scroll position
              scrollContainerRef.current.scrollTop = scrollPositionRef.current;
            }
          }
        });
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load jobs");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadJobs();
    // No automatic refresh - user can refresh manually using the button
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []); // Load only once on mount

  // Save scroll position when currentJobId changes (user selects a job)
  useEffect(() => {
    if (currentJobId && scrollContainerRef.current) {
      selectedJobRef.current = currentJobId;
      // Scroll to selected item after a short delay to ensure DOM is updated
      setTimeout(() => {
        if (scrollContainerRef.current) {
          const selectedElement = scrollContainerRef.current.querySelector(
            `[data-job-id="${currentJobId}"]`
          ) as HTMLElement;
          if (selectedElement) {
            selectedElement.scrollIntoView({ block: "nearest", behavior: "smooth" });
          }
        }
      }, 100);
    }
  }, [currentJobId]);

  // Handle Delete key press
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Don't trigger delete if:
      // 1. No current job selected
      // 2. Delete confirmation modal is already open
      // 3. File editor modal is open (check for modal--editor class)
      // 4. Focus is in an input/textarea (user might be editing)
      const target = e.target as HTMLElement;
      const isInInput = target.tagName === 'INPUT' || target.tagName === 'TEXTAREA';
      const isEditorOpen = document.querySelector('.modal--editor') !== null;
      
      if (e.key === "Delete" && currentJobId && !deleteConfirm && !isEditorOpen && !isInInput) {
        e.preventDefault();
        setDeleteConfirm(currentJobId);
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [currentJobId, deleteConfirm]);

  const handleDelete = async (jobId: string) => {
    try {
      let nextJobId: string | null = null;
      
      // If deleted job was selected, select next/previous job first
      if (jobId === currentJobId) {
        const currentIndex = jobs.findIndex((j) => j.id === jobId);
        const remainingJobs = jobs.filter((j) => j.id !== jobId);
        
        if (remainingJobs.length > 0) {
          // Select next job, or previous if current was last
          let nextJob;
          if (currentIndex < remainingJobs.length) {
            // Select job at same position (next job moved up)
            nextJob = remainingJobs[currentIndex];
          } else {
            // Select last job if current was last
            nextJob = remainingJobs[remainingJobs.length - 1];
          }
          
          nextJobId = nextJob.id;
          // Select the next job before deleting
          onSelectJob(nextJobId);
        } else {
          // No jobs left, clear selection
          onSelectJob("");
        }
      }
      
      // Now delete the job
      await deleteJob(jobId);
      setDeleteConfirm(null);
      
      // Invalidate cache for deleted job
      invalidateAllCache();
      
      // Remove job from local list instead of reloading
      const updatedJobs = jobs.filter((j) => j.id !== jobId);
      setJobs(updatedJobs);
      
      // Save scroll position
      if (scrollContainerRef.current) {
        scrollPositionRef.current = scrollContainerRef.current.scrollTop;
      }
      
      // Set selectedJobRef for scroll
      if (nextJobId) {
        selectedJobRef.current = nextJobId;
      }
      
      // Scroll to next selected job if one was selected
      if (nextJobId) {
        requestAnimationFrame(() => {
          requestAnimationFrame(() => {
            setTimeout(() => {
              if (scrollContainerRef.current) {
                const selectedElement = scrollContainerRef.current.querySelector(
                  `[data-job-id="${nextJobId}"]`
                ) as HTMLElement;
                if (selectedElement) {
                  selectedElement.scrollIntoView({ block: "nearest", behavior: "smooth" });
                }
              }
            }, 50);
          });
        });
      }
    } catch (err) {
      alert(err instanceof Error ? err.message : "Failed to delete job");
    }
  };

  const getStatusColor = (status: JobStatus, phase: JobPhase): string => {
    if (status === "done") return "#10b981";
    if (status === "failed") return "#ef4444";
    if (status === "running" || phase === "transcriber" || phase === "refiner")
      return "#3b82f6";
    return "#94a3b8";
  };

  const getStatusText = (status: JobStatus, phase: JobPhase): string => {
    if (status === "done") return "Завершено";
    if (status === "failed") return "Ошибка";
    if (status === "running") {
      if (phase === "transcriber") return "Транскрипция";
      if (phase === "refiner") return "Улучшение";
      if (phase === "awaiting_refiner") return "Ожидание";
      return "Выполняется";
    }
    return "В очереди";
  };

  const formatDate = (dateStr: string | null, showTime: boolean = false): string => {
    if (!dateStr) return "";
    try {
      const date = new Date(dateStr);
      
      // If showTime is true, always show date and time
      if (showTime) {
        return date.toLocaleString("ru-RU", {
          day: "2-digit",
          month: "2-digit",
          year: "numeric",
          hour: "2-digit",
          minute: "2-digit",
        });
      }
      
      // Otherwise, show relative time for recent dates
      const now = new Date();
      const diffMs = now.getTime() - date.getTime();
      const diffMins = Math.floor(diffMs / 60000);
      const diffHours = Math.floor(diffMs / 3600000);
      const diffDays = Math.floor(diffMs / 86400000);

      if (diffMins < 1) return "только что";
      if (diffMins < 60) return `${diffMins} мин назад`;
      if (diffHours < 24) return `${diffHours} ч назад`;
      if (diffDays < 7) return `${diffDays} дн назад`;
      
      // For older dates, show date only
      return date.toLocaleDateString("ru-RU", {
        day: "2-digit",
        month: "2-digit",
        year: "numeric",
      });
    } catch {
      return "";
    }
  };

  return (
    <div className="jobs-list">
      <div className="jobs-list__header">
        <h2 className="jobs-list__title">Переводы</h2>
        <button
          className="jobs-list__refresh"
          onClick={() => loadJobs(true)}
          disabled={loading}
          title="Обновить список"
        >
          ↻
        </button>
      </div>

      {loading && jobs.length === 0 && (
        <div className="jobs-list__empty">
          <div className="spinner spinner--sm"></div>
          <span>Загрузка...</span>
        </div>
      )}

      {error && (
        <div className="jobs-list__error">
          <div>{error}</div>
          <button className="btn btn--sm" onClick={loadJobs}>
            Повторить
          </button>
        </div>
      )}

      {!loading && !error && jobs.length === 0 && (
        <div className="jobs-list__empty">Нет выполненных переводов</div>
      )}

      {!loading && !error && jobs.length > 0 && (
        <div 
          className="jobs-list__items"
          ref={scrollContainerRef}
        >
          {jobs.map((job) => {
            const isActive = job.id === currentJobId;
            const statusColor = getStatusColor(job.status, job.phase);
            return (
              <div
                key={job.id}
                data-job-id={job.id}
                className={`jobs-list__item ${isActive ? "active" : ""}`}
                onClick={() => onSelectJob(job.id)}
              >
                <div className="jobs-list__item-header">
                  <div
                    className="jobs-list__item-status"
                    style={{ backgroundColor: statusColor }}
                  />
                  <div className="jobs-list__item-title">
                    {job.originalFilename}
                  </div>
                  {isActive && (
                    <button
                      className="jobs-list__item-delete"
                      onClick={(e) => {
                        e.stopPropagation();
                        setDeleteConfirm(job.id);
                      }}
                      title="Удалить проект (Del)"
                    >
                      ×
                    </button>
                  )}
                </div>
                <div className="jobs-list__item-meta">
                  <span className="jobs-list__item-status-text">
                    {getStatusText(job.status, job.phase)}
                  </span>
                  {job.status === "done" && job.completedAt ? (
                    <span className="jobs-list__item-date">
                      {formatDate(job.completedAt, true)}
                    </span>
                  ) : job.createdAt ? (
                    <span className="jobs-list__item-date">
                      {formatDate(job.createdAt)}
                    </span>
                  ) : null}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Delete confirmation modal */}
      {deleteConfirm && (
        <div className="modal-overlay" onClick={() => setDeleteConfirm(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal__header">
              <h3>Подтверждение удаления</h3>
            </div>
            <div className="modal__body">
              <p>
                Вы уверены, что хотите удалить проект{" "}
                <strong>
                  {jobs.find((j) => j.id === deleteConfirm)?.originalFilename ||
                    "этот проект"}
                </strong>
                ?
              </p>
              <p className="modal__warning">
                Это действие нельзя отменить. Все файлы проекта будут удалены.
              </p>
            </div>
            <div className="modal__actions">
              <button
                className="btn btn--ghost"
                onClick={() => setDeleteConfirm(null)}
              >
                Отмена
              </button>
              <button
                className="btn btn--danger"
                onClick={() => handleDelete(deleteConfirm)}
              >
                Удалить
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
