import type {
  ChunkVirtualModelEntry,
  JobProjectFile,
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

function stripExtension(name: string): string {
  const i = name.lastIndexOf('.');
  return i > 0 ? name.slice(0, i) : name;
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

/** When groups come from Agent04, split blocking matches scanner: any sub-chunk row (not merged-only at chunk root). */
export function chunkGroupHasBlockingSplitArtifacts(g: ChunkArtifactGroup): boolean {
  return g.subChunks.some(
    (sc) => sc.audioFiles.length > 0 || sc.jsonFiles.length > 0
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
