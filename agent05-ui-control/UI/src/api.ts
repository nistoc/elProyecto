import type {
  JobSnapshot,
  JobListItem,
  JobsListResponse,
  CreateJobResponse,
  StreamEvent,
  JobFilesApiResponse,
} from './types';
import type { ChunkArtifactGroup } from './utils/chunkArtifactGroups';

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
  const r = await fetch(
    `${API_BASE}/api/jobs/${encodeURIComponent(id)}/files`
  );
  if (r.status === 404) return null;
  if (!r.ok) throw new Error(`HTTP ${r.status}: /api/jobs/.../files`);
  return r.json() as Promise<JobFilesApiResponse>;
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
  tags?: string[]
): Promise<CreateJobResponse> {
  const form = new FormData();
  form.append('file', file);
  if (tags?.length) form.append('tags', tags.join(','));
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

export type ChunkActionName =
  | 'cancel'
  | 'skip'
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
const RECONNECT_DELAY_MS = 2000;

export function subscribeToJob(
  jobId: string,
  onEvent: (event: StreamEvent) => void,
  options?: { signal?: AbortSignal }
): () => void {
  let attempt = 0;
  let closed = false;
  let eventSource: EventSource | null = null;

  function close(): void {
    closed = true;
    eventSource?.close();
    eventSource = null;
  }

  options?.signal?.addEventListener('abort', close);

  function connect(): void {
    if (closed || options?.signal?.aborted) return;
    eventSource?.close();
    const url = `${API_BASE}/api/jobs/${jobId}/stream`;
    eventSource = new EventSource(url);

    eventSource.onmessage = (e: MessageEvent) => {
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
      if (attempt >= MAX_RECONNECT_ATTEMPTS) {
        onEvent({ type: 'done' });
        return;
      }
      attempt += 1;
      setTimeout(connect, RECONNECT_DELAY_MS);
    };
  }

  connect();
  return close;
}
