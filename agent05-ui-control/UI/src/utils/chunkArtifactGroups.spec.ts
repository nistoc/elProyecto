import { describe, expect, it } from 'vitest';
import type { ChunkVirtualModelEntry, JobProjectFile, JobSnapshot } from '../types';
import {
  type ChunkArtifactGroup,
  isWeakPlaceholderVm,
  mergeVmRowPreferInformative,
  overlayVmFromJobWhenMissing,
  splitChunksForProjectFilesOrphansOnly,
} from './chunkArtifactGroups';

function vm(partial: Partial<ChunkVirtualModelEntry> & Pick<ChunkVirtualModelEntry, 'index' | 'state'>): ChunkVirtualModelEntry {
  return {
    index: partial.index,
    state: partial.state,
    startedAt: partial.startedAt ?? null,
    completedAt: partial.completedAt ?? null,
    errorMessage: partial.errorMessage ?? null,
    isSubChunk: partial.isSubChunk ?? null,
    parentChunkIndex: partial.parentChunkIndex ?? null,
    subChunkIndex: partial.subChunkIndex ?? null,
    transcriptActivityLog: partial.transcriptActivityLog ?? null,
  };
}

describe('isWeakPlaceholderVm', () => {
  it('treats Pending without dates as weak', () => {
    expect(isWeakPlaceholderVm(vm({ index: 0, state: 'Pending' }))).toBe(true);
    expect(isWeakPlaceholderVm(vm({ index: 0, state: '' }))).toBe(true);
  });

  it('treats Running as strong', () => {
    expect(isWeakPlaceholderVm(vm({ index: 0, state: 'Running' }))).toBe(false);
  });
});

describe('mergeVmRowPreferInformative', () => {
  it('replaces weak server row with snapshot that has timings', () => {
    const server = vm({ index: 0, state: 'Pending' });
    const snap = vm({
      index: 0,
      state: 'Completed',
      startedAt: '2020-01-01T00:00:00Z',
      completedAt: '2020-01-01T00:01:00Z',
    });
    expect(mergeVmRowPreferInformative(server, snap)).toBe(snap);
  });

  it('keeps strong server row over snapshot', () => {
    const server = vm({
      index: 0,
      state: 'Running',
      startedAt: '2020-01-02T00:00:00Z',
    });
    const snap = vm({
      index: 0,
      state: 'Completed',
      startedAt: '2020-01-01T00:00:00Z',
      completedAt: '2020-01-01T00:01:00Z',
    });
    expect(mergeVmRowPreferInformative(server, snap)).toBe(server);
  });
});

function pf(partial: Partial<JobProjectFile> & Pick<JobProjectFile, 'name' | 'relativePath'>): JobProjectFile {
  return {
    name: partial.name,
    relativePath: partial.relativePath,
    kind: partial.kind ?? 'audio',
    sizeBytes: partial.sizeBytes ?? 0,
    parentIndex: partial.parentIndex ?? null,
    subIndex: partial.subIndex ?? null,
    isTranscript: partial.isTranscript ?? false,
    hasTranscript: partial.hasTranscript ?? false,
    index: partial.index ?? null,
    lineCount: partial.lineCount ?? null,
    durationSeconds: partial.durationSeconds ?? null,
  };
}

describe('splitChunksForProjectFilesOrphansOnly', () => {
  it('returns full list when groups are missing', () => {
    const split = [pf({ name: 'a.wav', relativePath: 'split_chunks/chunk_0/sub_chunks/a.wav', parentIndex: 0 })];
    expect(splitChunksForProjectFilesOrphansOnly(split, null)).toBe(split);
    expect(splitChunksForProjectFilesOrphansOnly(split, undefined)).toBe(split);
    expect(splitChunksForProjectFilesOrphansOnly(split, [])).toBe(split);
  });

  it('drops split rows whose parent index has a chunk group', () => {
    const split = [
      pf({ name: 'a.wav', relativePath: 's/c0/a.wav', parentIndex: 0 }),
      pf({ name: 'b.wav', relativePath: 's/c9/b.wav', parentIndex: 9 }),
    ];
    const groups: ChunkArtifactGroup[] = [
      {
        index: 0,
        displayStem: 'x',
        audioFiles: [],
        jsonFiles: [],
        subChunks: [],
        mergedSplitFiles: [],
        vmRow: null,
      },
    ];
    const out = splitChunksForProjectFilesOrphansOnly(split, groups);
    expect(out).toHaveLength(1);
    expect(out[0].parentIndex).toBe(9);
  });

  it('keeps rows without parentIndex when groups exist', () => {
    const split = [pf({ name: 'x.json', relativePath: 'split_chunks/x.json', parentIndex: null })];
    const groups: ChunkArtifactGroup[] = [
      {
        index: 0,
        displayStem: 'x',
        audioFiles: [],
        jsonFiles: [],
        subChunks: [],
        mergedSplitFiles: [],
        vmRow: null,
      },
    ];
    expect(splitChunksForProjectFilesOrphansOnly(split, groups)).toEqual(split);
  });
});

describe('overlayVmFromJobWhenMissing', () => {
  it('fills weak main vmRow from job snapshot', () => {
    const groups: ChunkArtifactGroup[] = [
      {
        index: 0,
        displayStem: 'a',
        audioFiles: [],
        jsonFiles: [],
        subChunks: [],
        mergedSplitFiles: [],
        vmRow: vm({ index: 0, state: 'Pending' }),
      },
    ];
    const job = {
      id: 'j1',
      status: 'done',
      phase: 'completed',
      logs: [],
      chunks: {
        total: 1,
        active: [],
        completed: [],
        cancelled: [],
        failed: [],
        chunkVirtualModel: [
          vm({
            index: 0,
            state: 'Completed',
            startedAt: '2020-01-01T00:00:00Z',
            completedAt: '2020-01-01T00:05:00Z',
          }),
        ],
      },
    };
    const out = overlayVmFromJobWhenMissing(groups, job as JobSnapshot);
    expect(out[0].vmRow?.startedAt).toBe('2020-01-01T00:00:00Z');
    expect(out[0].vmRow?.state).toBe('Completed');
  });
});
