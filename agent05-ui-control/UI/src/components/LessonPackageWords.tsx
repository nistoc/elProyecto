import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type PointerEvent as ReactPointerEvent,
} from 'react';
import {
  COLUMN_WIDTHS_KEY,
  DEFAULT_WORD_FILTER,
  DEFAULT_WORD_SORT,
  WORD_LISTS,
  aroundWord,
  clampColumn,
  countWordRows,
  dayLabel,
  filterWordRows,
  isNewWord,
  nextWordSort,
  readColumnWidths,
  sortWordRows,
  tagLabel,
  whoLabel,
  wordRows,
  type LessonPackageDocument,
  type PackageEdit,
  type WordFilter,
  type WordList,
  type WordRow,
  type WordSort,
  type WordSortKey,
} from '../utils/lessonPackage';
import { EditableText } from './EditableText';

interface LessonPackageWordsProps {
  pkg: LessonPackageDocument;
  /** How many earlier lessons the words were compared with. */
  comparedLessons: number;
  showLeftOut: boolean;
  t: (key: string) => string;
  /** Writes an edit of one item; resolves false when it was not saved. */
  onEdit: (id: string, change: PackageEdit) => Promise<boolean>;
  /** Plays the recording of a period from a place; null when that period has no audio to play. */
  onPlay: (period: number, sec: number | null) => void;
  canPlay: (period: number) => boolean;
}

type ColumnKey = 'word' | 'ru' | 'list' | 'place' | 'seen';

const COLUMN_DEFAULTS: Record<ColumnKey, number> = {
  word: 280,
  ru: 280,
  list: 210,
  place: 190,
  seen: 190,
};

function storedWidths(): Record<ColumnKey, number> {
  let stored: string | null = null;
  try {
    stored = localStorage.getItem(COLUMN_WIDTHS_KEY);
  } catch {
    // A browser that refuses storage: the columns keep the usual widths.
  }
  return readColumnWidths(COLUMN_DEFAULTS, stored);
}

const LIST_LABEL: Record<WordList, string> = {
  new: 'pkgListNew',
  vocabulary: 'pkgListVocabulary',
  code: 'pkgListCode',
  codeOther: 'pkgListCodeOther',
};

/**
 * The words of a lesson package as one table: the new words, the vocabulary and the words found by code.
 * It opens on the words no earlier lesson has; the lists, the search and the recording narrow it, a
 * click on a heading orders it, the edges of the headings are dragged, and every text is edited in place.
 */
export function LessonPackageWords({
  pkg,
  comparedLessons,
  showLeftOut,
  t,
  onEdit,
  onPlay,
  canPlay,
}: LessonPackageWordsProps) {
  const [filter, setFilter] = useState<WordFilter>(DEFAULT_WORD_FILTER);
  const [sort, setSort] = useState<WordSort>(DEFAULT_WORD_SORT);
  const [widths, setWidths] = useState(storedWidths);
  const [resizing, setResizing] = useState(false);
  const drag = useRef<{ key: ColumnKey; startX: number; start: number } | null>(
    null
  );

  const rows = useMemo(() => wordRows(pkg), [pkg]);
  const counts = useMemo(() => countWordRows(rows), [rows]);
  const shown = useMemo(
    () => sortWordRows(filterWordRows(rows, { ...filter, showLeftOut }), sort),
    [rows, filter, showLeftOut, sort]
  );
  const totals = useMemo(() => {
    const picked = WORD_LISTS.filter((l) => filter.lists.has(l));
    return {
      onlyNew: picked.reduce((n, l) => n + counts.newByList[l], 0),
      all: picked.reduce((n, l) => n + counts.byList[l], 0),
    };
  }, [counts, filter.lists]);

  // Written when the drag ends, not on every pixel of it.
  useEffect(() => {
    if (resizing) return;
    try {
      localStorage.setItem(COLUMN_WIDTHS_KEY, JSON.stringify(widths));
    } catch {
      // Not kept: the next visit starts from the usual widths.
    }
  }, [widths, resizing]);

  const onGripDown = useCallback(
    (key: ColumnKey, e: ReactPointerEvent<HTMLElement>) => {
      if (e.button !== 0) return;
      e.preventDefault();
      e.stopPropagation();
      const el = e.currentTarget;
      drag.current = { key, startX: e.clientX, start: widths[key] };
      setResizing(true);
      try {
        el.setPointerCapture(e.pointerId);
      } catch {
        // No capture: the drag still follows the pointer over the grip.
      }
      const move = (ev: PointerEvent) => {
        const d = drag.current;
        if (!d) return;
        setWidths((w) => ({
          ...w,
          [d.key]: clampColumn(d.start + ev.clientX - d.startX),
        }));
      };
      const end = () => {
        drag.current = null;
        setResizing(false);
        el.removeEventListener('pointermove', move);
        el.removeEventListener('pointerup', end);
        el.removeEventListener('pointercancel', end);
        el.removeEventListener('lostpointercapture', end);
      };
      el.addEventListener('pointermove', move);
      el.addEventListener('pointerup', end);
      el.addEventListener('pointercancel', end);
      el.addEventListener('lostpointercapture', end);
    },
    [widths]
  );

  const toggleList = (list: WordList) =>
    setFilter((f) => {
      const lists = new Set(f.lists);
      if (lists.has(list)) lists.delete(list);
      else lists.add(list);
      return { ...f, lists };
    });

  const heading = (key: ColumnKey, sortKey: WordSortKey, label: string) => (
    <th
      style={{ width: widths[key] }}
      aria-sort={
        sort.key === sortKey
          ? sort.dir === 'asc'
            ? 'ascending'
            : 'descending'
          : 'none'
      }
    >
      <button
        type="button"
        className="pkg-th"
        onClick={() => setSort((s) => nextWordSort(s, sortKey))}
        title={t('pkgSortHint')}
      >
        {label}
        <span className="pkg-th__dir" aria-hidden="true">
          {sort.key === sortKey ? (sort.dir === 'asc' ? '▲' : '▼') : ''}
        </span>
      </button>
      <span
        className="pkg-th__grip"
        role="separator"
        aria-orientation="vertical"
        title={t('pkgResizeHint')}
        onPointerDown={(e) => onGripDown(key, e)}
        onDoubleClick={() =>
          setWidths((w) => ({ ...w, [key]: COLUMN_DEFAULTS[key] }))
        }
      />
    </th>
  );

  const phraseOf = (row: WordRow) => {
    if (row.phrase === '') return undefined;
    const [before, word, after] = aroundWord(row.phrase, row.word, 110);
    return (
      <>
        «{before}
        {word !== '' && <mark className="pkg-mark">{word}</mark>}
        {after}»
      </>
    );
  };

  return (
    <div className="pkg-words" data-testid="package-words">
      <div className="pkg-toolbar">
        <div className="pkg-seg" role="group">
          <button
            type="button"
            className={filter.onlyNew ? 'pkg-seg__btn on' : 'pkg-seg__btn'}
            aria-pressed={filter.onlyNew}
            onClick={() => setFilter((f) => ({ ...f, onlyNew: true }))}
            title={t('pkgOnlyNewHint')}
            data-testid="package-only-new"
          >
            {t('pkgOnlyNew')} <b>{totals.onlyNew}</b>
          </button>
          <button
            type="button"
            className={filter.onlyNew ? 'pkg-seg__btn' : 'pkg-seg__btn on'}
            aria-pressed={!filter.onlyNew}
            onClick={() => setFilter((f) => ({ ...f, onlyNew: false }))}
            data-testid="package-all-words"
          >
            {t('pkgAllWords')} <b>{totals.all}</b>
          </button>
        </div>
        <div className="pkg-chips" role="group">
          {WORD_LISTS.map((list) => (
            <button
              key={list}
              type="button"
              className={
                filter.lists.has(list)
                  ? `pkg-chip pkg-chip--${list} on`
                  : `pkg-chip pkg-chip--${list}`
              }
              aria-pressed={filter.lists.has(list)}
              onClick={() => toggleList(list)}
              title={t(`${LIST_LABEL[list]}Hint`)}
            >
              {t(LIST_LABEL[list])}{' '}
              <b>
                {filter.onlyNew ? counts.newByList[list] : counts.byList[list]}
              </b>
            </button>
          ))}
        </div>
        <input
          type="search"
          className="pkg-search"
          value={filter.query}
          placeholder={t('pkgSearch')}
          onChange={(e) => setFilter((f) => ({ ...f, query: e.target.value }))}
          data-testid="package-search"
        />
        <select
          className="pkg-select"
          value={filter.period}
          onChange={(e) =>
            setFilter((f) => ({ ...f, period: Number(e.target.value) }))
          }
          aria-label={t('pkgRecording')}
        >
          <option value={0}>{t('pkgAllRecordings')}</option>
          {pkg.periods.map((p) => (
            <option key={p.n} value={p.n}>
              {t('pkgRecording')} {p.n}
              {p.start ? ` · ${p.start}` : ''}
            </option>
          ))}
        </select>
        <span
          className="pkg-muted pkg-toolbar__shown"
          data-testid="package-shown"
        >
          {t('pkgShown')}: {shown.length}
        </span>
      </div>
      {filter.onlyNew && (
        <p className="pkg-muted pkg-words__note">
          {comparedLessons > 0
            ? `${t('pkgOnlyNewNote')} ${comparedLessons}.`
            : t('pkgComparedNone')}
        </p>
      )}

      <div
        className={
          resizing ? 'pkg-table-wrap pkg-table-wrap--drag' : 'pkg-table-wrap'
        }
      >
        <table className="pkg-table">
          <thead>
            <tr>
              {heading('word', 'word', t('pkgColWord'))}
              {heading('ru', 'ru', t('pkgColRu'))}
              {heading('list', 'list', t('pkgColList'))}
              {heading('place', 'place', t('pkgColPlace'))}
              <th>{t('pkgColPhrase')}</th>
              {heading('seen', 'seen', t('pkgColSeen'))}
              <th style={{ width: 96 }} aria-label={t('pkgLeaveOut')} />
            </tr>
          </thead>
          <tbody>
            {shown.map((row) => (
              <tr
                key={row.id}
                className={row.leftOut ? 'pkg-row pkg-row--out' : 'pkg-row'}
                data-testid="package-word-row"
              >
                <td>
                  <div className="pkg-word">
                    <EditableText
                      value={row.word}
                      was={row.edited[row.wordField]}
                      hint={
                        row.line
                          ? `«${row.line}»\n${t('pkgRecording')} ${row.period} · ${row.at}`
                          : undefined
                      }
                      t={t}
                      onSave={(text) =>
                        onEdit(row.id, { fields: { [row.wordField]: text } })
                      }
                    />
                  </div>
                  {row.wordField === 'raw' &&
                    (row.lemma !== '' || 'lemma' in row.edited) && (
                      <div className="pkg-sub">
                        {t('pkgLemma')}:{' '}
                        <EditableText
                          value={row.lemma}
                          was={row.edited.lemma}
                          t={t}
                          onSave={(text) =>
                            onEdit(row.id, { fields: { lemma: text } })
                          }
                        />
                      </div>
                    )}
                  {row.heard !== '' && (
                    <div className="pkg-sub">
                      {t('pkgHeard')}: {row.heard}
                    </div>
                  )}
                  {row.wordField === 'es' &&
                    (row.note !== '' || 'note' in row.edited) && (
                      <div className="pkg-sub">
                        <EditableText
                          value={row.note}
                          was={row.edited.note}
                          multiline
                          t={t}
                          onSave={(text) =>
                            onEdit(row.id, { fields: { note: text } })
                          }
                        />
                      </div>
                    )}
                </td>
                <td>
                  <EditableText
                    value={row.ru}
                    was={row.edited.ru}
                    t={t}
                    onSave={(text) => onEdit(row.id, { fields: { ru: text } })}
                  />
                </td>
                <td>
                  <span
                    className={`pkg-chip pkg-chip--${row.list} on pkg-chip--still`}
                  >
                    {t(LIST_LABEL[row.list])}
                  </span>
                  <div className="pkg-sub">
                    {tagLabel(row.tag, t)}
                    {row.count > 1 ? ` · ×${row.count}` : ''}
                  </div>
                </td>
                <td className="pkg-place">
                  <button
                    type="button"
                    className="pkg-play"
                    disabled={row.sec === null || !canPlay(row.period)}
                    onClick={() => onPlay(row.period, row.sec)}
                    title={canPlay(row.period) ? t('pkgPlay') : t('pkgNoAudio')}
                    aria-label={t('pkgPlay')}
                  >
                    ▶
                  </button>
                  <span className="pkg-place__at">
                    {row.period} · {row.at || '—'}
                  </span>
                  <span className="pkg-sub">{whoLabel(row.who, t)}</span>
                </td>
                <td className="pkg-phrase">
                  <EditableText
                    value={row.phrase}
                    was={row.edited[row.phraseField]}
                    multiline
                    display={phraseOf(row)}
                    hint={
                      row.line && row.line !== row.phrase
                        ? `«${row.line}»`
                        : undefined
                    }
                    t={t}
                    onSave={(text) =>
                      onEdit(row.id, { fields: { [row.phraseField]: text } })
                    }
                  />
                </td>
                <td>
                  {row.seenIn === null ? (
                    <span className="pkg-muted" title={t('pkgPatternHint')}>
                      {t('pkgPattern')}
                    </span>
                  ) : isNewWord(row) ? (
                    <span className="pkg-badge-new">{t('pkgNew')}</span>
                  ) : (
                    <span
                      title={`${t('pkgSeenHint')}:\n${row.seenIn.map(dayLabel).join('\n')}`}
                    >
                      {t('pkgSeenLessons')}: {row.seenIn.length}
                      <span className="pkg-sub">
                        {t('pkgSeenSince')} {dayLabel(row.seenIn[0])}
                      </span>
                    </span>
                  )}
                </td>
                <td>
                  <button
                    type="button"
                    className="pkg-btn"
                    onClick={() =>
                      void onEdit(row.id, { leftOut: !row.leftOut })
                    }
                    title={t('pkgLeaveOutHint')}
                    data-testid="package-leave-out"
                  >
                    {row.leftOut ? t('pkgPutBack') : t('pkgLeaveOut')}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {shown.length === 0 && (
          <p className="pkg-empty" data-testid="package-words-empty">
            {filter.onlyNew && filter.query.trim() === ''
              ? t('pkgNothingNew')
              : t('pkgEmpty')}
          </p>
        )}
      </div>
    </div>
  );
}
