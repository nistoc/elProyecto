/** Job status: queued | running | done | failed */
export type JobStatus = string;

/** Job phase: idle | transcriber | awaiting_refiner | refiner | completed */
export type JobPhase = string;

export interface LogEntry {
  ts: number;
  level: string;
  message: string;
}

export interface SubChunk {
  idx: number;
  status: string;
  audioPath?: string;
}

export interface SplitJob {
  parentIdx: number;
  parts: number;
  status: string;
  subChunks: SubChunk[];
  mergedText?: string;
  error?: string;
}

/** Agent04 virtual model row (ISO timestamps); survives page refresh. */
export interface ChunkVirtualModelEntry {
  index: number;
  startedAt?: string | null;
  completedAt?: string | null;
  state: string;
  errorMessage?: string | null;
  /** Sub-chunk transcription row (proto is_sub_chunk). */
  isSubChunk?: boolean | null;
  parentChunkIndex?: number | null;
  subChunkIndex?: number | null;
  /**
   * Multi-line diagnostics from Agent04 (retries, HTTP, completion); cleared on chunk restart.
   * Each line: ISO8601 timestamp, optional `[warn] ` / `[err] ` (see Agent04 `TranscriptActivityLogFormatter`), then message.
   */
  transcriptActivityLog?: string | null;
}

export interface ChunkState {
  total: number;
  active: number[];
  completed: number[];
  cancelled: number[];
  failed: number[];
  skipped?: number[];
  splitJobs?: Record<number, SplitJob>;
  chunkVirtualModel?: ChunkVirtualModelEntry[] | null;
}

export interface JobResult {
  transcript?: string;
  transcriptFixed?: string;
  transcriptFixedAll?: string[];
  rawJson?: string;
}

/** Refiner batch row from SSE snapshot (Agent06 stream via BFF). */
export interface RefinerThreadBatchSnapshot {
  batchIndex: number;
  totalBatches: number;
  beforeText: string;
  afterText?: string | null;
}

/** Silence runs (seconds on source file) for UI timeline; from Agent04 after detect. */
export interface TranscriptionSilenceRegion {
  startSec: number;
  endSec: number;
}

export interface TranscriptionSilenceTimeline {
  sourceDurationSec: number;
  regions: TranscriptionSilenceRegion[];
}

export interface JobSnapshot {
  id: string;
  status: JobStatus;
  phase: JobPhase;
  logs: LogEntry[];
  chunks?: ChunkState | null;
  result?: JobResult | null;
  originalFilename?: string | null;
  tags?: string[] | null;
  createdAt?: string | null;
  completedAt?: string | null;
  agentPaused?: string | null;
  mdOutputPath?: string | null;
  /** Full path to the job directory (where files are stored). For debugging and display. */
  jobDirectoryPath?: string | null;
  /** Agent04 gRPC job id (set while transcribing). Optional in API JSON. */
  agent04JobId?: string | null;
  /** Agent06 refine job id after refiner starts. */
  agent06RefineJobId?: string | null;
  /** System + user prompt Agent06 sends to OpenAI for the current batch (SSE). */
  refinerOpenAiRequestPreview?: string | null;
  /** From refiner_threads/checkpoint.json (remaining work; survives Agent06 restart). */
  refinerCheckpointNextBatchIndex0?: number | null;
  refinerCheckpointTotalBatches?: number | null;
  refinerCheckpointRemainingBatches?: number | null;
  /** Live refiner batch rows (SSE from BFF; Agent06 gRPC). UI uses this field only. */
  refinerThreadBatches?: RefinerThreadBatchSnapshot[] | null;
  /** Last Agent04 / transcription failure message (from gRPC or submit). */
  transcriptionError?: string | null;
  /** Short line for status bar (e.g. automatic HTTP retry with chunk context). */
  transcriptionFooterHint?: string | null;
  /** Agent04 pipeline phase label during transcription (e.g. silence compression). */
  transcriptionPhaseDetail?: string | null;
  /** Agent04 overall percent during transcription (0–100). */
  transcriptionProgressPercent?: number | null;
  /** Detected silence intervals on the source timeline (for progress UI). */
  transcriptionSilenceTimeline?: TranscriptionSilenceTimeline | null;
  /** Last Agent04 VM merge stats (API only; copy for debugging sync issues). */
  transcriptionSyncDebug?: string | null;
  /** @deprecated Not populated by API. Use GET /api/jobs/:id/files (JobProjectFiles). */
  files?: JobFileInfo[] | null;
}

export interface JobFileInfo {
  name: string;
  kind: 'text' | 'audio' | 'other';
  sizeBytes: number;
  lineCount?: number | null;
  durationSeconds?: number | null;
}

/** Structured project files (GET /api/jobs/:id/files), aligned with agent-browser. */
export interface JobProjectFile {
  name: string;
  relativePath: string;
  fullPath?: string | null;
  sizeBytes: number;
  kind: string;
  lineCount?: number | null;
  durationSeconds?: number | null;
  index?: number | null;
  parentIndex?: number | null;
  subIndex?: number | null;
  hasTranscript?: boolean | null;
  isTranscript?: boolean | null;
}

export interface JobProjectFiles {
  original: JobProjectFile[];
  chunks: JobProjectFile[];
  chunkJson: JobProjectFile[];
  transcripts: JobProjectFile[];
  intermediate: JobProjectFile[];
  converted: JobProjectFile[];
  splitChunks: JobProjectFile[];
}

export interface JobFilesApiResponse {
  files: JobProjectFiles;
  jobDir: string;
}

export interface JobListItem {
  id: string;
  originalFilename: string;
  status: string;
  phase: string;
  createdAt?: string | null;
  completedAt?: string | null;
  tags?: string[] | null;
}

export interface JobsListResponse {
  jobs: JobListItem[];
}

export interface CreateJobResponse {
  jobId: string;
}

/** SSE stream event: snapshot | status | log | chunk | split | done */
export interface StreamEvent {
  type: 'snapshot' | 'status' | 'log' | 'chunk' | 'split' | 'done';
  payload?: unknown;
}

/** Step status for StepCard */
export type StepStatus = 'waiting' | 'running' | 'done' | 'failed';
