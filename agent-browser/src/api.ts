import type { JobSnapshot, StreamEvent } from "./types";

const API_BASE = import.meta.env.VITE_API_BASE || "http://localhost:3001";

export type JobListItem = {
  id: string;
  originalFilename: string;
  status: JobSnapshot["status"];
  phase: JobSnapshot["phase"];
  createdAt: string | null;
  completedAt: string | null;
};

export async function fetchJobsList(): Promise<JobListItem[]> {
  const res = await fetch(`${API_BASE}/api/jobs`);
  if (!res.ok) {
    throw new Error(`Failed to fetch jobs list: ${res.statusText}`);
  }
  const data = (await res.json()) as { jobs: JobListItem[] };
  return data.jobs;
}

/**
 * Delete a job by ID.
 */
export async function deleteJob(jobId: string) {
  const res = await fetch(`${API_BASE}/api/jobs/${jobId}`, {
    method: "DELETE",
  });
  if (!res.ok) {
    throw new Error(`Failed to delete job: ${res.statusText}`);
  }
  return (await res.json()) as { ok: boolean; message?: string };
}

export type ProjectFile = {
  name: string;
  path: string;
  size: number;
  url: string;
  index?: number;
  duration?: number; // Duration in seconds for audio files
  lineCount?: number; // Line count for text files
};

export type SplitChunkFile = ProjectFile & {
  parentIndex: number;
  subIndex?: number;
  hasTranscript?: boolean;
  isTranscript?: boolean; // true for transcript files, false for audio files
};

export type ProjectFiles = {
  original: ProjectFile[];
  chunks: ProjectFile[];
  chunkJson: ProjectFile[];
  transcripts: ProjectFile[];
  intermediate: ProjectFile[];
  converted: ProjectFile[];
  splitChunks: SplitChunkFile[];
};

// Map to track active fetchJobFiles requests to prevent duplicate requests
const activeFetchJobFilesRequests = new Map<string, Promise<{ files: ProjectFiles; jobDir: string }>>();

/**
 * Get list of all files in a job directory.
 * Uses request deduplication: if a request for the same jobId is already in progress,
 * returns the same Promise instead of making a new request.
 */
export async function fetchJobFiles(jobId: string): Promise<{ files: ProjectFiles; jobDir: string }> {
  // Check if there's already an active request for this jobId
  const existingRequest = activeFetchJobFilesRequests.get(jobId);
  if (existingRequest) {
    return existingRequest;
  }

  // Create new request
  const requestPromise = (async () => {
    try {
      const res = await fetch(`${API_BASE}/api/jobs/${jobId}/files`);
      if (!res.ok) {
        throw new Error(`Failed to fetch job files: ${res.statusText}`);
      }
      const data = (await res.json()) as { files: ProjectFiles; jobDir: string };
      return data;
    } finally {
      // Remove from active requests map when done (success or error)
      activeFetchJobFilesRequests.delete(jobId);
    }
  })();

  // Store the request in the map
  activeFetchJobFilesRequests.set(jobId, requestPromise);

  return requestPromise;
}

/**
 * Get file content by relative path within job directory.
 */
export async function fetchFileContent(jobId: string, filePath: string): Promise<string> {
  // Encode each path segment separately to preserve slashes
  const encodedPath = filePath.split('/').map(segment => encodeURIComponent(segment)).join('/');
  const res = await fetch(`${API_BASE}/api/jobs/${jobId}/files/${encodedPath}`);
  if (!res.ok) {
    throw new Error(`Failed to fetch file content: ${res.statusText}`);
  }
  const data = (await res.json()) as { content: string; path: string };
  return data.content;
}

/**
 * Save file content by relative path within job directory.
 */
export async function saveFileContent(jobId: string, filePath: string, content: string): Promise<void> {
  // Encode each path segment separately to preserve slashes
  const encodedPath = filePath.split('/').map(segment => encodeURIComponent(segment)).join('/');
  const res = await fetch(`${API_BASE}/api/jobs/${jobId}/files/${encodedPath}`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'text/plain',
    },
    body: content,
  });
  if (!res.ok) {
    throw new Error(`Failed to save file: ${res.statusText}`);
  }
}

export async function createJob(file: File) {
  const formData = new FormData();
  formData.append("file", file);

  const res = await fetch(`${API_BASE}/api/jobs`, {
    method: "POST",
    body: formData,
  });

  if (!res.ok) {
    throw new Error(`Upload failed: ${res.statusText}`);
  }
  return (await res.json()) as { jobId: string };
}

export async function fetchJob(jobId: string): Promise<JobSnapshot | null> {
  const res = await fetch(`${API_BASE}/api/jobs/${jobId}`);
  if (!res.ok) {
    if (res.status === 404) {
      return null; // Job not found
    }
    throw new Error(`Failed to fetch job: ${res.statusText}`);
  }
  return (await res.json()) as JobSnapshot;
}

export async function cancelChunk(jobId: string, idx: number) {
  const res = await fetch(
    `${API_BASE}/api/jobs/${jobId}/chunks/${idx}/cancel`,
    { method: "POST" }
  );
  if (!res.ok) {
    throw new Error(`Cancel failed: ${res.statusText}`);
  }
  return (await res.json()) as { ok: boolean };
}

/**
 * Split a failed/cancelled chunk into smaller parts and re-transcribe.
 */
export async function splitChunk(jobId: string, idx: number, parts: number) {
  const res = await fetch(
    `${API_BASE}/api/jobs/${jobId}/chunks/${idx}/split`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ parts }),
    }
  );
  if (!res.ok) {
    throw new Error(`Split failed: ${res.statusText}`);
  }
  return (await res.json()) as { ok: boolean; message?: string };
}

/**
 * Cancel a sub-chunk within a split job.
 */
export async function cancelSubChunk(jobId: string, parentIdx: number, subIdx: number) {
  const res = await fetch(
    `${API_BASE}/api/jobs/${jobId}/chunks/${parentIdx}/sub/${subIdx}/cancel`,
    { method: "POST" }
  );
  if (!res.ok) {
    throw new Error(`Cancel sub-chunk failed: ${res.statusText}`);
  }
  return (await res.json()) as { ok: boolean };
}

/**
 * Retranscribe a specific sub-chunk from a split job.
 */
export async function retranscribeSubChunk(jobId: string, parentIdx: number, subIdx: number) {
  const res = await fetch(
    `${API_BASE}/api/jobs/${jobId}/chunks/${parentIdx}/sub/${subIdx}/retranscribe`,
    { method: "POST" }
  );
  if (!res.ok) {
    throw new Error(`Retranscribe sub-chunk failed: ${res.statusText}`);
  }
  return (await res.json()) as { ok: boolean; message?: string };
}

/**
 * Permanently skip a chunk (no transcription).
 */
export async function skipChunk(jobId: string, idx: number) {
  const res = await fetch(
    `${API_BASE}/api/jobs/${jobId}/chunks/${idx}/skip`,
    { method: "POST" }
  );
  if (!res.ok) {
    throw new Error(`Skip failed: ${res.statusText}`);
  }
  return (await res.json()) as { ok: boolean };
}

/**
 * Start the refiner stage manually.
 */
export async function startRefiner(jobId: string) {
  const res = await fetch(
    `${API_BASE}/api/jobs/${jobId}/start-refiner`,
    { method: "POST" }
  );
  if (!res.ok) {
    throw new Error(`Start refiner failed: ${res.statusText}`);
  }
  return (await res.json()) as { ok: boolean };
}

export async function rebuildTranscript(jobId: string) {
  const url = `${API_BASE}/api/jobs/${jobId}/rebuild-transcript`;
  console.log("[API] Rebuild transcript request:", url);
  
  const res = await fetch(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
  });
  
  console.log("[API] Rebuild transcript response:", res.status, res.statusText);
  
  if (!res.ok) {
    const errorText = await res.text();
    console.error("[API] Rebuild transcript error:", errorText);
    throw new Error(`Rebuild transcript failed: ${res.statusText} - ${errorText}`);
  }
  
  const result = await res.json();
  console.log("[API] Rebuild transcript result:", result);
  return result as { ok: boolean; message?: string };
}

/**
 * Skip the refiner stage and mark job as done.
 */
export async function skipRefiner(jobId: string) {
  const res = await fetch(
    `${API_BASE}/api/jobs/${jobId}/skip-refiner`,
    { method: "POST" }
  );
  if (!res.ok) {
    throw new Error(`Skip refiner failed: ${res.statusText}`);
  }
  return (await res.json()) as { ok: boolean };
}

/**
 * Pause agent (transcriber or refiner).
 */
export async function pauseAgent(jobId: string, agent: "transcriber" | "refiner") {
  const res = await fetch(
    `${API_BASE}/api/jobs/${jobId}/pause-agent`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ agent }),
    }
  );
  if (!res.ok) {
    throw new Error(`Pause agent failed: ${res.statusText}`);
  }
  return (await res.json()) as { ok: boolean; message?: string };
}

/**
 * Resume agent (transcriber or refiner).
 */
export async function resumeAgent(jobId: string, agent: "transcriber" | "refiner") {
  const res = await fetch(
    `${API_BASE}/api/jobs/${jobId}/resume-agent`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ agent }),
    }
  );
  if (!res.ok) {
    throw new Error(`Resume agent failed: ${res.statusText}`);
  }
  return (await res.json()) as { ok: boolean; message?: string };
}

/**
 * Get the audio URL for a specific chunk.
 */
export function getChunkAudioUrl(jobId: string, idx: number): string {
  return `${API_BASE}/api/jobs/${jobId}/chunks/${idx}/audio`;
}

/**
 * Subscribe to job SSE stream with automatic reconnection.
 */
export function subscribeToJob(
  jobId: string,
  onEvent: (event: StreamEvent) => void
) {
  let es: EventSource | null = null;
  let reconnectAttempts = 0;
  let closed = false;

  const MAX_RECONNECT_ATTEMPTS = 5;
  const BASE_DELAY = 1000;

  function connect() {
    if (closed) return;

    es = new EventSource(`${API_BASE}/api/jobs/${jobId}/stream`);

    es.onopen = () => {
      reconnectAttempts = 0;
    };

    es.onmessage = (event) => {
      try {
        const parsed = JSON.parse(event.data) as StreamEvent;
        onEvent(parsed);

        // If job is done, close connection
        if (parsed.type === "done") {
          close();
        }
      } catch (err) {
        console.error("Failed to parse SSE event", err);
      }
    };

    es.onerror = () => {
      if (closed) return;

      // If connection is immediately closed, it might be a 404
      // Check if job exists before reconnecting
      if (es && es.readyState === EventSource.CLOSED && reconnectAttempts === 0) {
        // First error - check if job exists
        fetch(`${API_BASE}/api/jobs/${jobId}`)
          .then((res) => {
            if (res.status === 404) {
              // Job doesn't exist, stop reconnecting
              console.log(`Job ${jobId} not found, stopping SSE connection`);
              close();
              return;
            }
            // Job exists, try reconnecting
            handleReconnect();
          })
          .catch(() => {
            // On error, try reconnecting
            handleReconnect();
          });
      } else {
        // Subsequent errors, try reconnecting
        handleReconnect();
      }
    };

    function handleReconnect() {
      if (closed) return;

      es?.close();
      es = null;

      if (reconnectAttempts < MAX_RECONNECT_ATTEMPTS) {
        const delay = BASE_DELAY * Math.pow(2, reconnectAttempts);
        reconnectAttempts++;
        console.log(`SSE reconnecting in ${delay}ms (attempt ${reconnectAttempts})`);
        setTimeout(connect, delay);
      } else {
        console.error("SSE max reconnect attempts reached");
        close();
      }
    }
  }

  function close() {
    closed = true;
    es?.close();
    es = null;
  }

  connect();

  return close;
}
