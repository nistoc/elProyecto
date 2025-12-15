import type { JobSnapshot, StreamEvent } from "./types";

const API_BASE = import.meta.env.VITE_API_BASE || "http://localhost:3001";

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

export async function fetchJob(jobId: string): Promise<JobSnapshot> {
  const res = await fetch(`${API_BASE}/api/jobs/${jobId}`);
  if (!res.ok) {
    throw new Error("Job not found");
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

      es?.close();
      es = null;

      if (reconnectAttempts < MAX_RECONNECT_ATTEMPTS) {
        const delay = BASE_DELAY * Math.pow(2, reconnectAttempts);
        reconnectAttempts++;
        console.log(`SSE reconnecting in ${delay}ms (attempt ${reconnectAttempts})`);
        setTimeout(connect, delay);
      } else {
        console.error("SSE max reconnect attempts reached");
      }
    };
  }

  function close() {
    closed = true;
    es?.close();
    es = null;
  }

  connect();

  return close;
}
