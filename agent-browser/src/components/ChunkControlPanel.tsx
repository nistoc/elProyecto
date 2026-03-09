import React, { useState, useEffect } from "react";
import type { ChunkState, SplitJob, SubChunk } from "../types";
import { getChunkAudioUrl, fetchJobFiles, type SplitChunkFile } from "../api";
import { useAudioPlayer } from "../contexts/AudioPlayerContext";

type Props = {
  state?: ChunkState;
  jobId?: string;
  onCancel: (idx: number) => void;
  onSplit: (idx: number, parts: number) => void;
  onCancelSubChunk: (parentIdx: number, subIdx: number) => void;
  onSkip: (idx: number) => void;
  disabled?: boolean;
  selectedChunkIndex?: number | null;
  onChunkSelect?: (idx: number | null) => void;
};

/**
 * Get CSS class for sub-chunk status.
 */
function getSubChunkClass(status: SubChunk["status"]): string {
  switch (status) {
    case "pending":
      return "sub-chip--pending";
    case "started":
      return "sub-chip--active";
    case "completed":
      return "sub-chip--ok";
    case "cancelled":
      return "sub-chip--muted";
    case "failed":
      return "sub-chip--warn";
    default:
      return "";
  }
}

/**
 * Format elapsed time in seconds to a readable string.
 */
function formatDuration(seconds: number): string {
  if (seconds < 60) {
    return `${Math.floor(seconds)}s`;
  } else if (seconds < 3600) {
    const mins = Math.floor(seconds / 60);
    const secs = Math.floor(seconds % 60);
    return `${mins}m ${secs}s`;
  } else {
    const hours = Math.floor(seconds / 3600);
    const mins = Math.floor((seconds % 3600) / 60);
    return `${hours}h ${mins}m`;
  }
}

export function ChunkControlPanel({
  state,
  jobId,
  onCancel,
  onSplit,
  onCancelSubChunk,
  onRetranscribeSubChunk,
  onSkip,
  disabled,
  selectedChunkIndex,
  onChunkSelect,
}: Props) {
  const { play: playAudio, currentUrl, stop: stopAudio } = useAudioPlayer();
  const [splitChunksInfo, setSplitChunksInfo] = useState<Map<number, SplitChunkFile[]>>(new Map());
  // Track start times for active chunks
  const [chunkStartTimes, setChunkStartTimes] = useState<Map<number, number>>(new Map());
  // Track current elapsed times for display
  const [chunkDurations, setChunkDurations] = useState<Map<number, string>>(new Map());
  
  // Load split chunks information from files
  useEffect(() => {
    if (!jobId) {
      setSplitChunksInfo(new Map());
      return;
    }
    
    const loadSplitChunks = async () => {
      try {
        const { files } = await fetchJobFiles(jobId);
        const splitChunksMap = new Map<number, SplitChunkFile[]>();
        
        // Group split chunks by parent index
        files.splitChunks.forEach((chunk) => {
          if (chunk.parentIndex !== undefined) {
            if (!splitChunksMap.has(chunk.parentIndex)) {
              splitChunksMap.set(chunk.parentIndex, []);
            }
            splitChunksMap.get(chunk.parentIndex)!.push(chunk);
          }
        });
        
        setSplitChunksInfo(splitChunksMap);
      } catch (err) {
        console.error("Failed to load split chunks info:", err);
      }
    };
    
    loadSplitChunks();
    
    // Also reload when split jobs complete (with a small delay to ensure files are written)
    const completedSplitJobs = Object.entries(state?.splitJobs || {}).filter(
      ([_, job]) => job.status === "completed"
    );
    if (completedSplitJobs.length > 0) {
      const timeoutId = setTimeout(() => {
        loadSplitChunks();
      }, 500); // Small delay to ensure files are written to disk
      return () => clearTimeout(timeoutId);
    }
  }, [jobId, state?.splitJobs, state?.completed, state?.cancelled]);

  // Track start times for active chunks
  useEffect(() => {
    if (!state) return;
    
    const active = state.active || [];
    const now = Date.now();
    
    setChunkStartTimes((prev) => {
      const next = new Map(prev);
      
      // Add start time for newly active chunks
      active.forEach((idx) => {
        if (!next.has(idx)) {
          next.set(idx, now);
        }
      });
      
      // Remove start times for chunks that are no longer active
      prev.forEach((_, idx) => {
        if (!active.includes(idx)) {
          next.delete(idx);
        }
      });
      
      return next;
    });
  }, [state?.active]);

  // Update durations for active chunks periodically
  useEffect(() => {
    if (chunkStartTimes.size === 0) {
      setChunkDurations(new Map());
      return;
    }
    
    const updateDurations = () => {
      const now = Date.now();
      const durations = new Map<number, string>();
      
      chunkStartTimes.forEach((startTime, idx) => {
        const elapsed = (now - startTime) / 1000; // Convert to seconds
        durations.set(idx, formatDuration(elapsed));
      });
      
      setChunkDurations(durations);
    };
    
    // Update immediately
    updateDurations();
    
    // Update every second
    const interval = setInterval(updateDurations, 1000);
    
    return () => clearInterval(interval);
  }, [chunkStartTimes]);

  if (!state) return null;

  const active = state.active || [];
  const cancelled = state.cancelled || [];
  const completed = state.completed || [];
  const failed = state.failed || [];
  const skipped = state.skipped || [];
  const splitJobs = state.splitJobs || {};

  // Helper to check if a chunk has existing split chunks
  const hasExistingSplitChunks = (idx: number): boolean => {
    const chunks = splitChunksInfo.get(idx);
    return chunks !== undefined && chunks.length > 0;
  };
  
  // Get split chunks for a specific parent chunk
  const getSplitChunksForParent = (idx: number): SplitChunkFile[] => {
    return splitChunksInfo.get(idx) || [];
  };

  // Chunks that can be split (failed or cancelled, not already being split, and no existing split chunks)
  const splittableChunks = [...failed, ...cancelled].filter(
    (idx) => (!splitJobs[idx] || splitJobs[idx].status === "failed") && !hasExistingSplitChunks(idx)
  );

  // Cancelled chunks that have existing split chunks (show them instead of split buttons)
  // Also include completed chunks that have split chunks (they were cancelled but then split and completed)
  // Include chunks even if split job is still in progress (transcribing/merging)
  const cancelledWithSplitChunks = [...cancelled, ...completed].filter(
    (idx) => hasExistingSplitChunks(idx) || (splitJobs[idx] && splitJobs[idx].subChunks && splitJobs[idx].subChunks.length > 0)
  );

  // Cancelled chunks that are not in splittable list and don't have split chunks
  const cancelledOnly = cancelled.filter(
    (idx) => !splittableChunks.includes(idx) && !hasExistingSplitChunks(idx)
  );

  const handlePlayChunk = (idx: number) => {
    if (!jobId) return;
    const url = getChunkAudioUrl(jobId, idx);
    playAudio(url, `Chunk #${idx + 1}`);
  };

  const handleChunkClick = (idx: number, e: React.MouseEvent) => {
    // Don't trigger selection if clicking on buttons
    if ((e.target as HTMLElement).closest('button')) {
      return;
    }
    
    if (onChunkSelect) {
      if (selectedChunkIndex === idx) {
        // Deselect if clicking the same chunk
        onChunkSelect(null);
      } else {
        // Select the chunk
        onChunkSelect(idx);
      }
    }
  };

  return (
    <div className="card chunk-card">
      <div className="card__header">
        <h3>Chunks</h3>
        <span className="muted">
          total: {state.total ?? "—"} • done: {completed.length} • cancelled:{" "}
          {cancelled.length} • failed: {failed.length}
        </span>
      </div>

      {/* Active chunks */}
      <div className="chunk-section">
        <div className="chunk-section__title">Active</div>
        <div className="chunk-chips">
          {active.length === 0 && (
            <span className="muted">No active chunks</span>
          )}
          {active.map((idx) => {
            const duration = chunkDurations.get(idx);
            return (
              <div 
                key={idx} 
                className={`chip chip--action ${selectedChunkIndex === idx ? 'chip--selected' : ''}`}
                onClick={(e) => handleChunkClick(idx, e)}
                style={{ cursor: 'pointer' }}
              >
                <span>Chunk #{idx + 1}</span>
                {duration && (
                  <span className="chunk-duration" title="Длительность транскрибации">
                    {duration}
                  </span>
                )}
                <button
                  className="ghost"
                  onClick={() => onCancel(idx)}
                  disabled={disabled}
                  title="Cancel this chunk"
                >
                  cancel
                </button>
              </div>
            );
          })}
        </div>
      </div>


      {/* Splittable chunks (failed/cancelled) */}
      {splittableChunks.length > 0 && (
        <div className="chunk-section">
          <div className="chunk-section__title">
            Failed / Cancelled — can split
          </div>
          <div className="chunk-list">
            {splittableChunks.map((idx) => (
              <div 
                key={idx} 
                className={`chunk-item chunk-item--problem ${selectedChunkIndex === idx ? 'chunk-item--selected' : ''}`}
                onClick={(e) => handleChunkClick(idx, e)}
                style={{ cursor: 'pointer' }}
              >
                <div className="chunk-item__header">
                  <span className="chip chip--warn">
                    Chunk #{idx + 1}
                    {failed.includes(idx) ? " (failed)" : " (cancelled)"}
                  </span>
                  {jobId && (
                    <button
                      className="btn btn--sm btn--play"
                      onClick={() => handlePlayChunk(idx)}
                      title="Listen to this chunk"
                    >
                      {jobId && currentUrl === getChunkAudioUrl(jobId, idx) ? "⏸" : "▶"}
                    </button>
                  )}
                </div>
                <div className="chunk-item__actions">
                  <button
                    className="btn btn--sm btn--split"
                    onClick={() => onSplit(idx, 2)}
                    disabled={disabled}
                    title="Split into 2 parts"
                  >
                    ÷2
                  </button>
                  <button
                    className="btn btn--sm btn--split"
                    onClick={() => onSplit(idx, 3)}
                    disabled={disabled}
                    title="Split into 3 parts"
                  >
                    ÷3
                  </button>
                  <button
                    className="btn btn--sm btn--split"
                    onClick={() => onSplit(idx, 4)}
                    disabled={disabled}
                    title="Split into 4 parts"
                  >
                    ÷4
                  </button>
                  <button
                    className="btn btn--sm btn--skip"
                    onClick={() => onSkip(idx)}
                    disabled={disabled}
                    title="Skip this chunk permanently"
                  >
                    skip
                  </button>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Completed chunks (excluding those shown in cancelledWithSplitChunks) */}
      {completed.filter(idx => !cancelledWithSplitChunks.includes(idx)).length > 0 && (
        <div className="chunk-section">
          <div className="chunk-section__title">Completed</div>
          <div className="chunk-chips">
            {completed.filter(idx => !cancelledWithSplitChunks.includes(idx)).map((idx) => (
              <span 
                key={idx} 
                className={`chip chip--ok ${selectedChunkIndex === idx ? 'chip--selected' : ''}`}
                onClick={(e) => handleChunkClick(idx, e)}
                style={{ cursor: 'pointer' }}
              >
                #{idx + 1}
                {splitJobs[idx]?.status === "completed" && " (split)"}
              </span>
            ))}
          </div>
        </div>
      )}

      {/* Cancelled/Completed chunks with existing split chunks */}
      {cancelledWithSplitChunks.length > 0 && (
        <div className="chunk-section">
          <div className="chunk-section__title">Cancelled/Completed — Split Chunks</div>
          <div className="chunk-list">
            {cancelledWithSplitChunks.map((idx) => {
              const splitChunks = getSplitChunksForParent(idx);
              const splitJob = splitJobs[idx];
              // Group by subIndex and separate audio from transcripts
              const subChunksMap = new Map<number, { audio?: SplitChunkFile; transcript?: SplitChunkFile; status?: string }>();
              
              splitChunks.forEach((chunk) => {
                if (chunk.subIndex !== undefined) {
                  if (!subChunksMap.has(chunk.subIndex)) {
                    subChunksMap.set(chunk.subIndex, {});
                  }
                  const entry = subChunksMap.get(chunk.subIndex)!;
                  if (chunk.isTranscript) {
                    entry.transcript = chunk;
                  } else {
                    entry.audio = chunk;
                  }
                }
              });
              
              // Add status from splitJob if available
              if (splitJob && splitJob.subChunks) {
                splitJob.subChunks.forEach((sub, subIdx) => {
                  if (sub && subChunksMap.has(subIdx)) {
                    subChunksMap.get(subIdx)!.status = sub.status;
                  }
                });
              }
              
              const subChunksList = Array.from(subChunksMap.entries())
                .sort(([a], [b]) => a - b);
              
              // Check if split job is still in progress
              const isInProgress = splitJob && (splitJob.status === "transcribing" || splitJob.status === "merging" || splitJob.status === "splitting");
              
              return (
                <div 
                  key={idx} 
                  className={`chunk-item chunk-item--problem ${selectedChunkIndex === idx ? 'chunk-item--selected' : ''}`}
                  onClick={(e) => handleChunkClick(idx, e)}
                  style={{ cursor: 'pointer' }}
                >
                  <div className="chunk-item__header">
                    <span className="chip chip--warn">
                      Chunk #{idx + 1} (cancelled, {subChunksList.length} sub-chunks)
                      {isInProgress && " — в процессе"}
                    </span>
                  </div>
                  <div className="split-job__subchunks" style={{ marginTop: "8px" }}>
                    {subChunksList.map(([subIdx, { audio, transcript, status }]) => {
                      // Use status from splitJob if available, otherwise check transcript
                      const hasTranscript = transcript !== undefined;
                      const subStatus = status || (hasTranscript ? "completed" : "cancelled");
                      const isCancelled = subStatus === "cancelled";
                      const isCompleted = subStatus === "completed";
                      const isStarted = subStatus === "started";
                      
                      return (
                        <div
                          key={subIdx}
                          className={`sub-chip ${isCompleted ? 'sub-chip--ok' : isCancelled ? 'sub-chip--muted' : isStarted ? 'sub-chip--active' : 'sub-chip--warn'}`}
                          onClick={(e) => {
                            // Allow clicks on sub-chips to propagate to parent chunk selection
                            // Only stop if clicking on a button
                            if (!(e.target as HTMLElement).closest('button')) {
                              handleChunkClick(idx, e);
                            }
                          }}
                          style={{ cursor: 'pointer' }}
                        >
                          <span>#{idx + 1}.{subIdx + 1}</span>
                          {isCompleted && hasTranscript ? (
                            <span className="sub-chip__status" title="Транскрипт есть">✓</span>
                          ) : isCancelled ? (
                            <>
                              <span className="sub-chip__status" title="Транскрипт отсутствует">⚠</span>
                              <button
                                className="ghost-sm"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  onRetranscribeSubChunk(idx, subIdx);
                                }}
                                disabled={disabled}
                                title="Перезапустить транскрибацию"
                              >
                                ↻
                              </button>
                            </>
                          ) : isStarted ? (
                            <>
                              <span className="sub-chip__status">{subStatus}</span>
                              <button
                                className="ghost-sm"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  onCancelSubChunk(idx, subIdx);
                                }}
                                disabled={disabled}
                                title="Отменить транскрибацию"
                              >
                                ✕
                              </button>
                            </>
                          ) : (
                            <span className="sub-chip__status">{subStatus || (hasTranscript ? "✓" : "⚠")}</span>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* Cancelled chunks (not splittable, no split chunks) */}
      {cancelledOnly.length > 0 && (
        <div className="chunk-section">
          <div className="chunk-section__title">Cancelled</div>
          <div className="chunk-chips">
            {cancelledOnly.map((idx) => (
              <div 
                key={idx} 
                className={`chip chip--action ${selectedChunkIndex === idx ? 'chip--selected' : ''}`}
                onClick={(e) => handleChunkClick(idx, e)}
                style={{ cursor: 'pointer' }}
              >
                <span>Chunk #{idx + 1}</span>
                {jobId && (
                  <button
                    className="ghost"
                    onClick={(e) => {
                      e.stopPropagation();
                      handlePlayChunk(idx);
                    }}
                    title="Listen to this chunk"
                  >
                    {jobId && currentUrl === getChunkAudioUrl(jobId, idx) ? "⏸" : "▶"}
                  </button>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Skipped chunks */}
      {skipped.length > 0 && (
        <div className="chunk-section">
          <div className="chunk-section__title">Skipped</div>
          <div className="chunk-chips">
            {skipped.map((idx) => (
              <span 
                key={idx} 
                className={`chip chip--muted ${selectedChunkIndex === idx ? 'chip--selected' : ''}`}
                onClick={(e) => handleChunkClick(idx, e)}
                style={{ cursor: 'pointer' }}
              >
                #{idx + 1}
              </span>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

