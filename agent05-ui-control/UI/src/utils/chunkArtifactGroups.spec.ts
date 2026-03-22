import { describe, expect, it } from 'vitest';
import type { ChunkVirtualModelEntry, JobSnapshot } from '../types';
import {
  type ChunkArtifactGroup,
  isWeakPlaceholderVm,
  mergeVmRowPreferInformative,
  overlayVmFromJobWhenMissing,
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
