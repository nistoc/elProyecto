import type {
  ChunkVirtualModelEntry,
  JobProjectFile,
  JobProjectFiles,
  JobSnapshot,
} from '../types';

/** One operator split child under `split_chunks/chunk_N/` (API: `parentIndex` / `subIndex`). */
export interface SubChunkArtifactGroup {
  /** 0-based sub-chunk index within the parent; `null` if the scanner could not parse it. */
  subIndex: number | null;
  displayStem: string;
  audioFiles: JobProjectFile[];
  jsonFiles: JobProjectFile[];
  /** Agent04 VM row when present (isSubChunk + matching indices). */
  vmRow: ChunkVirtualModelEntry | null;
}

export interface ChunkArtifactGroup {
  /** 0-based chunk index in the pipeline / VM. */
  index: number;
  /** Human-readable stem from chunk audio/JSON filename (basename without extension). */
  displayStem: string;
  audioFiles: JobProjectFile[];
  jsonFiles: JobProjectFile[];
  /** Files under `split_chunks/chunk_{index}/`, grouped by `subIndex`. */
  subChunks: SubChunkArtifactGroup[];
  /** `chunk_{index}_merged.json` / `.md` at split chunk folder root (agent01-style). */
  mergedSplitFiles: JobProjectFile[];
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
 * - Else: **sorted unique** indices from VM + chunk files + **parentIndex** from `splitChunks`
 *   (narrow archives: only `split_chunks/` and empty `chunks/`).
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
  for (const f of files.splitChunks) {
    if (f.parentIndex != null) set.add(f.parentIndex);
  }

  return [...set].sort((a, b) => a - b);
}

/** Merged split output at `split_chunks/chunk_N/chunk_N_merged.{json,md}`. */
export function isSplitParentMergedArtifact(
  f: JobProjectFile,
  parentIndex: number
): boolean {
  if (f.parentIndex !== parentIndex) return false;
  const m = /^chunk_(\d+)_merged\.(json|md)$/i.exec(f.name);
  return m != null && parseInt(m[1], 10) === parentIndex;
}

function computeDisplayStem(
  audioFiles: JobProjectFile[],
  jsonFiles: JobProjectFile[]
): string {
  const first = audioFiles[0] ?? jsonFiles[0];
  if (first) return stripExtension(first.name);
  return '';
}

function subChunkGroupSortKey(subIndex: number | null): number {
  return subIndex == null ? Number.POSITIVE_INFINITY : subIndex;
}

export function findMainChunkVmRow(
  vm: ChunkVirtualModelEntry[] | null | undefined,
  index: number
): ChunkVirtualModelEntry | null {
  if (!vm?.length) return null;
  const row = vm.find((r) => r.isSubChunk !== true && r.index === index);
  return row ?? null;
}

export function findSubChunkVmRow(
  vm: ChunkVirtualModelEntry[] | null | undefined,
  parentIndex: number,
  subIndex: number | null
): ChunkVirtualModelEntry | null {
  if (!vm?.length || subIndex == null) return null;
  return (
    vm.find(
      (r) =>
        r.isSubChunk === true &&
        r.parentChunkIndex === parentIndex &&
        r.subChunkIndex === subIndex
    ) ?? null
  );
}

/**
 * Partition `files.splitChunks` for a single parent chunk index (scanner sets `parentIndex` / `subIndex`).
 */
export function buildSubChunkGroups(
  splitChunks: JobProjectFile[],
  parentIndex: number,
  vm: ChunkVirtualModelEntry[] | null | undefined
): SubChunkArtifactGroup[] {
  const forParent = splitChunks.filter((f) => f.parentIndex === parentIndex);
  if (forParent.length === 0) return [];

  const byKey = new Map<
    string,
    { subIndex: number | null; bucket: JobProjectFile[] }
  >();
  for (const f of forParent) {
    const subIndex = f.subIndex ?? null;
    const key = subIndex != null ? `i:${subIndex}` : 'i:null';
    let row = byKey.get(key);
    if (!row) {
      row = { subIndex, bucket: [] };
      byKey.set(key, row);
    }
    row.bucket.push(f);
  }

  const out: SubChunkArtifactGroup[] = [];
  for (const { subIndex, bucket } of byKey.values()) {
    const audioFiles = bucket.filter((f) => f.kind === 'audio');
    const jsonFiles = bucket.filter((f) => f.kind === 'text');
    if (audioFiles.length === 0 && jsonFiles.length === 0) continue;
    out.push({
      subIndex,
      audioFiles,
      jsonFiles,
      displayStem: computeDisplayStem(audioFiles, jsonFiles),
      vmRow: findSubChunkVmRow(vm, parentIndex, subIndex),
    });
  }

  out.sort(
    (a, b) => subChunkGroupSortKey(a.subIndex) - subChunkGroupSortKey(b.subIndex)
  );
  return out;
}

/** True when every chunk audio has a JSON with the same basename (stem) — legacy completion signal. */
/** Any `split_chunks/` file row for this parent (includes merged `chunk_N_merged.*`). */
export function chunkHasSplitArtifacts(
  files: JobProjectFiles | null | undefined,
  chunkIndex: number
): boolean {
  const list = files?.splitChunks;
  if (!list?.length) return false;
  return list.some((f) => f.parentIndex === chunkIndex);
}

/**
 * True when operator-split **blocking** artifacts exist for Retranscribe / server guard:
 * sub-chunk files under `split_chunks/chunk_N/` excluding root-level `chunk_N_merged.*`.
 * Matches agent05 split-presence check (sub_chunks files or `sub_chunk_*_result.json`).
 * Leftover merged JSON/MD alone does **not** block — those are not removed by "delete sub-chunk".
 */
export function chunkHasBlockingSplitArtifacts(
  files: JobProjectFiles | null | undefined,
  chunkIndex: number
): boolean {
  const list = files?.splitChunks;
  if (!list?.length) return false;
  return list.some(
    (f) =>
      f.parentIndex === chunkIndex &&
      !isSplitParentMergedArtifact(f, chunkIndex)
  );
}

export function chunkArtifactsTranscriptionComplete(
  audioFiles: JobProjectFile[],
  jsonFiles: JobProjectFile[]
): boolean {
  if (audioFiles.length === 0 || jsonFiles.length === 0) return false;
  const jsonStems = new Set(jsonFiles.map((f) => stripExtension(f.name)));
  return audioFiles.every((a) => jsonStems.has(stripExtension(a.name)));
}

/**
 * Fills vmRow from the job snapshot only where the API omitted it (e.g. Rentgen node missing).
 * When Agent04 sends main_virtual_model / sub_virtual_model, those win.
 */
export function overlayVmFromJobWhenMissing(
  groups: ChunkArtifactGroup[],
  job: JobSnapshot
): ChunkArtifactGroup[] {
  const vm = job.chunks?.chunkVirtualModel;
  return groups.map((g) => ({
    ...g,
    vmRow: g.vmRow ?? findMainChunkVmRow(vm, g.index),
    subChunks: (g.subChunks ?? []).map((sc) => ({
      ...sc,
      vmRow: sc.vmRow ?? findSubChunkVmRow(vm, g.index, sc.subIndex ?? null),
    })),
  }));
}

export function buildChunkGroups(
  job: JobSnapshot,
  files: JobProjectFiles
): ChunkArtifactGroup[] {
  const indices = computeChunkIndices(job, files);
  const vm = job.chunks?.chunkVirtualModel;
  const total = job.chunks?.total ?? 0;

  return indices.map((index) => {
    const audioFiles = files.chunks.filter((f) =>
      fileBelongsToChunkIndex(f, index, total)
    );
    const jsonFiles = files.chunkJson.filter((f) =>
      fileBelongsToChunkIndex(f, index, total)
    );
    const splitForParent = files.splitChunks.filter(
      (f) => f.parentIndex === index
    );
    const mergedSplitFiles = splitForParent.filter((f) =>
      isSplitParentMergedArtifact(f, index)
    );
    const splitForSubRows = splitForParent.filter(
      (f) => !isSplitParentMergedArtifact(f, index)
    );
    const subChunks = buildSubChunkGroups(splitForSubRows, index, vm);
    const base: Omit<ChunkArtifactGroup, 'displayStem'> = {
      index,
      audioFiles,
      jsonFiles,
      subChunks,
      mergedSplitFiles,
      vmRow: findMainChunkVmRow(vm, index),
    };
    return {
      ...base,
      displayStem: computeDisplayStem(audioFiles, jsonFiles),
    };
  });
}
