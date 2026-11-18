/**
 * The package of a lesson as GET /api/sessions/{date}/package gives it: the lists the builder made
 * (topics, grammar, examples, words), with the owner's edits put on and every word counted against the
 * lessons before this one. The helpers here turn the three word lists into the rows of one table.
 */

/** What every item of a list has for the owner's edits. */
export interface PackageItem {
  /** The id the edits of the item are kept by. */
  id: string;
  /** Fields the owner wrote anew: name of the field — what the builder had there. */
  edited?: Record<string, string | null> | null;
  /** The owner took the item out of the lists. */
  leftOut?: boolean;
  editedAt?: string | null;
}

export interface PackagePlace extends PackageItem {
  period: number;
  /** "12:34" in the recording of the period; null when the quote was not found. */
  at?: string | null;
  sec?: number | null;
  /** teacher | student | '' */
  who?: string | null;
  line?: number | null;
  quote?: string | null;
  /** The line of the transcript around the quote. */
  said?: string | null;
  check?: string | null;
}

export interface PackagePeriod {
  n: number;
  job?: string | null;
  /** "19:03" — local time the recording started. */
  start?: string | null;
  lines: number;
  endAt?: string | null;
  /** The mixed track of the recording, when it has one: what a phrase is played from. */
  audioTrack?: string | null;
}

export interface PackageTopic extends PackageItem {
  period: number;
  title?: string | null;
  summary?: string | null;
  fromAt?: string | null;
  toAt?: string | null;
  fromSec?: number | null;
  toSec?: number | null;
}

export interface PackageGrammarPart {
  topic?: string | null;
  rule?: string | null;
  examples: PackagePlace[];
}

export interface PackageGrammar extends PackageItem {
  topic?: string | null;
  theme?: string | null;
  rule?: string | null;
  /** explained | corrected | asked */
  kind?: string | null;
  period: number;
  points: number;
  examples: PackagePlace[];
  parts: PackageGrammarPart[];
}

export interface PackageExample extends PackagePlace {
  ru?: string | null;
  shows?: string | null;
}

export interface PackageWord extends PackagePlace {
  es?: string | null;
  ru?: string | null;
  /** New words: explained | asked | corrected | prompted | introduced. */
  how?: string | null;
  /** Vocabulary: noun | verb | adjective | adverb | expression. */
  pos?: string | null;
  heard?: string | null;
  note?: string | null;
  /** Dates of the earlier lessons that have the word, oldest first; null — a pattern, nothing to look for. */
  seenIn?: string[] | null;
}

export interface PackageOtherWord extends PackageItem {
  word?: string | null;
  raw?: string | null;
  count: number;
  period: number;
  at?: string | null;
  sec?: number | null;
  who?: string | null;
  said?: string | null;
  /** spanish | russian | maybe_russian | other | broken | '' (not looked at). */
  kind?: string | null;
  lemma?: string | null;
  ru?: string | null;
  sure?: boolean;
  seenIn?: string[] | null;
}

export interface LessonPackageDocument {
  lesson?: string | null;
  builtAt?: string | null;
  builder?: string | null;
  requests?: number | null;
  costUsd?: number | null;
  periods: PackagePeriod[];
  topics: PackageTopic[];
  grammar: PackageGrammar[];
  wordNotes: PackageGrammar[];
  examples: PackageExample[];
  newWords: PackageWord[];
  vocabulary: PackageWord[];
  otherWords: PackageOtherWord[];
}

export interface LessonPackageView {
  /** YYYY-MM-DD of the lesson. */
  date: string;
  package: LessonPackageDocument;
  /** The earlier lessons the words were compared with. */
  compared: {
    lessons: number;
    from?: string | null;
    to?: string | null;
    dates: string[];
  };
  counts: {
    newWords: number;
    newWordsNeverSeen: number;
    vocabulary: number;
    vocabularyNeverSeen: number;
    otherWords: number;
    otherWordsNeverSeen: number;
    edited: number;
    leftOut: number;
    /** Edits whose item the package no longer has. */
    stale: number;
  };
}

/** Which of the lists a row of the words table came from. */
export type WordList = 'new' | 'vocabulary' | 'code' | 'codeOther';

export const WORD_LISTS: readonly WordList[] = [
  'new',
  'vocabulary',
  'code',
  'codeOther',
];

/** One row of the words table, whichever list the word is in. */
export interface WordRow {
  id: string;
  list: WordList;
  word: string;
  /** The field of the item the word stands in: what an edit of the word writes. */
  wordField: 'es' | 'raw';
  ru: string;
  /** how / pos / kind — the builder's own word for what the row is; shown through the translations. */
  tag: string;
  note: string;
  /** Dictionary form of a word found by code. */
  lemma: string;
  /** What stands in the line when it is not the right form. */
  heard: string;
  period: number;
  at: string;
  sec: number | null;
  who: string;
  /** The words shown as the example of the row. */
  phrase: string;
  phraseField: 'quote' | 'said';
  /** The whole line of the transcript, for the hint. */
  line: string;
  /** How many times the form was said (words found by code); 0 when not counted. */
  count: number;
  /** Earlier lessons that have the word, oldest first; null — nothing to look for. */
  seenIn: string[] | null;
  leftOut: boolean;
  /** Field — what the builder had there. */
  edited: Record<string, string | null>;
}

const hasLatin = (s: string) => /[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]/.test(s);

/** Every word of the package as rows of one table: new words, vocabulary, then the words found by code. */
export function wordRows(pkg: LessonPackageDocument): WordRow[] {
  const rows: WordRow[] = [];
  const ofList = (list: 'new' | 'vocabulary', words: PackageWord[]) => {
    for (const w of words ?? []) {
      rows.push({
        id: w.id,
        list,
        word: w.es ?? '',
        wordField: 'es',
        ru: w.ru ?? '',
        tag: (list === 'new' ? w.how : w.pos) ?? '',
        note: w.note ?? '',
        lemma: '',
        heard: w.heard ?? '',
        period: w.period,
        at: w.at ?? '',
        sec: w.sec ?? null,
        who: w.who ?? '',
        phrase: w.quote ?? '',
        phraseField: 'quote',
        line: w.said ?? '',
        count: 0,
        seenIn: w.seenIn ?? null,
        leftOut: w.leftOut === true,
        edited: w.edited ?? {},
      });
    }
  };
  ofList('new', pkg.newWords);
  ofList('vocabulary', pkg.vocabulary);
  for (const w of pkg.otherWords ?? []) {
    const word = w.raw ?? w.word ?? '';
    rows.push({
      id: w.id,
      // A word the owner wrote in other letters (a Russian word heard as Latin) is not a Spanish one.
      list: w.kind === 'spanish' && hasLatin(word) ? 'code' : 'codeOther',
      word,
      wordField: 'raw',
      ru: w.ru ?? '',
      tag: w.kind ?? '',
      note: '',
      lemma: w.lemma ?? '',
      heard: '',
      period: w.period,
      at: w.at ?? '',
      sec: w.sec ?? null,
      who: w.who ?? '',
      phrase: w.said ?? '',
      phraseField: 'said',
      line: w.said ?? '',
      count: w.count ?? 0,
      seenIn: w.seenIn ?? null,
      leftOut: w.leftOut === true,
      edited: w.edited ?? {},
    });
  }
  return rows;
}

/** No lesson before this one has the word. A pattern with nothing to look for is not called new. */
export function isNewWord(row: WordRow): boolean {
  return row.seenIn !== null && row.seenIn.length === 0;
}

export interface WordFilter {
  /** Only the words no earlier lesson has. */
  onlyNew: boolean;
  lists: ReadonlySet<WordList>;
  /** Looked for in the word, its translation, note and phrase; case and accents do not matter. */
  query: string;
  /** 0 — every recording of the lesson. */
  period: number;
  /** Show the rows the owner took out too. */
  showLeftOut: boolean;
}

/** The lists shown until the owner picks: the words found by code that are not Spanish stay out. */
export const DEFAULT_WORD_FILTER: WordFilter = {
  onlyNew: true,
  lists: new Set<WordList>(['new', 'vocabulary', 'code']),
  query: '',
  period: 0,
  showLeftOut: false,
};

/** Lower case, no accents: «Está» — "esta". */
export function plain(text: string): string {
  return text.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
}

export function filterWordRows(
  rows: readonly WordRow[],
  filter: WordFilter
): WordRow[] {
  const query = plain(filter.query.trim());
  return rows.filter((row) => {
    if (row.leftOut && !filter.showLeftOut) return false;
    if (!filter.lists.has(row.list)) return false;
    if (filter.onlyNew && !isNewWord(row)) return false;
    if (filter.period !== 0 && row.period !== filter.period) return false;
    if (query === '') return true;
    return [row.word, row.ru, row.note, row.lemma, row.phrase].some((text) =>
      plain(text).includes(query)
    );
  });
}

export type WordSortKey = 'word' | 'ru' | 'list' | 'place' | 'seen' | 'count';

export interface WordSort {
  key: WordSortKey;
  dir: 'asc' | 'desc';
}

/** The order the lesson went in: recording, then time. */
export const DEFAULT_WORD_SORT: WordSort = { key: 'place', dir: 'asc' };

const placeOrder = (row: WordRow) =>
  row.period * 1_000_000 + (row.sec ?? 999_999);

const seenOrder = (row: WordRow) =>
  row.seenIn === null ? Number.MAX_SAFE_INTEGER : row.seenIn.length;

export function sortWordRows(
  rows: readonly WordRow[],
  sort: WordSort
): WordRow[] {
  const sign = sort.dir === 'asc' ? 1 : -1;
  const compare = (a: WordRow, b: WordRow): number => {
    switch (sort.key) {
      case 'word':
        // By the Spanish alphabet whatever the language of the browser: a word in other letters goes last.
        return plain(a.word).localeCompare(plain(b.word), 'es');
      case 'ru':
        return a.ru.localeCompare(b.ru, 'ru');
      case 'list':
        return (
          WORD_LISTS.indexOf(a.list) - WORD_LISTS.indexOf(b.list) ||
          a.tag.localeCompare(b.tag)
        );
      case 'seen':
        return seenOrder(a) - seenOrder(b);
      case 'count':
        return a.count - b.count;
      default:
        return placeOrder(a) - placeOrder(b);
    }
  };
  // The place breaks a tie, so rows equal by the column stand in the order of the lesson.
  return rows
    .slice()
    .sort((a, b) => sign * compare(a, b) || placeOrder(a) - placeOrder(b));
}

/** Clicking the heading of the column that orders the table turns the order; another column starts ascending. */
export function nextWordSort(current: WordSort, key: WordSortKey): WordSort {
  if (current.key !== key) return { key, dir: 'asc' };
  return { key, dir: current.dir === 'asc' ? 'desc' : 'asc' };
}

export interface WordCounts {
  /** Rows of each list (the ones taken out are not counted). */
  byList: Record<WordList, number>;
  /** Of them the words no earlier lesson has. */
  newByList: Record<WordList, number>;
  leftOut: number;
}

export function countWordRows(rows: readonly WordRow[]): WordCounts {
  const zero = (): Record<WordList, number> => ({
    new: 0,
    vocabulary: 0,
    code: 0,
    codeOther: 0,
  });
  const counts: WordCounts = { byList: zero(), newByList: zero(), leftOut: 0 };
  for (const row of rows) {
    if (row.leftOut) {
      counts.leftOut += 1;
      continue;
    }
    counts.byList[row.list] += 1;
    if (isNewWord(row)) counts.newByList[row.list] += 1;
  }
  return counts;
}

/**
 * The part of a long line around a word, cut at spaces: [before, the word as the line has it, after].
 * A line that does not have the word comes back whole in the first part.
 */
export function aroundWord(
  line: string,
  word: string,
  reach = 90
): [string, string, string] {
  const at = word.trim() === '' ? -1 : plain(line).indexOf(plain(word.trim()));
  // Accents are single letters after normalize, so the place in the plain text is the place in the line
  // only when both are the same length; otherwise the line is shown uncut.
  if (at < 0 || plain(line).length !== line.length) {
    return [
      line.length > reach * 3 ? `${line.slice(0, reach * 3)}…` : line,
      '',
      '',
    ];
  }
  const end = at + word.trim().length;
  let from = Math.max(0, at - reach);
  let to = Math.min(line.length, end + reach);
  if (from > 0) {
    const space = line.indexOf(' ', from);
    from = space >= 0 && space < at ? space + 1 : from;
  }
  if (to < line.length) {
    const space = line.lastIndexOf(' ', to);
    to = space > end ? space : to;
  }
  return [
    (from > 0 ? '…' : '') + line.slice(from, at),
    line.slice(at, end),
    line.slice(end, to) + (to < line.length ? '…' : ''),
  ];
}

/** "2026-10-23" → "23.10.2026"; anything else comes back as it is. */
export function dayLabel(date: string | null | undefined): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date ?? '');
  return m ? `${m[3]}.${m[2]}.${m[1]}` : (date ?? '');
}

/** Where the playing of a place starts: a little before it, so the first word is not cut. */
export function playFromSec(sec: number | null | undefined): number | null {
  if (sec === null || sec === undefined || !Number.isFinite(sec)) return null;
  return Math.max(0, sec - 1.5);
}

/** The address the audio of a recordings track is played from. */
export function trackAudioUrl(track: string): string {
  return `/api/sessions/tracks/audio?track=${encodeURIComponent(track)}`;
}

/** The widths of the columns the owner may drag, in px. */
export const COLUMN_WIDTHS_KEY = 'xtract-package-columns';
export const COLUMN_MIN = 70;
export const COLUMN_MAX = 1400;

export function clampColumn(width: number): number {
  if (!Number.isFinite(width)) return COLUMN_MIN;
  return Math.min(COLUMN_MAX, Math.max(COLUMN_MIN, Math.round(width)));
}

/** The widths kept from the last time, over the given ones; anything that is not a width is dropped. */
export function readColumnWidths<K extends string>(
  defaults: Record<K, number>,
  stored: string | null | undefined
): Record<K, number> {
  const widths = { ...defaults };
  if (!stored) return widths;
  try {
    const parsed = JSON.parse(stored) as Record<string, unknown>;
    for (const key of Object.keys(defaults) as K[]) {
      const value = parsed?.[key];
      if (typeof value === 'number' && Number.isFinite(value)) {
        widths[key] = clampColumn(value);
      }
    }
  } catch {
    // not ours: the given widths stay
  }
  return widths;
}

/** The builder's word for what a row is (explained, noun, spanish…), in the language of the UI. */
export function tagLabel(tag: string, t: (key: string) => string): string {
  const key = `pkgTag_${tag === '' ? 'none' : tag}`;
  const label = t(key);
  return label === key ? tag : label;
}

/** Whose voice a place is in, in the language of the UI; empty when the package does not tell. */
export function whoLabel(
  who: string | null | undefined,
  t: (key: string) => string
): string {
  if (who === 'teacher') return t('pkgTeacher');
  if (who === 'student') return t('pkgStudent');
  return '';
}

/** What an edit of one item of a package changes. */
export interface PackageEdit {
  /** Name of the field — the text to write; null — back to what the builder wrote. */
  fields?: Record<string, string | null>;
  /** True — take the item out of the lists; false — put it back. */
  leftOut?: boolean;
}
