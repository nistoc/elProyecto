/**
 * Latest job-root transcript for Refiner UI, aligned with Agent04 {@code TranscriptionMdOutputPath}:
 * {@code <stem>_yyyyMMdd_HHmmss_fff.md} (UTC) or legacy {@code <stem>.md}.
 */

/** Strip Agent04 UTC version suffix from stem so `transcript_20260323_173352_044` → `transcript`. */
const STEM_VERSION_SUFFIX_RE = /_\d{8}_\d{6}_\d{3}$/i;

export function stemFromMdOutputPath(mdOutputPath: string | null | undefined): string {
  const raw = mdOutputPath?.trim();
  if (!raw) return 'transcript';
  const base = raw.replace(/\\/g, '/').split('/').pop() ?? raw;
  let noExt = base.replace(/\.[^.]+$/, '');
  noExt = noExt.replace(STEM_VERSION_SUFFIX_RE, '');
  return noExt || 'transcript';
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** Refiner outputs — not a transcription final for stem selection. */
export function isRefinerTranscriptArtifactName(name: string): boolean {
  const n = name.toLowerCase();
  if (n === 'transcript_fixed.md') return true;
  return /^transcript_fixed_\d+\.md$/i.test(name);
}

/**
 * Picks the newest transcription markdown for {@code stem}: max versioned name lexicographically,
 * or {@code stem.md} if no versioned files exist.
 */
export function latestStemTranscriptFile<
  T extends { name: string; relativePath: string },
>(transcripts: T[], stem: string): T | null {
  const stemLower = stem.toLowerCase();
  const versionedRe = new RegExp(
    `^${escapeRegExp(stem)}_(\\d{8}_\\d{6}_\\d{3})\\.md$`,
    'i'
  );

  const versioned = transcripts.filter(
    (f) =>
      versionedRe.test(f.name) && !isRefinerTranscriptArtifactName(f.name)
  );
  if (versioned.length > 0) {
    versioned.sort((a, b) =>
      a.name.localeCompare(b.name, undefined, { sensitivity: 'base' })
    );
    return versioned[versioned.length - 1]!;
  }

  const plain = transcripts.find(
    (f) => f.name.toLowerCase() === `${stemLower}.md`
  );
  if (plain && !isRefinerTranscriptArtifactName(plain.name)) return plain;
  return null;
}
