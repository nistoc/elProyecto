/** Job status: queued | running | done | failed */
export type JobStatus = string;

/** Job phase: idle | transcriber | awaiting_refiner | refiner | completed */
export type JobPhase = string;

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
  /** Multi-line diagnostics from Agent04 (retries, HTTP, completion); cleared on chunk restart. */
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

export interface JobSnapshot {
  id: string;
  status: JobStatus;
  phase: JobPhase;
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
  /** Last Agent04 / transcription failure message (from gRPC or submit). */
  transcriptionError?: string | null;
  /** Short line for status bar (e.g. automatic HTTP retry with chunk context). */
  transcriptionFooterHint?: string | null;
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
  chunkMd: JobProjectFile[];
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

/** SSE stream event: snapshot | status | log | chunk | split | done; 'connection' is client-side (SSE health). */
export interface StreamEvent {
  type:
    | 'snapshot'
    | 'status'
    | 'log'
    | 'chunk'
    | 'split'
    | 'done'
    | 'connection'
    | 'refine_progress';
  payload?: unknown;
}

/** One refined fragment: the text before and after the refiner touched it. */
export interface RefineBatch {
  index: number;
  total: number;
  original: string;
  refined: string;
  ok: boolean;
  error?: string | null;
  /** Lines of the previous (already refined) batch sent along as context — the seam between blocks. */
  context?: string;
  /** 1-based content line range of this batch in the transcript. */
  startLine?: number;
  endLine?: number;
}

/** SSE 'refine_progress' payload: prompt + incremental batch deltas. */
export interface RefineProgressEvent {
  promptPreview?: string | null;
  currentBatch?: number;
  totalBatches?: number;
  progressPercent?: number;
  batches?: RefineBatch[];
}

/** Accumulated refiner progress held in the UI (batches merged by index). */
export interface RefineProgressState {
  promptPreview: string | null;
  currentBatch: number;
  totalBatches: number;
  progressPercent: number;
  batches: RefineBatch[];
  /** True when loaded from the saved last run (refine_batches/), not from the live stream. */
  fromDisk?: boolean;
}

/** Operator-tuned refiner settings (GET/PUT /api/refiner/settings), shared by all jobs. */
export interface RefinerSettings {
  promptTemplate: string;
  isCustomPrompt: boolean;
  defaultPromptTemplate: string;
  systemPrompt: string;
  isCustomSystemPrompt: boolean;
  defaultSystemPrompt: string;
  batchSize: number;
  contextLines: number;
  model: string;
  defaults: { batchSize: number; contextLines: number; model: string };
}

/** Client-side SSE connection health, delivered as StreamEvent type 'connection'. */
export type ConnectionState = 'connected' | 'reconnecting' | 'polling';

/** Step status for StepCard */
export type StepStatus = 'waiting' | 'running' | 'done' | 'failed';
