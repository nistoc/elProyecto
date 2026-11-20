/**
 * The notes of a lesson as GET /api/sessions/{date}/notes gives them: the notes of the vault copy in the day
 * folder of the lesson — the homework an earlier parse made of it, a summary, cards, a log. Only read.
 */

export type LessonNoteKind =
  | 'homework'
  | 'summary'
  | 'cards'
  | 'dialogues'
  | 'topics'
  | 'transcript'
  | 'log'
  | 'other';

export interface LessonNote {
  /** Path from the recordings root, with '/': the text is asked by it. */
  path: string;
  title: string;
  kind: LessonNoteKind | string;
  sizeBytes: number;
  modifiedAt: string;
}

export interface LessonNotesResponse {
  date: string;
  notes: LessonNote[];
}

/** How many notes the lesson has, and how many of them are homework. */
export interface LessonNotesInfo {
  notes: number;
  homework: number;
}

export const NOTE_KINDS: readonly LessonNoteKind[] = [
  'homework',
  'summary',
  'cards',
  'dialogues',
  'topics',
  'transcript',
  'log',
  'other',
];

/** The key of the screen's word for a kind of note; an unknown kind is "other". */
export function noteKindKey(kind: string): string {
  return `notesKind_${(NOTE_KINDS as readonly string[]).includes(kind) ? kind : 'other'}`;
}

/** The notes in groups by kind, in the order of NOTE_KINDS; only the homework when `homeworkOnly`. */
export function groupNotes(
  notes: readonly LessonNote[],
  homeworkOnly: boolean
): { kind: string; notes: LessonNote[] }[] {
  const groups = new Map<string, LessonNote[]>();
  for (const note of notes) {
    const kind = (NOTE_KINDS as readonly string[]).includes(note.kind)
      ? note.kind
      : 'other';
    if (homeworkOnly && kind !== 'homework') continue;
    groups.set(kind, [...(groups.get(kind) ?? []), note]);
  }
  return NOTE_KINDS.filter((k) => groups.has(k)).map((kind) => ({
    kind,
    notes: groups.get(kind)!,
  }));
}

/** The note a link of another note names (its file name, as the vault finds it), or null. */
export function findNote(
  notes: readonly LessonNote[],
  target: string
): LessonNote | null {
  const want = target.trim().replace(/\.md$/i, '').toLowerCase();
  if (!want) return null;
  return (
    notes.find((n) => n.title.toLowerCase() === want) ??
    notes.find((n) =>
      n.path.toLowerCase().replace(/\.md$/i, '').endsWith(`/${want}`)
    ) ??
    null
  );
}

/** The note shown first: the first homework, else the first note. */
export function firstNote(notes: readonly LessonNote[]): LessonNote | null {
  return notes.find((n) => n.kind === 'homework') ?? notes[0] ?? null;
}

/** The address a picture or a sound of a note is shown from. */
export function noteFileUrl(date: string, name: string): string {
  return `/api/sessions/${encodeURIComponent(date)}/notes/file?name=${encodeURIComponent(name)}`;
}

/** "12 KB" — the size of a note, rounded up. */
export function noteSizeLabel(bytes: number): string {
  return `${Math.max(1, Math.ceil(bytes / 1024))} KB`;
}
