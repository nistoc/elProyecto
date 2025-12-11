import { JobSnapshot, StreamEvent } from "./types";

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

export function subscribeToJob(
  jobId: string,
  onEvent: (event: StreamEvent) => void,
) {
  const es = new EventSource(`${API_BASE}/api/jobs/${jobId}/stream`);
  es.onmessage = (event) => {
    try {
      const parsed = JSON.parse(event.data) as StreamEvent;
      onEvent(parsed);
    } catch (err) {
      console.error("Failed to parse SSE event", err);
    }
  };
  es.onerror = () => {
    es.close();
  };
  return () => es.close();
}

