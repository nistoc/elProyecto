import type {
  JobSnapshot,
  JobListItem,
  JobsListResponse,
  CreateJobResponse,
  StreamEvent,
  ConnectionState,
  JobFilesApiResponse,
  RefinerSettings,
  RefineBatch,
} from './types';
import type { ChunkArtifactGroup } from './utils/chunkArtifactGroups';
import type { JobSettings } from './utils/jobSettings';
import type { LessonPage, LessonsResponse } from './utils/lessons';
import type { LessonPackageView, PackageEdit } from './utils/lessonPackage';
import type { LessonNotesResponse } from './utils/lessonNotes';
import type { JobLanguageShareReport } from './utils/languageShare';
import type { RefinePrompt } from './utils/refinePrompt';
import {
  computeReconnectDelay,
  isTerminalSnapshot,
} from './utils/streamReconnect';

const API_BASE = '';

async function get<T>(path: string): Promise<T> {
  const r = await fetch(`${API_BASE}${path}`);
  if (!r.ok) throw new Error(`HTTP ${r.status}: ${path}`);
  return r.json() as Promise<T>;
}

async function del(path: string): Promise<void> {
  const r = await fetch(`${API_BASE}${path}`, { method: 'DELETE' });
  if (!r.ok && r.status !== 404) throw new Error(`HTTP ${r.status}: ${path}`);
}

export async function fetchJobsList(params?: {
  semanticKey?: string;
  status?: string;
  from?: string;
  to?: string;
  limit?: number;
  offset?: number;
}): Promise<JobListItem[]> {
  const q = new URLSearchParams();
  if (params?.semanticKey) q.set('semanticKey', params.semanticKey);
  if (params?.status) q.set('status', params.status);
  if (params?.from) q.set('from', params.from);
  if (params?.to) q.set('to', params.to);
  if (params?.limit != null) q.set('limit', String(params.limit));
  if (params?.offset != null) q.set('offset', String(params.offset));
  const query = q.toString();
  const path = query ? `/api/jobs?${query}` : '/api/jobs';
  const data = await get<JobsListResponse>(path);
  return data.jobs;
}

export async function fetchJob(id: string): Promise<JobSnapshot | null> {
  const r = await fetch(`${API_BASE}/api/jobs/${id}`);
  if (r.status === 404) return null;
  if (!r.ok) throw new Error(`HTTP ${r.status}`);
  return r.json() as Promise<JobSnapshot>;
}

/** Structured files under the job directory (404 if folder missing and not archive). */
export async function fetchJobFiles(
  id: string
): Promise<JobFilesApiResponse | null> {
  const r = await fetch(`${API_BASE}/api/jobs/${encodeURIComponent(id)}/files`);
  if (r.status === 404) return null;
  if (!r.ok) throw new Error(`HTTP ${r.status}: /api/jobs/.../files`);
  return r.json() as Promise<JobFilesApiResponse>;
}

/** Share of Spanish and Russian speech of a job (per track for a dialogue); null when the job folder is gone. */
export async function fetchJobLanguageShare(
  id: string
): Promise<JobLanguageShareReport | null> {
  const r = await fetch(
    `${API_BASE}/api/jobs/${encodeURIComponent(id)}/language-share`
  );
  if (r.status === 404) return null;
  if (!r.ok) throw new Error(`HTTP ${r.status}: /api/jobs/.../language-share`);
  return r.json() as Promise<JobLanguageShareReport>;
}

/** Chunk/split groups from Agent04 via API proxy (VM merged client-side). */
export async function fetchJobChunkArtifactGroups(
  jobId: string
): Promise<{ groups: ChunkArtifactGroup[] }> {
  const r = await fetch(
    `${API_BASE}/api/jobs/${encodeURIComponent(jobId)}/chunk-artifact-groups`
  );
  if (r.status === 404) throw new Error(`HTTP ${r.status}: job not found`);
  if (!r.ok) {
    const text = await r.text();
    let msg = text || `HTTP ${r.status}`;
    try {
      const j = JSON.parse(text) as { error?: string };
      if (j?.error) msg = j.error;
    } catch {
      /* keep */
    }
    throw new Error(msg);
  }
  return r.json() as Promise<{ groups: ChunkArtifactGroup[] }>;
}

/** URL to stream/download a file by path relative to the job directory. */
export function jobProjectFileContentUrl(
  jobId: string,
  relativePath: string
): string {
  const q = new URLSearchParams({ path: relativePath });
  return `${API_BASE}/api/jobs/${encodeURIComponent(jobId)}/files/content?${q}`;
}

/** Load file body as UTF-8 text (for editor). */
export async function fetchJobFileText(
  jobId: string,
  relativePath: string
): Promise<string> {
  const url = jobProjectFileContentUrl(jobId, relativePath);
  const r = await fetch(url);
  if (!r.ok) throw new Error(`HTTP ${r.status}`);
  return r.text();
}

/** Save text file (existing path only). Max body 50 MB (server limit). */
export async function putJobFileContent(
  jobId: string,
  relativePath: string,
  content: string
): Promise<void> {
  const q = new URLSearchParams({ path: relativePath });
  const r = await fetch(
    `${API_BASE}/api/jobs/${encodeURIComponent(jobId)}/files/content?${q}`,
    {
      method: 'PUT',
      headers: { 'Content-Type': 'text/plain; charset=utf-8' },
      body: content,
    }
  );
  if (!r.ok) {
    const textBody = await r.text();
    let msg = textBody || `HTTP ${r.status}`;
    try {
      const j = JSON.parse(textBody) as { error?: string };
      if (j?.error) msg = j.error;
    } catch {
      /* keep msg */
    }
    throw new Error(msg);
  }
}

/** Delete a single file under the job directory (not a directory). */
export async function deleteJobProjectFile(
  jobId: string,
  relativePath: string
): Promise<void> {
  const q = new URLSearchParams({ path: relativePath });
  const r = await fetch(
    `${API_BASE}/api/jobs/${encodeURIComponent(jobId)}/files/content?${q}`,
    { method: 'DELETE' }
  );
  if (!r.ok) {
    const textBody = await r.text();
    let msg = textBody || `HTTP ${r.status}`;
    try {
      const j = JSON.parse(textBody) as { error?: string };
      if (j?.error) msg = j.error;
    } catch {
      /* keep msg */
    }
    throw new Error(msg);
  }
}

/** Delete one operator-split sub-chunk bundle (audio, JSON, work-state row, cancel flag). */
export async function deleteJobSubChunk(
  jobId: string,
  parentChunkIndex: number,
  subChunkIndex: number
): Promise<void> {
  const r = await fetch(
    `${API_BASE}/api/jobs/${encodeURIComponent(jobId)}/chunks/${parentChunkIndex}/sub-chunks/${subChunkIndex}`,
    { method: 'DELETE' }
  );
  if (!r.ok) {
    const textBody = await r.text();
    let msg = textBody || `HTTP ${r.status}`;
    try {
      const j = JSON.parse(textBody) as { error?: string };
      if (j?.error) msg = j.error;
    } catch {
      /* keep msg */
    }
    throw new Error(msg);
  }
}

export async function createJob(
  file: File,
  tags?: string[],
  settings?: JobSettings
): Promise<CreateJobResponse> {
  const form = new FormData();
  form.append('file', file);
  if (tags?.length) form.append('tags', tags.join(','));
  // Settings of this one job (pause, compression, model); the API writes them to job_settings.json.
  if (settings && Object.keys(settings).length > 0)
    form.append('settings', JSON.stringify(settings));
  const r = await fetch(`${API_BASE}/api/jobs`, {
    method: 'POST',
    body: form,
  });
  if (!r.ok) {
    const text = await r.text();
    throw new Error(text || `HTTP ${r.status}`);
  }
  return r.json() as Promise<CreateJobResponse>;
}

export async function deleteJob(id: string): Promise<void> {
  await del(`/api/jobs/${id}`);
}

/** Manually start the refiner (agent06) for a job whose transcription finished, or skip it. */
export async function startRefine(
  jobId: string,
  skip = false,
  inputFile?: string | null,
  prompt?: RefinePrompt
): Promise<{ ok: boolean; message: string }> {
  const r = await fetch(
    `${API_BASE}/api/jobs/${encodeURIComponent(jobId)}/refine`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        skip,
        ...(inputFile ? { inputFile } : {}),
        ...(prompt ? { prompt } : {}),
      }),
    }
  );
  const text = await r.text();
  if (!r.ok) {
    let msg = text || `HTTP ${r.status}`;
    try {
      const j = JSON.parse(text) as { error?: string };
      if (j?.error) msg = j.error;
    } catch {
      /* keep */
    }
    throw new Error(msg);
  }
  try {
    return JSON.parse(text) as { ok: boolean; message: string };
  } catch {
    return { ok: true, message: skip ? 'refiner_skipped' : 'refiner_started' };
  }
}

export type ChunkActionName =
  | 'cancel'
  | 'retranscribe'
  | 'split'
  | 'transcribe_sub'
  | 'rebuild_combined'
  | 'rebuild_split_merged';

export interface ChunkActionResponseBody {
  ok: boolean;
  message: string;
}

export type PostChunkActionOptions = {
  splitParts?: number;
  subChunkIndex?: number;
};

/** POST /api/jobs/:id/chunk-actions — forwards to Agent04 (cancel supported). */
export async function postJobChunkAction(
  jobId: string,
  action: ChunkActionName,
  chunkIndex: number,
  options?: PostChunkActionOptions
): Promise<ChunkActionResponseBody> {
  const body: Record<string, unknown> = { action, chunkIndex };
  if (options?.splitParts != null && options.splitParts >= 2)
    body.splitParts = options.splitParts;
  if (
    options?.subChunkIndex !== undefined &&
    options.subChunkIndex !== null &&
    options.subChunkIndex >= 0
  )
    body.subChunkIndex = options.subChunkIndex;
  const r = await fetch(
    `${API_BASE}/api/jobs/${encodeURIComponent(jobId)}/chunk-actions`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }
  );
  const text = await r.text();
  if (!r.ok) {
    let msg = text || `HTTP ${r.status}`;
    try {
      const j = JSON.parse(text) as { error?: string };
      if (j?.error) msg = j.error;
    } catch {
      /* keep msg */
    }
    throw new Error(msg);
  }
  try {
    return JSON.parse(text) as ChunkActionResponseBody;
  } catch {
    throw new Error('Invalid chunk-action response');
  }
}

const MAX_RECONNECT_ATTEMPTS = 5;
const POLL_INTERVAL_MS = 5000;
const POLL_TICKS_PER_SSE_RETRY = 6;

export function subscribeToJob(
  jobId: string,
  onEvent: (event: StreamEvent) => void,
  options?: { signal?: AbortSignal }
): () => void {
  let attempt = 0;
  let closed = false;
  let eventSource: EventSource | null = null;
  let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  let pollTimer: ReturnType<typeof setInterval> | null = null;
  let pollTicks = 0;

  function stopPolling(): void {
    if (pollTimer != null) {
      clearInterval(pollTimer);
      pollTimer = null;
    }
  }

  function close(): void {
    closed = true;
    if (reconnectTimer != null) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }
    stopPolling();
    eventSource?.close();
    eventSource = null;
  }

  options?.signal?.addEventListener('abort', close);

  function emitConnection(state: ConnectionState): void {
    onEvent({ type: 'connection', payload: { state } });
  }

  /** Degraded mode after SSE gave up: poll the snapshot and periodically retry SSE. */
  function startPolling(): void {
    if (closed || pollTimer != null) return;
    emitConnection('polling');
    pollTicks = 0;
    pollTimer = setInterval(() => {
      if (closed) {
        stopPolling();
        return;
      }
      pollTicks += 1;
      fetchJob(jobId)
        .then((snap) => {
          if (closed) return;
          if (!snap) {
            // 404 — the job is gone; nothing more will ever arrive.
            onEvent({ type: 'done' });
            close();
            return;
          }
          onEvent({ type: 'snapshot', payload: snap });
          if (isTerminalSnapshot(snap)) {
            onEvent({ type: 'done' });
            close();
          }
        })
        .catch(() => {
          /* keep polling */
        });
      if (pollTimer != null && pollTicks % POLL_TICKS_PER_SSE_RETRY === 0) {
        connect();
      }
    }, POLL_INTERVAL_MS);
  }

  function connect(): void {
    if (closed || options?.signal?.aborted) return;
    eventSource?.close();
    const url = `${API_BASE}/api/jobs/${jobId}/stream`;
    eventSource = new EventSource(url);

    eventSource.onopen = () => {
      attempt = 0;
      stopPolling();
      emitConnection('connected');
    };

    eventSource.onmessage = (e: MessageEvent) => {
      attempt = 0;
      try {
        const data = JSON.parse(e.data) as StreamEvent;
        onEvent(data);
        if (data.type === 'done') {
          close();
        }
      } catch {
        // skip invalid
      }
    };

    eventSource.onerror = () => {
      eventSource?.close();
      eventSource = null;
      if (closed || options?.signal?.aborted) return;
      if (pollTimer != null) return; // polling mode owns the SSE retry cadence
      if (attempt >= MAX_RECONNECT_ATTEMPTS) {
        startPolling();
        return;
      }
      attempt += 1;
      emitConnection('reconnecting');
      reconnectTimer = setTimeout(connect, computeReconnectDelay(attempt));
    };
  }

  connect();
  return close;
}

async function readError(r: Response): Promise<string> {
  const text = await r.text();
  try {
    const j = JSON.parse(text) as { error?: string };
    if (j?.error) return j.error;
  } catch {
    /* not JSON */
  }
  return text || `HTTP ${r.status}`;
}

export async function fetchRefinerSettings(): Promise<RefinerSettings> {
  const r = await fetch(`${API_BASE}/api/refiner/settings`);
  if (!r.ok) throw new Error(await readError(r));
  return r.json() as Promise<RefinerSettings>;
}

export async function saveRefinerSettings(body: {
  promptTemplate: string | null;
  batchSize: number;
  contextLines: number;
  model: string;
  systemPrompt: string | null;
}): Promise<RefinerSettings> {
  const r = await fetch(`${API_BASE}/api/refiner/settings`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!r.ok) throw new Error(await readError(r));
  return r.json() as Promise<RefinerSettings>;
}

/** Saved batches of the job's last refiner run (agent06 writes them to refine_batches/). */
export async function fetchRefineBatches(
  jobId: string
): Promise<{ promptPreview: string | null; batches: RefineBatch[] }> {
  const raw = await get<{
    promptPreview: string | null;
    batches: Array<{
      index: number;
      total: number;
      startLine?: number;
      endLine?: number;
      contextText?: string;
      askedWords?: string;
      originalText: string;
      refinedText: string;
      ok: boolean;
      error?: string | null;
    }>;
  }>(`/api/jobs/${encodeURIComponent(jobId)}/refine-batches`);
  return {
    promptPreview: raw.promptPreview,
    batches: raw.batches.map((b) => ({
      index: b.index,
      total: b.total,
      original: b.originalText,
      refined: b.refinedText,
      ok: b.ok,
      error: b.error,
      context: b.contextText,
      asked: b.askedWords,
      startLine: b.startLine,
      endLine: b.endLine,
    })),
  };
}

/** Reads the `detail` of a problem response; falls back to the raw text or the status. */
async function problemText(r: Response): Promise<string> {
  const text = await r.text();
  try {
    const detail = (JSON.parse(text) as { detail?: unknown }).detail;
    if (typeof detail === 'string' && detail.trim() !== '') return detail;
  } catch {
    // not JSON: the text itself is the message
  }
  return text || `HTTP ${r.status}`;
}

/** Lessons of the recordings folder with the runs of every track (GET /api/sessions). */
export async function fetchLessons(): Promise<LessonsResponse> {
  return get<LessonsResponse>('/api/sessions');
}

/**
 * Starts a job for one track of a lesson: the API copies the file into a new job folder and runs
 * the usual pipeline. The transcription is paid.
 */
export async function startTrackRun(
  track: string,
  settings?: JobSettings
): Promise<CreateJobResponse> {
  const body: { track: string; settings?: JobSettings } = { track };
  if (settings && Object.keys(settings).length > 0) body.settings = settings;
  const r = await fetch(`${API_BASE}/api/sessions/runs`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!r.ok) throw new Error(await problemText(r));
  return r.json() as Promise<CreateJobResponse>;
}

export interface DialogueBuildResponse {
  jobId: string;
  file: string;
  entries: number;
  warnings: string[];
}

/** Merges the transcripts of participant-track jobs into a dialogue job. No model is called. */
export async function createDialogue(
  title: string,
  tracks: { jobId: string; speaker: string }[]
): Promise<DialogueBuildResponse> {
  const r = await fetch(`${API_BASE}/api/jobs/dialogue`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ title, tracks }),
  });
  if (!r.ok) throw new Error(await problemText(r));
  return r.json() as Promise<DialogueBuildResponse>;
}

export interface LessonPagesResponse {
  /** YYYY-MM-DD of the lesson. */
  date: string;
  pages: LessonPage[];
}

/**
 * Puts the address of a page made of the lesson of this date (the lesson package). A page with this
 * address that the lesson already has only gets the new title. Returns the pages of the lesson.
 */
export async function putLessonPage(
  date: string,
  url: string,
  title?: string
): Promise<LessonPagesResponse> {
  const r = await fetch(
    `${API_BASE}/api/sessions/${encodeURIComponent(date)}/pages`,
    {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ url, title: title?.trim() || null }),
    }
  );
  if (!r.ok) throw new Error(await problemText(r));
  return r.json() as Promise<LessonPagesResponse>;
}

/** Takes the page with this address off the lesson; the page itself stays where it is. */
export async function removeLessonPage(
  date: string,
  url: string
): Promise<LessonPagesResponse> {
  const r = await fetch(
    `${API_BASE}/api/sessions/${encodeURIComponent(date)}/pages?url=${encodeURIComponent(url)}`,
    { method: 'DELETE' }
  );
  if (!r.ok) throw new Error(await problemText(r));
  return r.json() as Promise<LessonPagesResponse>;
}

/**
 * The package of the lesson of this date (GET /api/sessions/{date}/package): its lists with the owner's
 * edits put on and every word counted against the lessons before it.
 */
/** The notes of a lesson from the vault copy in its day folder, homework first. */
export async function fetchLessonNotes(
  date: string
): Promise<LessonNotesResponse> {
  const r = await fetch(
    `${API_BASE}/api/sessions/${encodeURIComponent(date)}/notes`
  );
  if (!r.ok) throw new Error(await problemText(r));
  return r.json() as Promise<LessonNotesResponse>;
}

/** The text (markdown) of one note of a lesson, by its path. */
export async function fetchLessonNoteText(
  date: string,
  path: string
): Promise<string> {
  const r = await fetch(
    `${API_BASE}/api/sessions/${encodeURIComponent(date)}/notes/text?path=${encodeURIComponent(path)}`
  );
  if (!r.ok) throw new Error(await problemText(r));
  return r.text();
}

export async function fetchLessonPackage(
  date: string
): Promise<LessonPackageView> {
  const r = await fetch(
    `${API_BASE}/api/sessions/${encodeURIComponent(date)}/package`
  );
  if (!r.ok) throw new Error(await problemText(r));
  return r.json() as Promise<LessonPackageView>;
}

/**
 * Writes fields of one item of a package anew, or takes the item out of the lists. The package file is
 * not changed: the edit is kept next to it. Returns the package as it reads now.
 */
export async function putLessonPackageEdit(
  date: string,
  id: string,
  change: PackageEdit
): Promise<LessonPackageView> {
  const r = await fetch(
    `${API_BASE}/api/sessions/${encodeURIComponent(date)}/package/marks/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(change),
    }
  );
  if (!r.ok) throw new Error(await problemText(r));
  return r.json() as Promise<LessonPackageView>;
}
