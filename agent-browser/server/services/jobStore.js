/**
 * In-memory job store.
 * Can be replaced with Redis/DB for production.
 */
const jobs = new Map();

export function createJob(job) {
  jobs.set(job.id, job);
  return job;
}

export function getJob(jobId) {
  return jobs.get(jobId) || null;
}

export function updateJob(jobId, patch) {
  const job = jobs.get(jobId);
  if (!job) return null;
  Object.assign(job, patch);
  return job;
}

export function deleteJob(jobId) {
  return jobs.delete(jobId);
}

export function getAllJobs() {
  return Array.from(jobs.values());
}

export function jobExists(jobId) {
  return jobs.has(jobId);
}

