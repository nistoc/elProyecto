export type JobStatus = "queued" | "running" | "failed" | "done";
export type JobPhase = "idle" | "transcriber" | "awaiting_refiner" | "refiner" | "completed";

export type LogLevel = "info" | "warn" | "error";

export type LogEntry = {
  ts: number;
  level: LogLevel;
  message: string;
};

/** Status of a sub-chunk in a split job */
export type SubChunkStatus = "pending" | "started" | "completed" | "cancelled" | "failed";

/** Information about a sub-chunk */
export type SubChunk = {
  idx: number;
  status: SubChunkStatus;
  audioPath?: string;
};

/** Status of a split job */
export type SplitJobStatus = "splitting" | "transcribing" | "merging" | "completed" | "failed";

/** Information about a split job for a problematic chunk */
export type SplitJob = {
  parentIdx: number;
  parts: number;
  status: SplitJobStatus;
  subChunks: SubChunk[];
  mergedText?: string;
  error?: string;
};

export type ChunkState = {
  total: number;
  active: number[];
  completed: number[];
  cancelled: number[];
  failed: number[];
  skipped?: number[];
  splitJobs?: Record<number, SplitJob>;
};

export type ChunkEventPayload = {
  status: "prepared" | "started" | "completed" | "cancelled" | "failed" | "skipped";
  idx?: number;
  total?: number;
  basename?: string;
  message?: string;
};

/** Payload for split-related SSE events */
export type SplitEventPayload = {
  type: "split_started" | "split_progress" | "split_completed" | "split_failed";
  parentIdx: number;
  parts?: number;
  event?: string;
  subIdx?: number;
  subChunks?: SubChunk[];
  mergedText?: string;
  error?: string;
};

export type JobResult = {
  transcript?: string;
  transcriptFixed?: string;
  transcriptFixedAll?: string[]; // All transcript_fixed files (transcript_fixed_1.md, transcript_fixed_2.md, etc.)
  rawJson?: string;
};

export type JobSnapshot = {
  id: string;
  status: JobStatus;
  phase: JobPhase;
  logs: LogEntry[];
  chunks?: ChunkState;
  result?: JobResult;
  agentPaused?: "transcriber" | "refiner" | null;
  /** Original audio file name (e.g. from upload) */
  originalFilename?: string;
};

export type StreamEvent =
  | { type: "snapshot"; payload: JobSnapshot }
  | { type: "log"; payload: LogEntry }
  | { type: "status"; payload: Partial<JobSnapshot> }
  | { type: "chunk"; payload: ChunkEventPayload }
  | { type: "split"; payload: SplitEventPayload }
  | { type: "done"; payload?: unknown };

