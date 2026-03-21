import type {
  ChunkVirtualModelEntry,
  JobProjectFile,
  JobProjectFiles,
  JobSnapshot,
} from '../types';

export interface ChunkArtifactGroup {
  index: number;
  audioFiles: JobProjectFile[];
  jsonFiles: JobProjectFile[];
  vmRow: ChunkVirtualModelEntry | null;
}

/** Indices 0..max inclusive: from total, chunk files, chunk JSON, or VM rows. */
export function computeChunkIndices(
  job: JobSnapshot,
  files: JobProjectFiles
): number[] {
  const total = job.chunks?.total ?? 0;
  const vm = job.chunks?.chunkVirtualModel;
  let max = -1;
  if (total > 0) max = Math.max(max, total - 1);
  for (const f of files.chunks) {
    if (f.index != null) max = Math.max(max, f.index);
  }
  for (const f of files.chunkJson) {
    if (f.index != null) max = Math.max(max, f.index);
  }
  if (vm) {
    for (const r of vm) max = Math.max(max, r.index);
  }
  if (max < 0) return [];
  return Array.from({ length: max + 1 }, (_, i) => i);
}

export function buildChunkGroups(
  job: JobSnapshot,
  files: JobProjectFiles
): ChunkArtifactGroup[] {
  const indices = computeChunkIndices(job, files);
  const vm = job.chunks?.chunkVirtualModel;
  const vmByIndex = new Map<number, ChunkVirtualModelEntry>();
  if (vm) {
    for (const r of vm) vmByIndex.set(r.index, r);
  }
  return indices.map((index) => ({
    index,
    audioFiles: files.chunks.filter((f) => f.index === index),
    jsonFiles: files.chunkJson.filter((f) => f.index === index),
    vmRow: vmByIndex.get(index) ?? null,
  }));
}
