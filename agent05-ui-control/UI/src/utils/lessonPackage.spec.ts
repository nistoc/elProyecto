import { describe, expect, it } from 'vitest';
import {
  COLUMN_MAX,
  COLUMN_MIN,
  DEFAULT_WORD_FILTER,
  DEFAULT_WORD_SORT,
  aroundWord,
  clampColumn,
  countWordRows,
  dayLabel,
  filterWordRows,
  isNewWord,
  isUnheard,
  nextWordSort,
  plain,
  playFromSec,
  readColumnWidths,
  sortWordRows,
  tagLabel,
  trackAudioUrl,
  whoLabel,
  wordRows,
  type LessonPackageDocument,
  type WordFilter,
  type WordList,
} from './lessonPackage';

// Every word and line here is made up.
const pkg: LessonPackageDocument = {
  lesson: '2026-10-23',
  periods: [
    { n: 1, lines: 10 },
    { n: 2, lines: 5 },
  ],
  topics: [],
  grammar: [],
  wordNotes: [],
  examples: [],
  newWords: [
    {
      id: 'n-ventana',
      period: 1,
      at: '0:05',
      sec: 5,
      who: 'teacher',
      es: 'ventana',
      ru: 'окно',
      how: 'explained',
      quote: 'la ventana',
      said: 'Abre la ventana, por favor.',
      seenIn: ['2026-10-16'],
    },
    {
      id: 'n-tejado',
      period: 2,
      at: '1:10',
      sec: 70,
      es: 'tejado',
      ru: 'крыша',
      how: 'asked',
      note: 'of a house',
      quote: 'el tejado',
      seenIn: [],
      edited: { ru: 'кровля' },
    },
    {
      id: 'n-llevar-gerundio',
      period: 1,
      at: null,
      sec: null,
      es: 'se + lo',
      ru: 'образец',
      seenIn: null,
    },
  ],
  vocabulary: [
    {
      id: 'v-estufa',
      period: 1,
      at: '0:30',
      sec: 30,
      es: 'estufa',
      ru: 'печь',
      pos: 'noun',
      seenIn: [],
      leftOut: true,
    },
  ],
  otherWords: [
    {
      id: 'o-chimenea',
      word: 'chimenea',
      raw: 'Chimenea',
      count: 3,
      period: 1,
      at: '0:50',
      sec: 50,
      said: 'Una chimenea grande.',
      kind: 'spanish',
      lemma: 'chimenea',
      ru: 'камин',
      seenIn: [],
    },
    {
      id: 'o-ladno',
      word: 'ladno',
      raw: 'ladno',
      count: 1,
      period: 2,
      at: '0:02',
      sec: 2,
      kind: 'russian',
      seenIn: [],
    },
    {
      id: 'o-okno',
      word: 'okno',
      // The owner wrote the word in the letters it was said in.
      raw: 'окно',
      count: 1,
      period: 2,
      kind: 'spanish',
      seenIn: [],
      edited: { raw: 'okno' },
    },
  ],
};

const rows = wordRows(pkg);
const ids = (list: { id: string }[]) => list.map((r) => r.id);
const filter = (over: Partial<WordFilter> = {}): WordFilter => ({
  ...DEFAULT_WORD_FILTER,
  ...over,
});
const lists = (...names: WordList[]) => new Set<WordList>(names);

describe('wordRows', () => {
  it('puts the three lists into one table', () => {
    expect(ids(rows)).toEqual([
      'n-ventana',
      'n-tejado',
      'n-llevar-gerundio',
      'v-estufa',
      'o-chimenea',
      'o-ladno',
      'o-okno',
    ]);
    expect(rows[0]).toMatchObject({
      list: 'new',
      word: 'ventana',
      wordField: 'es',
      ru: 'окно',
      tag: 'explained',
      period: 1,
      at: '0:05',
      sec: 5,
      who: 'teacher',
      phrase: 'la ventana',
      phraseField: 'quote',
      line: 'Abre la ventana, por favor.',
      seenIn: ['2026-10-16'],
      leftOut: false,
      edited: {},
    });
    expect(rows[1].edited).toEqual({ ru: 'кровля' });
    expect(rows[3]).toMatchObject({
      list: 'vocabulary',
      tag: 'noun',
      leftOut: true,
    });
    expect(rows[4]).toMatchObject({
      list: 'code',
      word: 'Chimenea',
      wordField: 'raw',
      tag: 'spanish',
      lemma: 'chimenea',
      phrase: 'Una chimenea grande.',
      phraseField: 'said',
      count: 3,
    });
  });

  it('keeps a word found by code that is not Spanish in a list of its own', () => {
    expect(rows[5].list).toBe('codeOther');
    // Called Spanish by the model, but the owner wrote it in other letters.
    expect(rows[6].list).toBe('codeOther');
  });

  it('reads a package with no lists as no rows', () => {
    expect(wordRows({} as unknown as LessonPackageDocument)).toEqual([]);
  });
});

describe('isNewWord', () => {
  it('is a word no earlier lesson has; a pattern with nothing to look for is not one', () => {
    expect(isNewWord(rows[0])).toBe(false);
    expect(isNewWord(rows[1])).toBe(true);
    expect(isNewWord(rows[2])).toBe(false);
  });
});

describe('filterWordRows', () => {
  it('shows at first only what no earlier lesson has, without the rows taken out and the words that are not Spanish', () => {
    expect(ids(filterWordRows(rows, DEFAULT_WORD_FILTER))).toEqual([
      'n-tejado',
      'o-chimenea',
    ]);
  });

  it('shows every word of the chosen lists when asked', () => {
    expect(ids(filterWordRows(rows, filter({ onlyNew: false })))).toEqual([
      'n-ventana',
      'n-tejado',
      'n-llevar-gerundio',
      'o-chimenea',
    ]);
    expect(
      ids(
        filterWordRows(
          rows,
          filter({ onlyNew: false, lists: lists('codeOther') })
        )
      )
    ).toEqual(['o-ladno', 'o-okno']);
  });

  it('shows the rows taken out on request', () => {
    expect(
      ids(
        filterWordRows(
          rows,
          filter({ showLeftOut: true, lists: lists('vocabulary') })
        )
      )
    ).toEqual(['v-estufa']);
  });

  it('keeps to one recording', () => {
    expect(
      ids(filterWordRows(rows, filter({ onlyNew: false, period: 2 })))
    ).toEqual(['n-tejado']);
  });

  it('looks for the text in the word, translation, note and phrase without case and accents', () => {
    const all = filter({ onlyNew: false });
    expect(ids(filterWordRows(rows, { ...all, query: ' VENTÁNA ' }))).toEqual([
      'n-ventana',
    ]);
    expect(ids(filterWordRows(rows, { ...all, query: 'крыш' }))).toEqual([
      'n-tejado',
    ]);
    expect(ids(filterWordRows(rows, { ...all, query: 'house' }))).toEqual([
      'n-tejado',
    ]);
    expect(ids(filterWordRows(rows, { ...all, query: 'grande' }))).toEqual([
      'o-chimenea',
    ]);
    expect(filterWordRows(rows, { ...all, query: 'balcón' })).toEqual([]);
  });
});

describe('words no other hearing has', () => {
  const heard = wordRows({
    ...pkg,
    newWords: pkg.newWords.map((w, i) => ({
      ...w,
      heardElsewhere: i === 0 ? true : i === 1 ? false : null,
    })),
  });

  it('carries what the hearings told, and nothing for a package read before them', () => {
    expect(heard.slice(0, 3).map((r) => r.heardElsewhere)).toEqual([
      true,
      false,
      null,
    ]);
    expect(rows.every((r) => r.heardElsewhere === null)).toBe(true);
    expect(heard.filter(isUnheard).map((r) => r.id)).toEqual(['n-tejado']);
  });

  it('keeps to the doubtful words when asked, together with the other filters', () => {
    expect(
      ids(filterWordRows(heard, filter({ onlyNew: false, onlyUnheard: true })))
    ).toEqual(['n-tejado']);
    expect(
      ids(
        filterWordRows(
          heard,
          filter({ onlyNew: false, onlyUnheard: true, period: 1 })
        )
      )
    ).toEqual([]);
    expect(
      filterWordRows(heard, filter({ onlyNew: false })).length
    ).toBeGreaterThan(1);
  });
});

describe('sortWordRows', () => {
  const shown = filterWordRows(
    rows,
    filter({
      onlyNew: false,
      showLeftOut: true,
      lists: lists('new', 'vocabulary', 'code', 'codeOther'),
    })
  );

  it('stands at first in the order of the lesson, a row with no place last in its recording', () => {
    expect(ids(sortWordRows(shown, DEFAULT_WORD_SORT))).toEqual([
      'n-ventana',
      'v-estufa',
      'o-chimenea',
      'n-llevar-gerundio',
      'o-ladno',
      'n-tejado',
      'o-okno',
    ]);
  });

  it('orders by the word without case and accents, both ways', () => {
    const asc = ids(sortWordRows(shown, { key: 'word', dir: 'asc' }));
    expect(asc.slice(0, 3)).toEqual(['o-chimenea', 'v-estufa', 'o-ladno']);
    const desc = ids(sortWordRows(shown, { key: 'word', dir: 'desc' }));
    expect(desc[desc.length - 1]).toBe('o-chimenea');
  });

  it('orders by how many earlier lessons have the word, patterns last', () => {
    const bySeen = ids(sortWordRows(shown, { key: 'seen', dir: 'asc' }));
    expect(bySeen.slice(-2)).toEqual(['n-ventana', 'n-llevar-gerundio']);
    // Rows equal by the column stand in the order of the lesson.
    expect(bySeen.slice(0, 2)).toEqual(['v-estufa', 'o-chimenea']);
  });

  it('orders by the list and by how often a word was said', () => {
    expect(
      ids(sortWordRows(shown, { key: 'list', dir: 'asc' })).slice(-2)
    ).toEqual(['o-ladno', 'o-okno']);
    expect(ids(sortWordRows(shown, { key: 'count', dir: 'desc' }))[0]).toBe(
      'o-chimenea'
    );
  });

  it('does not change the list it was given', () => {
    const before = ids(shown);
    sortWordRows(shown, { key: 'word', dir: 'desc' });
    expect(ids(shown)).toEqual(before);
  });
});

describe('nextWordSort', () => {
  it('turns the order of the same column and starts another column ascending', () => {
    expect(nextWordSort({ key: 'word', dir: 'asc' }, 'word')).toEqual({
      key: 'word',
      dir: 'desc',
    });
    expect(nextWordSort({ key: 'word', dir: 'desc' }, 'word')).toEqual({
      key: 'word',
      dir: 'asc',
    });
    expect(nextWordSort({ key: 'word', dir: 'desc' }, 'seen')).toEqual({
      key: 'seen',
      dir: 'asc',
    });
  });
});

describe('countWordRows', () => {
  it('counts the rows of each list and the new ones among them, without the rows taken out', () => {
    expect(countWordRows(rows)).toEqual({
      byList: { new: 3, vocabulary: 0, code: 1, codeOther: 2 },
      newByList: { new: 1, vocabulary: 0, code: 1, codeOther: 2 },
      leftOut: 1,
    });
  });
});

describe('aroundWord', () => {
  it('gives a short line whole, with the word as the line has it', () => {
    expect(aroundWord('Abre la Ventána, por favor.', 'ventana')).toEqual([
      'Abre la ',
      'Ventána',
      ', por favor.',
    ]);
  });

  it('cuts a long line at spaces on both sides of the word', () => {
    const line = `${'uno dos tres '.repeat(20)}la ventana ${'cuatro cinco '.repeat(20)}`;
    const [before, word, after] = aroundWord(line, 'ventana', 20);
    expect(word).toBe('ventana');
    expect(before.startsWith('…')).toBe(true);
    expect(before.endsWith('la ')).toBe(true);
    expect(before.length).toBeLessThanOrEqual(22);
    expect(after.endsWith('…')).toBe(true);
    expect(after.length).toBeLessThanOrEqual(22);
    // Cut at a space: the first and the last word are whole.
    expect(before).toBe('…uno dos tres la ');
    expect(after).toBe(' cuatro cinco cuatro…');
  });

  it('gives back a line that does not have the word', () => {
    expect(aroundWord('El tejado es rojo.', 'ventana')).toEqual([
      'El tejado es rojo.',
      '',
      '',
    ]);
    expect(aroundWord('El tejado es rojo.', '  ')).toEqual([
      'El tejado es rojo.',
      '',
      '',
    ]);
  });
});

describe('small helpers', () => {
  it('plain is lower case without accents', () => {
    expect(plain('Está MAÑANA')).toBe('esta manana');
  });

  it('dayLabel writes the date of a lesson the usual way', () => {
    expect(dayLabel('2026-10-23')).toBe('23.10.2026');
    expect(dayLabel('yesterday')).toBe('yesterday');
    expect(dayLabel(null)).toBe('');
  });

  it('playFromSec starts a little before the place', () => {
    expect(playFromSec(12.5)).toBe(11);
    expect(playFromSec(0.4)).toBe(0);
    expect(playFromSec(null)).toBeNull();
    expect(playFromSec(Number.NaN)).toBeNull();
  });

  it('trackAudioUrl puts the id of the track into the address safely', () => {
    expect(trackAudioUrl('2026.10.23/a b&c')).toBe(
      '/api/sessions/tracks/audio?track=2026.10.23%2Fa%20b%26c'
    );
  });

  it('tagLabel and whoLabel speak the language of the UI', () => {
    const words: Record<string, string> = {
      pkgTag_noun: 'существительное',
      pkgTag_none: 'не разобрано',
      pkgTeacher: 'преподаватель',
      pkgStudent: 'ученик',
    };
    const t = (key: string) => words[key] ?? key;
    expect(tagLabel('noun', t)).toBe('существительное');
    expect(tagLabel('', t)).toBe('не разобрано');
    expect(tagLabel('something-new', t)).toBe('something-new');
    expect(whoLabel('teacher', t)).toBe('преподаватель');
    expect(whoLabel('student', t)).toBe('ученик');
    expect(whoLabel('', t)).toBe('');
    expect(whoLabel(null, t)).toBe('');
  });
});

describe('column widths', () => {
  const defaults = { word: 200, ru: 220 };

  it('clampColumn keeps a width inside the limits', () => {
    expect(clampColumn(10)).toBe(COLUMN_MIN);
    expect(clampColumn(99999)).toBe(COLUMN_MAX);
    expect(clampColumn(250.6)).toBe(251);
    expect(clampColumn(Number.NaN)).toBe(COLUMN_MIN);
  });

  it('readColumnWidths puts the kept widths over the given ones and drops the rest', () => {
    expect(
      readColumnWidths(
        defaults,
        JSON.stringify({ word: 320, ru: 'wide', other: 500 })
      )
    ).toEqual({ word: 320, ru: 220 });
    expect(readColumnWidths(defaults, JSON.stringify({ word: 5 }))).toEqual({
      word: COLUMN_MIN,
      ru: 220,
    });
  });

  it('readColumnWidths gives the given widths when nothing usable is kept', () => {
    expect(readColumnWidths(defaults, null)).toEqual(defaults);
    expect(readColumnWidths(defaults, '{ broken')).toEqual(defaults);
    expect(readColumnWidths(defaults, 'null')).toEqual(defaults);
  });
});
