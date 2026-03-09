import React, { useMemo } from "react";
import type { LogEntry } from "../types";

type Props = {
  logs: LogEntry[];
  onSkipBatch?: () => void;
  isRefining: boolean;
};

/**
 * Extracts the current refining text from logs.
 * Looks for text between [REFINE_TEXT_START] and [REFINE_TEXT_END] markers.
 */
function extractRefiningText(logs: LogEntry[]): string | null {
  // Search backwards from the end to find the most recent refining text
  let startIdx = -1;
  let endIdx = -1;
  
  for (let i = logs.length - 1; i >= 0; i--) {
    const msg = logs[i].message;
    
    if (msg.includes("[REFINE_TEXT_END]") && endIdx === -1) {
      endIdx = i;
    }
    if (msg.includes("[REFINE_TEXT_START]")) {
      startIdx = i;
      break;
    }
  }
  
  // If we found both markers
  if (startIdx !== -1 && endIdx !== -1 && startIdx < endIdx) {
    // The text is in the logs between start and end markers
    const textLines: string[] = [];
    for (let i = startIdx + 1; i < endIdx; i++) {
      // Extract just the log message without the Refiner Agent prefix
      const msg = logs[i].message;
      // Remove "Refiner Agent: " prefix if present
      const cleanMsg = msg.replace(/^Refiner Agent:\s*/, "");
      textLines.push(cleanMsg);
    }
    return textLines.join("\n");
  }
  
  // If we only found START (text is being collected), return partial
  if (startIdx !== -1 && endIdx === -1) {
    const textLines: string[] = [];
    for (let i = startIdx + 1; i < logs.length; i++) {
      const msg = logs[i].message;
      // Skip if it's the end marker or batch info
      if (msg.includes("[REFINE_TEXT_END]") || msg.includes("[BATCH") || msg.includes("[API]")) {
        break;
      }
      const cleanMsg = msg.replace(/^Refiner Agent:\s*/, "");
      textLines.push(cleanMsg);
    }
    if (textLines.length > 0) {
      return textLines.join("\n");
    }
  }
  
  return null;
}

/**
 * Extracts current batch info from logs.
 */
function extractBatchInfo(logs: LogEntry[]): { current: number; total: number } | null {
  // Search backwards for the most recent batch info
  for (let i = logs.length - 1; i >= 0; i--) {
    const msg = logs[i].message;
    const match = msg.match(/\[BATCH\s+(\d+)\/(\d+)\]/);
    if (match) {
      return {
        current: parseInt(match[1], 10),
        total: parseInt(match[2], 10),
      };
    }
  }
  return null;
}

export function RefiningTextPreview({ logs, onSkipBatch, isRefining }: Props) {
  const refiningText = useMemo(() => extractRefiningText(logs), [logs]);
  const batchInfo = useMemo(() => extractBatchInfo(logs), [logs]);
  
  if (!isRefining) {
    return null;
  }
  
  return (
    <div className="card" style={{ marginBottom: "16px" }}>
      <div className="card__header" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <div>
          <h3 style={{ margin: 0 }}>Текст на рефайнинг</h3>
          {batchInfo && (
            <span style={{ fontSize: "12px", color: "#94a3b8", marginLeft: "8px" }}>
              Батч {batchInfo.current} из {batchInfo.total}
            </span>
          )}
        </div>
        {onSkipBatch && (
          <button
            className="btn btn--secondary"
            onClick={onSkipBatch}
            style={{ display: "flex", alignItems: "center", gap: "6px" }}
            title="Пропустить текущий фрагмент и оставить оригинальный текст"
          >
            ⏭ Пропустить фрагмент
          </button>
        )}
      </div>
      <div
        className="refining-text-preview"
        style={{
          height: "700px",
          overflow: "auto",
          padding: "12px 16px",
          backgroundColor: "#0f172a",
          borderRadius: "0 0 8px 8px",
          fontFamily: "monospace",
          fontSize: "13px",
          lineHeight: "1.5",
          whiteSpace: "pre-wrap",
          color: "#e2e8f0",
        }}
      >
        {refiningText ? (
          refiningText
        ) : (
          <span style={{ color: "#64748b", fontStyle: "italic" }}>
            Ожидание текста для рефайнинга...
          </span>
        )}
      </div>
    </div>
  );
}
