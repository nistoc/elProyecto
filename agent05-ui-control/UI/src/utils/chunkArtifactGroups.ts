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

/** Matches Agent04 `sub_chunk_XX_result.json` naming (two-digit index). */
export function subChunkGroupHasMergeResultJson(
  sc: SubChunkArtifactGroup
): boolean {
  if (sc.subIndex == null) return false;
  const needle = `sub_chunk_${String(sc.subIndex).padStart(2, '0')}_result.json`;
  const n = needle.toLowerCase();
  return sc.jsonFiles.some((f) => f.name.toLowerCase() === n);
}

/** True when every sub-chunk row has its paired result JSON (integrator-ready). */
export function chunkGroupAllSubResultsPresentForMerge(
  g: ChunkArtifactGroup
): boolean {
  if (!g.subChunks.length) return false;
  return g.subChunks.every(subChunkGroupHasMergeResultJson);
}

/** Mirrors Agent04 ChunkVirtualModelMerge.IsWeakPlaceholder for UI fallback. */
export function isWeakPlaceholderVm(row: ChunkVirtualModelEntry): boolean {
  const s = (row.state ?? '').trim();
  if (s.length !== 0 && s.toLowerCase() !== 'pending') return false;
  const started = (row.startedAt ?? '').trim();
  const completed = (row.completedAt ?? '').trim();
  if (started || completed) return false;
  return true;
}

function shouldPreferSnapshotVm(snapshot: ChunkVirtualModelEntry): boolean {
  const sl = (snapshot.state ?? '').trim().toLowerCase();
  if (sl === 'completed' || sl === 'failed' || sl === 'cancelled') return true;
  if (sl === 'running') return true;
  if ((snapshot.startedAt ?? '').trim() || (snapshot.completedAt ?? '').trim()) return true;
  if ((snapshot.transcriptActivityLog ?? '').trim()) return true;
  return false;
}

/**
 * Prefer server VM when it carries real telemetry; if the server sent a Pending shell without dates,
 * use the snapshot row when it is more informative (same policy as Agent04 merge).
 */
export function mergeVmRowPreferInformative(
  serverRow: ChunkVirtualModelEntry | null | undefined,
  snapshotRow: ChunkVirtualModelEntry | null | undefined
): ChunkVirtualModelEntry | null {
  const srv = serverRow ?? null;
  const snap = snapshotRow ?? null;
  if (!srv) return snap;
  if (!snap) return srv;
  if (isWeakPlaceholderVm(srv) && shouldPreferSnapshotVm(snap)) return snap;
  return srv;
}

/**
 * Fills vmRow from the job snapshot where the API omitted it or sent a weak Pending placeholder.
 * Strong server rows (Rentgen / merged Agent04) win.
 */
export function overlayVmFromJobWhenMissing(
  groups: ChunkArtifactGroup[],
  job: JobSnapshot
): ChunkArtifactGroup[] {
  const vm = job.chunks?.chunkVirtualModel;
  return groups.map((g) => ({
    ...g,
    vmRow: mergeVmRowPreferInformative(g.vmRow, findMainChunkVmRow(vm, g.index)),
    subChunks: (g.subChunks ?? []).map((sc) => ({
      ...sc,
      vmRow: mergeVmRowPreferInformative(
        sc.vmRow,
        findSubChunkVmRow(vm, g.index, sc.subIndex ?? null)
      ),
    })),
  }));
}
