import { describe, expect, it } from 'vitest';
import {
  findNote,
  firstNote,
  groupNotes,
  noteFileUrl,
  noteKindKey,
  noteSizeLabel,
  type LessonNote,
} from './lessonNotes';

// All names here are made up.
const note = (title: string, kind: string): LessonNote => ({
  path: `2026.03.04/obsidian/${title}.md`,
  title,
  kind,
  sizeBytes: 2048,
  modifiedAt: '2026-03-05T10:00:00Z',
});

const notes = [
  note('Conspecto', 'summary'),
  note('Tarea 1', 'homework'),
  note('Rara', 'something-new'),
  note('Tarea 2', 'homework'),
];

describe('lesson notes', () => {
  it('groups the notes by kind in a fixed order, an unknown kind as other', () => {
    expect(
      groupNotes(notes, false).map((g) => [g.kind, g.notes.map((n) => n.title)])
    ).toEqual([
      ['homework', ['Tarea 1', 'Tarea 2']],
      ['summary', ['Conspecto']],
      ['other', ['Rara']],
    ]);
    expect(groupNotes(notes, true).map((g) => g.kind)).toEqual(['homework']);
  });

  it('shows the first homework first', () => {
    expect(firstNote(notes)?.title).toBe('Tarea 1');
    expect(firstNote([note('Solo', 'log')])?.title).toBe('Solo');
    expect(firstNote([])).toBeNull();
  });

  it('finds a linked note by its name as the vault does', () => {
    expect(findNote(notes, 'tarea 2')?.title).toBe('Tarea 2');
    expect(findNote(notes, 'Tarea 2.md')?.title).toBe('Tarea 2');
    expect(findNote(notes, 'Nada')).toBeNull();
    expect(findNote(notes, ' ')).toBeNull();
  });

  it('names kinds, sizes and the address of a picture', () => {
    expect(noteKindKey('homework')).toBe('notesKind_homework');
    expect(noteKindKey('weird')).toBe('notesKind_other');
    expect(noteSizeLabel(10)).toBe('1 KB');
    expect(noteSizeLabel(2049)).toBe('3 KB');
    expect(noteFileUrl('2026-03-04', 'pizarra 1.png')).toBe(
      '/api/sessions/2026-03-04/notes/file?name=pizarra%201.png'
    );
  });
});
