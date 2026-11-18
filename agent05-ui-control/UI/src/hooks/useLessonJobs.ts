import { useEffect, useState } from 'react';
import { fetchLessons } from '../api';
import { indexLessonJobs, type LessonJobInfo } from '../utils/lessons';

/**
 * What the lessons list knows of each job (lesson, recording, track or dialogue, model, share of the
 * languages), by job id. Read only while `enabled` — the jobs list asks for it when it is wide enough to
 * show it — and read again when `signature` changes (a job appeared or moved to another phase).
 * Null until the first answer; a failed read leaves what was read before.
 */
export function useLessonJobs(
  enabled: boolean,
  signature: string
): Map<string, LessonJobInfo> | null {
  const [index, setIndex] = useState<Map<string, LessonJobInfo> | null>(null);

  useEffect(() => {
    if (!enabled) return;
    let cancelled = false;
    fetchLessons()
      .then((response) => {
        if (!cancelled) setIndex(indexLessonJobs(response));
      })
      .catch(() => {
        // The jobs list stays usable without it: it only tells less about each job.
      });
    return () => {
      cancelled = true;
    };
  }, [enabled, signature]);

  return index;
}
