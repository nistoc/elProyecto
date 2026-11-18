import type { LessonJobInfo, LessonPage } from './lessons';

/** What the jobs list is ordered by: the lesson a job belongs to, or the day the job itself was made. */
export type JobsSortKey = 'lesson' | 'created';
export type JobsSortDir = 'desc' | 'asc';

export interface JobsSort {
  key: JobsSortKey;
  dir: JobsSortDir;
}

export const JOBS_SORT_KEY = 'xtract-jobs-sort';
export const JOBS_SORT_DEFAULT: JobsSort = { key: 'lesson', dir: 'desc' };

/** "lesson:desc" — what the browser keeps between visits. */
export function formatJobsSort(sort: JobsSort): string {
  return `${sort.key}:${sort.dir}`;
}

export function parseStoredJobsSort(
  raw: string | null | undefined
): JobsSort | null {
  const m = /^(lesson|created):(desc|asc)$/.exec((raw ?? '').trim());
  return m ? { key: m[1] as JobsSortKey, dir: m[2] as JobsSortDir } : null;
}

/** A click on the name of an order: that order, newest first; the other direction when it is the order already. */
export function nextJobsSort(current: JobsSort, key: JobsSortKey): JobsSort {
  if (current.key !== key) return { key, dir: 'desc' };
  return { key, dir: current.dir === 'desc' ? 'asc' : 'desc' };
}

interface SortableJob {
  id: string;
  createdAt?: string | null;
}

/** Inside one recording: what was made of all tracks first, then the tracks. */
const KIND_ORDER: Record<LessonJobInfo['kind'], number> = {
  dialogue: 0,
  mixed: 1,
  participant: 2,
};

function newestFirst(a: SortableJob, b: SortableJob): number {
  return (b.createdAt ?? '').localeCompare(a.createdAt ?? '');
}

/**
 * The jobs in the order the list shows them.
 *
 * By lesson: the day of the lesson, then the start of the recording inside it — both in the direction
 * asked for. The jobs of one recording always stand the same way: the dialogue, the mixed track, the
 * participants by name, and of two runs of one track the newer first. A job the lessons list does not
 * know stands after all lessons in either direction, newer first.
 *
 * Until the lessons list is read (`lessonJobs` is null) the order by lesson cannot be told and the
 * jobs stand newest first.
 */
export function sortJobs<T extends SortableJob>(
  jobs: readonly T[],
  sort: JobsSort,
  lessonJobs: ReadonlyMap<string, LessonJobInfo> | null | undefined
): T[] {
  const sign = sort.dir === 'desc' ? -1 : 1;
  if (sort.key === 'created') {
    return [...jobs].sort((a, b) => -sign * newestFirst(a, b));
  }
  if (!lessonJobs) return [...jobs].sort(newestFirst);
  return [...jobs].sort((a, b) => {
    const la = lessonJobs.get(a.id);
    const lb = lessonJobs.get(b.id);
    if (!la || !lb) {
      if (la) return -1;
      if (lb) return 1;
      return newestFirst(a, b);
    }
    return (
      sign * la.date.localeCompare(lb.date) ||
      sign * la.startedAt.localeCompare(lb.startedAt) ||
      sign * la.time.localeCompare(lb.time) ||
      KIND_ORDER[la.kind] - KIND_ORDER[lb.kind] ||
      la.label.localeCompare(lb.label) ||
      newestFirst(a, b)
    );
  });
}

export interface JobsLessonGroup {
  /** YYYY-MM-DD of the lesson; null for the jobs the lessons list does not know. */
  date: string | null;
  count: number;
  /** Pages made of the lesson (the lesson package); absent when it has none. */
  pages?: LessonPage[];
}

/**
 * Where a new lesson begins in a list ordered by lesson: the id of the first job of every lesson, with
 * the day of the lesson, the number of its jobs and the pages made of it. The list puts a heading above
 * such a job.
 */
export function lessonGroupHeads(
  jobs: readonly SortableJob[],
  lessonJobs: ReadonlyMap<string, LessonJobInfo>
): Map<string, JobsLessonGroup> {
  const heads = new Map<string, JobsLessonGroup>();
  let current: JobsLessonGroup | null = null;
  for (const job of jobs) {
    const info = lessonJobs.get(job.id);
    const date = info?.date ?? null;
    if (!current || current.date !== date) {
      current = { date, count: 0 };
      if (info?.pages?.length) current.pages = info.pages;
      heads.set(job.id, current);
    }
    current.count += 1;
  }
  return heads;
}
