import type {
  ChunkVirtualModelEntry,
  JobProjectFile,
  JobProjectFiles,
  JobSnapshot,
} from '../types';

export interface ChunkArtifactGroup {
  /** 0-based chunk index in the pipeline / VM. */
  index: number;
  /** Human-readable stem from chunk audio/JSON filename (basename without extension). */
  displayStem: string;
  audioFiles: JobProjectFile[];
  jsonFiles: JobProjectFile[];
  vmRow: ChunkVirtualModelEntry | null;
}

/**
 * Try to read chunk ordinal from legacy names like `..._part_000.wav` / `...part_012...`.
 * This aligns better with real chunk indices than "first digits in filename" (which can be a year).
 */
export function inferChunkIndexFromName(fileName: string): number | null {
  const m =
    /\bpart_(\d+)\b/i.exec(fileName) ?? /_part_(\d+)/i.exec(fileName);
  if (!m) return null;
  const n = parseInt(m[1], 10);
  return Number.isFinite(n) ? n : null;
}

function stripExtension(name: string): string {
  const i = name.lastIndexOf('.');
  return i > 0 ? name.slice(0, i) : name;
}

/**
 * Which logical chunk row a file belongs to for Stats grouping.
 * If the filename has `part_NNN`, that wins over `f.index` (legacy/API often sets index from
 * the year in `2026-02-25_...`, producing a bogus chunk id like 2026).
 */
export function fileBelongsToChunkIndex(
  f: JobProjectFile,
  index: number,
  _total: number
): boolean {
  const inferred = inferChunkIndexFromName(f.name);
  if (inferred != null) return inferred === index;
  if (f.index != null) return f.index === index;
  return false;
}

/**
 * Indices to show as groups:
 * - If `job.chunks.total > 0`: always **0 .. total-1** only (do not build a dense range up to a bogus max from misparsed file indices).
 * - Else: **sorted unique** indices from VM + chunk files (no filling 0..max, so a stray `2026` does not allocate 2027 slots).
 */
export function computeChunkIndices(
  job: JobSnapshot,
  files: JobProjectFiles
): number[] {
  const total = job.chunks?.total ?? 0;
  const vm = job.chunks?.chunkVirtualModel;

  if (total > 0) {
    return Array.from({ length: total }, (_, i) => i);
  }

  const set = new Set<number>();
  const addIndexFromFile = (f: JobProjectFile) => {
    const inferred = inferChunkIndexFromName(f.name);
    if (inferred != null) set.add(inferred);
    else if (f.index != null) set.add(f.index);
  };
  for (const f of files.chunks) addIndexFromFile(f);
  for (const f of files.chunkJson) addIndexFromFile(f);
  if (vm) {
    for (const r of vm) set.add(r.index);
  }

  return [...set].sort((a, b) => a - b);
}

function computeDisplayStem(
  audioFiles: JobProjectFile[],
  jsonFiles: JobProjectFile[]
): string {
  const first = audioFiles[0] ?? jsonFiles[0];
  if (first) return stripExtension(first.name);
  return '';
}

/** True when every chunk audio has a JSON with the same basename (stem) — legacy completion signal. */
export function chunkArtifactsTranscriptionComplete(
  audioFiles: JobProjectFile[],
  jsonFiles: JobProjectFile[]
): boolean {
  if (audioFiles.length === 0 || jsonFiles.length === 0) return false;
  const jsonStems = new Set(jsonFiles.map((f) => stripExtension(f.name)));
  return audioFiles.every((a) => jsonStems.has(stripExtension(a.name)));
}

export function buildChunkGroups(
  job: JobSnapshot,
  files: JobProjectFiles
): ChunkArtifactGroup[] {
  const indices = computeChunkIndices(job, files);
  const vm = job.chunks?.chunkVirtualModel;
  const total = job.chunks?.total ?? 0;
  const vmByIndex = new Map<number, ChunkVirtualModelEntry>();
  if (vm) {
    for (const r of vm) vmByIndex.set(r.index, r);
  }

  return indices.map((index) => {
    const audioFiles = files.chunks.filter((f) =>
      fileBelongsToChunkIndex(f, index, total)
    );
    const jsonFiles = files.chunkJson.filter((f) =>
      fileBelongsToChunkIndex(f, index, total)
    );
    const base: Omit<ChunkArtifactGroup, 'displayStem'> = {
      index,
      audioFiles,
      jsonFiles,
      vmRow: vmByIndex.get(index) ?? null,
    };
    return {
      ...base,
      displayStem: computeDisplayStem(audioFiles, jsonFiles),
    };
  });
}
