import { describe, expect, it } from 'vitest';
import {
  JOBS_SORT_DEFAULT,
  formatJobsSort,
  lessonGroupHeads,
  nextJobsSort,
  parseStoredJobsSort,
  sortJobs,
} from './jobsSort';
import type { LessonJobInfo } from './lessons';

// All names here are made up.
function job(id: string, createdAt: string | null = null) {
  return { id, createdAt };
}

function info(
  date: string,
  startedAt: string,
  kind: LessonJobInfo['kind'] = 'mixed',
  label = ''
): LessonJobInfo {
  return { date, startedAt, time: startedAt.slice(11, 16), kind, label };
}

const lessons = new Map<string, LessonJobInfo>([
  ['oct-mixed', info('2026-10-23', '2026-10-23T20:21:45')],
  [
    'oct-maria',
    info('2026-10-23', '2026-10-23T20:21:45', 'participant', 'Maria Lopez'),
  ],
  [
    'oct-ana',
    info('2026-10-23', '2026-10-23T20:21:45', 'participant', 'Ana Ruiz'),
  ],
  [
    'oct-talk',
    info('2026-10-23', '2026-10-23T20:21:45', 'dialogue', 'Evening talk'),
  ],
  ['oct-early', info('2026-10-23', '2026-10-23T19:02:10')],
  ['nov-mixed', info('2026-11-13', '2026-11-13T19:03:00')],
]);

// Made in an order that has nothing to do with the lessons: the November lesson was run first.
const jobs = [
  job('oct-maria', '2026-11-16T10:00:00Z'),
  job('upload-old', '2026-02-03T09:00:00Z'),
  job('oct-talk', '2026-11-16T12:00:00Z'),
  job('nov-mixed', '2026-11-14T08:00:00Z'),
  job('oct-ana', '2026-11-16T10:05:00Z'),
  job('oct-early', '2026-11-15T07:00:00Z'),
  job('upload-new', '2026-11-17T09:00:00Z'),
  job('oct-mixed', '2026-11-15T08:00:00Z'),
];

function ids(list: { id: string }[]): string[] {
  return list.map((j) => j.id);
}

describe('sortJobs', () => {
  it('puts the newest lesson first and the jobs without a lesson last', () => {
    const sorted = sortJobs(jobs, { key: 'lesson', dir: 'desc' }, lessons);
    expect(ids(sorted)).toEqual([
      'nov-mixed',
      'oct-talk',
      'oct-mixed',
      'oct-ana',
      'oct-maria',
      'oct-early',
      'upload-new',
      'upload-old',
    ]);
  });

  it('turns the lessons and the recordings over, not the jobs of one recording', () => {
    const sorted = sortJobs(jobs, { key: 'lesson', dir: 'asc' }, lessons);
    expect(ids(sorted)).toEqual([
      'oct-early',
      'oct-talk',
      'oct-mixed',
      'oct-ana',
      'oct-maria',
      'nov-mixed',
      'upload-new',
      'upload-old',
    ]);
  });

  it('puts the newer of two runs of one track first', () => {
    const twice = new Map(lessons);
    twice.set('oct-mixed-again', lessons.get('oct-mixed')!);
    const sorted = sortJobs(
      [...jobs, job('oct-mixed-again', '2026-11-17T08:00:00Z')],
      { key: 'lesson', dir: 'asc' },
      twice
    );
    const at = ids(sorted).indexOf('oct-mixed-again');
    expect(ids(sorted)[at + 1]).toBe('oct-mixed');
  });

  it('orders by the day the job was made', () => {
    expect(
      ids(sortJobs(jobs, { key: 'created', dir: 'desc' }, lessons)).slice(0, 3)
    ).toEqual(['upload-new', 'oct-talk', 'oct-ana']);
    expect(
      ids(sortJobs(jobs, { key: 'created', dir: 'asc' }, null)).slice(0, 2)
    ).toEqual(['upload-old', 'nov-mixed']);
  });

  it('keeps the newest job first until the lessons list is read', () => {
    const sorted = sortJobs(jobs, { key: 'lesson', dir: 'asc' }, null);
    expect(ids(sorted)[0]).toBe('upload-new');
    expect(ids(sorted)[7]).toBe('upload-old');
  });

  it('does not change the list it was given', () => {
    const before = ids(jobs);
    sortJobs(jobs, JOBS_SORT_DEFAULT, lessons);
    expect(ids(jobs)).toEqual(before);
  });
});

describe('lessonGroupHeads', () => {
  it('marks the first job of every lesson with the day and the number of its jobs', () => {
    const sorted = sortJobs(jobs, { key: 'lesson', dir: 'desc' }, lessons);
    const heads = lessonGroupHeads(sorted, lessons);
    expect([...heads.entries()]).toEqual([
      ['nov-mixed', { date: '2026-11-13', count: 1 }],
      ['oct-talk', { date: '2026-10-23', count: 5 }],
      ['upload-new', { date: null, count: 2 }],
    ]);
  });

  it('has nothing to mark in an empty list', () => {
    expect(lessonGroupHeads([], lessons).size).toBe(0);
  });

  it('carries the pages made of a lesson to its heading', () => {
    const page = { url: 'https://pages.example.org/lesson/a1', title: 'v3' };
    const withPage = new Map(lessons);
    for (const [id, known] of lessons)
      if (known.date === '2026-10-23')
        withPage.set(id, { ...known, pages: [page] });
    const sorted = sortJobs(jobs, { key: 'lesson', dir: 'desc' }, withPage);

    const heads = lessonGroupHeads(sorted, withPage);

    expect(heads.get('oct-talk')).toEqual({
      date: '2026-10-23',
      count: 5,
      pages: [page],
    });
    expect(heads.get('nov-mixed')).toEqual({ date: '2026-11-13', count: 1 });
    expect(heads.get('upload-new')).toEqual({ date: null, count: 2 });
  });

  it('tells which lesson has a package the app itself shows', () => {
    const withPackage = new Map(lessons);
    for (const [id, known] of lessons)
      if (known.date === '2026-11-13')
        withPackage.set(id, { ...known, hasPackage: true });
    const sorted = sortJobs(jobs, { key: 'lesson', dir: 'desc' }, withPackage);

    const heads = lessonGroupHeads(sorted, withPackage);

    expect(heads.get('nov-mixed')).toEqual({
      date: '2026-11-13',
      count: 1,
      hasPackage: true,
    });
    expect(heads.get('oct-talk')).toEqual({ date: '2026-10-23', count: 5 });
  });

  it('tells which lesson day has its page', () => {
    const dayPage = { builtAt: '2026-11-14T10:00:00Z', stale: false };
    const withPage = new Map(lessons);
    for (const [id, known] of lessons)
      if (known.date === '2026-11-13') withPage.set(id, { ...known, dayPage });
    const sorted = sortJobs(jobs, { key: 'lesson', dir: 'desc' }, withPage);

    const heads = lessonGroupHeads(sorted, withPage);

    expect(heads.get('nov-mixed')).toEqual({
      date: '2026-11-13',
      count: 1,
      dayPage,
    });
    expect(heads.get('oct-talk')).toEqual({ date: '2026-10-23', count: 5 });
  });
});

describe('stored order', () => {
  it('is read back as it was written', () => {
    expect(parseStoredJobsSort(formatJobsSort(JOBS_SORT_DEFAULT))).toEqual(
      JOBS_SORT_DEFAULT
    );
    expect(parseStoredJobsSort(' created:asc ')).toEqual({
      key: 'created',
      dir: 'asc',
    });
  });

  it('is nothing when the stored value is not an order', () => {
    expect(parseStoredJobsSort(null)).toBeNull();
    expect(parseStoredJobsSort('')).toBeNull();
    expect(parseStoredJobsSort('name:desc')).toBeNull();
    expect(parseStoredJobsSort('lesson')).toBeNull();
  });
});

describe('nextJobsSort', () => {
  it('takes another order newest first and turns the same order over', () => {
    expect(nextJobsSort({ key: 'lesson', dir: 'asc' }, 'created')).toEqual({
      key: 'created',
      dir: 'desc',
    });
    expect(nextJobsSort({ key: 'lesson', dir: 'desc' }, 'lesson')).toEqual({
      key: 'lesson',
      dir: 'asc',
    });
    expect(nextJobsSort({ key: 'lesson', dir: 'asc' }, 'lesson')).toEqual({
      key: 'lesson',
      dir: 'desc',
    });
  });
});
