import type { ProjectFiles } from "../api";

type CachedFiles = {
  files: ProjectFiles;
  jobDir: string;
  timestamp: number;
};

// In-memory cache for project files
const filesCache = new Map<string, CachedFiles>();

// Cache TTL: 5 minutes (can be invalidated manually)
const CACHE_TTL = 5 * 60 * 1000;

/**
 * Get cached files for a job, if available and not expired.
 */
export function getCachedFiles(jobId: string): { files: ProjectFiles; jobDir: string } | null {
  const cached = filesCache.get(jobId);
  if (!cached) return null;
  
  // Check if cache is expired
  const age = Date.now() - cached.timestamp;
  if (age > CACHE_TTL) {
    filesCache.delete(jobId);
    return null;
  }
  
  return {
    files: cached.files,
    jobDir: cached.jobDir,
  };
}

/**
 * Cache files for a job.
 */
export function setCachedFiles(jobId: string, files: ProjectFiles, jobDir: string): void {
  filesCache.set(jobId, {
    files,
    jobDir,
    timestamp: Date.now(),
  });
}

/**
 * Invalidate cache for a specific job.
 */
export function invalidateCache(jobId: string): void {
  filesCache.delete(jobId);
}

/**
 * Invalidate cache for all jobs.
 */
export function invalidateAllCache(): void {
  filesCache.clear();
}

/**
 * Invalidate cache when files are modified (e.g., after saving).
 */
export function invalidateOnFileChange(jobId: string): void {
  invalidateCache(jobId);
}
