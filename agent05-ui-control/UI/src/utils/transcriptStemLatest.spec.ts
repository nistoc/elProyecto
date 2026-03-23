import { describe, expect, it } from 'vitest';
import {
  isRefinerTranscriptArtifactName,
  latestStemTranscriptFile,
  stemFromMdOutputPath,
} from './transcriptStemLatest';

describe('stemFromMdOutputPath', () => {
  it('defaults to transcript', () => {
    expect(stemFromMdOutputPath(null)).toBe('transcript');
    expect(stemFromMdOutputPath('')).toBe('transcript');
  });

  it('uses basename without extension', () => {
    expect(stemFromMdOutputPath('transcript.md')).toBe('transcript');
    expect(stemFromMdOutputPath('outputs/foo.md')).toBe('foo');
    expect(stemFromMdOutputPath('x\\y\\bar.md')).toBe('bar');
  });

  it('strips UTC version suffix from basename so stem matches versioned files', () => {
    expect(
      stemFromMdOutputPath('transcript_20260323_173352_044.md')
    ).toBe('transcript');
    expect(
      stemFromMdOutputPath('out/transcript_20260323_173352_044.md')
    ).toBe('transcript');
  });
});

describe('latestStemTranscriptFile', () => {
  const mk = (name: string) => ({ name, relativePath: name });

  it('picks max versioned name', () => {
    const files = [
      mk('transcript_20250101_120000_000.md'),
      mk('transcript_20250202_100000_000.md'),
      mk('response.json'),
    ];
    expect(latestStemTranscriptFile(files, 'transcript')?.name).toBe(
      'transcript_20250202_100000_000.md'
    );
  });

  it('falls back to stem.md when no versioned', () => {
    const files = [mk('transcript.md'), mk('response.json')];
    expect(latestStemTranscriptFile(files, 'transcript')?.name).toBe(
      'transcript.md'
    );
  });

  it('prefers versioned over plain stem.md', () => {
    const files = [
      mk('transcript.md'),
      mk('transcript_20250101_120000_000.md'),
    ];
    expect(latestStemTranscriptFile(files, 'transcript')?.name).toBe(
      'transcript_20250101_120000_000.md'
    );
  });

  it('ignores refiner fixed outputs', () => {
    const files = [
      mk('transcript_fixed.md'),
      mk('transcript_20250101_120000_000.md'),
    ];
    expect(latestStemTranscriptFile(files, 'transcript')?.name).toBe(
      'transcript_20250101_120000_000.md'
    );
  });

  it('respects custom stem', () => {
    const files = [mk('foo_20250101_120000_000.md'), mk('foo.md')];
    expect(latestStemTranscriptFile(files, 'foo')?.name).toBe(
      'foo_20250101_120000_000.md'
    );
  });
});

describe('isRefinerTranscriptArtifactName', () => {
  it('detects fixed variants', () => {
    expect(isRefinerTranscriptArtifactName('transcript_fixed.md')).toBe(true);
    expect(isRefinerTranscriptArtifactName('transcript_fixed_12.md')).toBe(
      true
    );
    expect(isRefinerTranscriptArtifactName('transcript.md')).toBe(false);
  });
});
