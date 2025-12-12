export type JobStatus = "queued" | "running" | "failed" | "done";
export type JobPhase = "idle" | "transcriber" | "refiner" | "completed";

export type LogLevel = "info" | "warn" | "error";

export type LogEntry = {
  ts: number;
  level: LogLevel;
  message: string;
};

export type ChunkState = {
  total: number;
  active: number[];
  completed: number[];
  cancelled: number[];
  failed: number[];
};

export type ChunkEventPayload = {
  status: "prepared" | "started" | "completed" | "cancelled" | "failed";
  idx?: number;
  total?: number;
  basename?: string;
  message?: string;
};

export type JobResult = {
  transcript?: string;
  transcriptFixed?: string;
  rawJson?: string;
};

export type JobSnapshot = {
  id: string;
  status: JobStatus;
  phase: JobPhase;
  logs: LogEntry[];
  chunks?: ChunkState;
  result?: JobResult;
};

export type StreamEvent =
  | { type: "snapshot"; payload: JobSnapshot }
  | { type: "log"; payload: LogEntry }
  | { type: "status"; payload: Partial<JobSnapshot> }
  | { type: "chunk"; payload: ChunkEventPayload }
  | { type: "done"; payload?: unknown };

// Runtime placeholders to satisfy value imports (bundler strips types).
export const JobSnapshot = {} as JobSnapshot;
export const LogEntry = {} as LogEntry;
export const StreamEvent = {} as StreamEvent;

