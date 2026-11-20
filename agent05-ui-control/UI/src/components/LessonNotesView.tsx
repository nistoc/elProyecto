import { useCallback, useEffect, useMemo, useState } from 'react';
import { fetchLessonNoteText, fetchLessonNotes } from '../api';
import { parseMarkdown } from '../utils/markdown';
import {
  findNote,
  firstNote,
  groupNotes,
  noteFileUrl,
  noteKindKey,
  noteSizeLabel,
  type LessonNote,
} from '../utils/lessonNotes';
import { MarkdownView } from './MarkdownView';

interface LessonNotesViewProps {
  /** YYYY-MM-DD of the lesson. */
  date: string;
  t: (key: string) => string;
  onClose: () => void;
}

/** "2026-02-18" → "18.02.2026". */
function dayLabel(date: string): string {
  const [y, m, d] = date.split('-');
  return d && m && y ? `${d}.${m}.${y}` : date;
}

/**
 * The notes of a lesson from the vault copy in its day folder — first of all the homework an earlier parse made
 * of the lesson — in place of the job view: the list of notes on the left, the note on the right. Only read:
 * the notes are the owner's files in the recordings folder.
 */
export function LessonNotesView({ date, t, onClose }: LessonNotesViewProps) {
  const [list, setList] = useState<{
    date: string;
    notes: LessonNote[] | null;
    error: string | null;
  } | null>(null);
  const [homeworkOnly, setHomeworkOnly] = useState(true);
  /** The note picked, by its path, in the lesson it was picked in. */
  const [picked, setPicked] = useState<{ date: string; path: string } | null>(
    null
  );
  const [text, setText] = useState<{
    path: string;
    text: string | null;
    error: string | null;
  } | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetchLessonNotes(date)
      .then((r) => {
        if (!cancelled) setList({ date, notes: r.notes, error: null });
      })
      .catch((e: unknown) => {
        if (!cancelled) {
          setList({
            date,
            notes: null,
            error: e instanceof Error ? e.message : String(e),
          });
        }
      });
    return () => {
      cancelled = true;
    };
  }, [date]);

  // The notes of the lesson that was open before are not shown under the next one.
  const notes = useMemo(
    () => (list && list.date === date ? list.notes : null),
    [list, date]
  );
  const hasHomework = !!notes?.some((n) => n.kind === 'homework');
  const onlyHomework = homeworkOnly && hasHomework;
  const groups = useMemo(
    () => (notes ? groupNotes(notes, onlyHomework) : []),
    [notes, onlyHomework]
  );
  const shown = useMemo(() => {
    if (!notes) return null;
    const path = picked && picked.date === date ? picked.path : null;
    return notes.find((n) => n.path === path) ?? firstNote(notes);
  }, [notes, picked, date]);

  useEffect(() => {
    if (!shown) return;
    let cancelled = false;
    fetchLessonNoteText(date, shown.path)
      .then((v) => {
        if (!cancelled) setText({ path: shown.path, text: v, error: null });
      })
      .catch((e: unknown) => {
        if (!cancelled) {
          setText({
            path: shown.path,
            text: null,
            error: e instanceof Error ? e.message : String(e),
          });
        }
      });
    return () => {
      cancelled = true;
    };
  }, [date, shown]);

  const current = shown && text && text.path === shown.path ? text : null;
  const blocks = useMemo(
    () => (current?.text != null ? parseMarkdown(current.text) : null),
    [current]
  );

  const embedUrl = useCallback(
    (name: string) => noteFileUrl(date, name),
    [date]
  );
  const onOpenNote = useCallback(
    (target: string) => {
      const note = notes ? findNote(notes, target) : null;
      if (!note) return null;
      return () => {
        if (note.kind !== 'homework') setHomeworkOnly(false);
        setPicked({ date, path: note.path });
      };
    },
    [notes, date]
  );

  const head = (
    <div className="notes-head">
      <h2 className="notes-title">
        {t('lessonNotes')} · {dayLabel(date)}
      </h2>
      {notes && notes.length > 0 && (
        <span
          className="notes-switch"
          role="group"
          aria-label={t('lessonNotes')}
        >
          <button
            type="button"
            className={
              onlyHomework ? 'notes-chip notes-chip--on' : 'notes-chip'
            }
            aria-pressed={onlyHomework}
            disabled={!hasHomework}
            onClick={() => setHomeworkOnly(true)}
          >
            {t('notesKind_homework')}{' '}
            <b>{notes.filter((n) => n.kind === 'homework').length}</b>
          </button>
          <button
            type="button"
            className={
              !onlyHomework ? 'notes-chip notes-chip--on' : 'notes-chip'
            }
            aria-pressed={!onlyHomework}
            onClick={() => setHomeworkOnly(false)}
          >
            {t('notesAll')} <b>{notes.length}</b>
          </button>
        </span>
      )}
      <button type="button" className="notes-btn" onClick={onClose}>
        {t('pkgBack')}
      </button>
    </div>
  );

  if (!list || list.date !== date) {
    return (
      <div className="notes" data-testid="lesson-notes">
        <p className="notes-muted">{t('notesLoading')}</p>
        <NotesStyles />
      </div>
    );
  }

  return (
    <div className="notes" data-testid="lesson-notes">
      {head}
      {list.error && (
        <p className="notes-error" role="alert">
          {t('notesLoadFailed')}: {list.error}
        </p>
      )}
      {notes && notes.length === 0 && (
        <p className="notes-muted">{t('notesNone')}</p>
      )}
      {notes && notes.length > 0 && (
        <div className="notes-body">
          <nav className="notes-list" aria-label={t('lessonNotes')}>
            {groups.map((g) => (
              <section key={g.kind} className="notes-group">
                <h3 className="notes-group__title">{t(noteKindKey(g.kind))}</h3>
                <ul>
                  {g.notes.map((n) => (
                    <li key={n.path}>
                      <button
                        type="button"
                        className={
                          shown?.path === n.path
                            ? 'notes-item notes-item--on'
                            : 'notes-item'
                        }
                        aria-current={
                          shown?.path === n.path ? 'true' : undefined
                        }
                        onClick={() => setPicked({ date, path: n.path })}
                        title={n.path}
                      >
                        <span className="notes-item__title">{n.title}</span>
                        <span className="notes-muted">
                          {noteSizeLabel(n.sizeBytes)}
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              </section>
            ))}
          </nav>
          <article className="notes-note" aria-live="polite">
            {shown && (
              <header className="notes-note__head">
                <h3 className="notes-note__title">{shown.title}</h3>
                <span className="notes-muted">
                  {t(noteKindKey(shown.kind))} ·{' '}
                  {noteSizeLabel(shown.sizeBytes)}
                </span>
              </header>
            )}
            {!current && <p className="notes-muted">{t('notesLoading')}</p>}
            {current?.error && (
              <p className="notes-error" role="alert">
                {t('notesLoadFailed')}: {current.error}
              </p>
            )}
            {blocks && (
              <MarkdownView
                blocks={blocks}
                embedUrl={embedUrl}
                onOpenNote={onOpenNote}
                t={t}
              />
            )}
          </article>
        </div>
      )}
      <NotesStyles />
    </div>
  );
}

function NotesStyles() {
  return (
    <style>{`
      .notes {
        flex: 1;
        min-height: 0;
        display: flex;
        flex-direction: column;
        gap: 0.6rem;
        color: var(--color-text);
        font-size: 0.9rem;
      }
      .notes-head { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem 1rem; }
      .notes-title { margin: 0; font-size: 1.2rem; color: var(--color-heading); font-variant-numeric: tabular-nums; }
      .notes-head .notes-btn { margin-left: auto; }
      .notes-btn, .notes-chip {
        padding: 0.2rem 0.6rem;
        border: 1px solid var(--color-border-strong);
        border-radius: 6px;
        background: var(--color-surface);
        color: var(--color-text);
        font-size: 0.8rem;
        cursor: pointer;
        white-space: nowrap;
      }
      .notes-btn:hover, .notes-chip:hover:not(:disabled) { background: var(--color-surface-hover); }
      .notes-chip:disabled { opacity: 0.5; cursor: default; }
      .notes-switch { display: inline-flex; gap: 0.25rem; }
      .notes-chip b { margin-left: 0.2rem; font-variant-numeric: tabular-nums; }
      .notes-chip--on { border-color: var(--color-primary); background: var(--color-accent-soft); }
      .notes-muted { color: var(--color-text-muted); }
      .notes-error {
        margin: 0;
        padding: 0.5rem 0.75rem;
        border: 1px solid var(--color-error);
        border-radius: 6px;
        background: color-mix(in srgb, var(--color-error) 10%, var(--color-surface));
      }
      .notes-body {
        flex: 1;
        min-height: 0;
        display: grid;
        grid-template-columns: minmax(13rem, min(22rem, 28%)) minmax(0, 1fr);
        gap: 1rem;
      }
      .notes-list {
        min-height: 0;
        overflow: auto;
        padding-right: 0.25rem;
        border-right: 1px solid var(--color-border);
      }
      .notes-group + .notes-group { margin-top: 0.75rem; }
      .notes-group__title {
        margin: 0 0 0.3rem;
        font-size: 0.75rem;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        color: var(--color-label);
      }
      .notes-group ul { margin: 0; padding: 0; list-style: none; display: flex; flex-direction: column; gap: 0.15rem; }
      .notes-item {
        width: 100%;
        display: flex;
        align-items: baseline;
        justify-content: space-between;
        gap: 0.5rem;
        padding: 0.35rem 0.5rem;
        border: 1px solid transparent;
        border-radius: 6px;
        background: transparent;
        color: var(--color-text);
        font: inherit;
        text-align: left;
        cursor: pointer;
      }
      .notes-item:hover { background: var(--color-surface-hover); }
      .notes-item--on { border-color: var(--color-primary); background: var(--color-accent-soft); }
      .notes-item__title { min-width: 0; overflow-wrap: anywhere; }
      .notes-item .notes-muted { flex: none; font-size: 0.75rem; font-variant-numeric: tabular-nums; }
      .notes-note {
        min-width: 0;
        min-height: 0;
        overflow: auto;
        padding: 0.75rem 1.25rem 2rem;
        border: 1px solid var(--color-border);
        border-radius: 8px;
        background: var(--color-surface);
      }
      .notes-note__head { display: flex; flex-wrap: wrap; align-items: baseline; gap: 0.25rem 0.75rem; margin-bottom: 0.5rem; }
      .notes-note__title { margin: 0; font-size: 1.1rem; color: var(--color-heading); overflow-wrap: anywhere; }

      .md { line-height: 1.55; max-width: 110ch; }
      .md-h { margin: 1.1em 0 0.4em; color: var(--color-heading); line-height: 1.25; text-wrap: balance; }
      .md-h1 { font-size: 1.35rem; }
      .md-h2 { font-size: 1.2rem; }
      .md-h3 { font-size: 1.05rem; }
      .md-h4, .md-h5, .md-h6 { font-size: 0.95rem; }
      .md-p { margin: 0.45em 0; }
      .md-list { margin: 0.35em 0; padding-left: 1.6em; }
      .md-list li { margin: 0.15em 0; }
      .md-list li > .md-p { margin: 0.2em 0; }
      .md-task { list-style: none; margin-left: -1.3em; }
      .md-task > input { margin: 0 0.4em 0 0; vertical-align: -0.1em; }
      .md-hr { border: 0; border-top: 1px solid var(--color-border); margin: 1em 0; }
      .md-code { padding: 0.05em 0.3em; border-radius: 4px; background: var(--color-surface-sunken); font-size: 0.9em; }
      .md-pre {
        margin: 0.5em 0;
        padding: 0.6em 0.8em;
        overflow-x: auto;
        border: 1px solid var(--color-border);
        border-radius: 6px;
        background: var(--color-surface-sunken);
        font-size: 0.85em;
        white-space: pre;
      }
      .md-mark { background: color-mix(in srgb, #facc15 45%, transparent); color: inherit; border-radius: 3px; padding: 0 0.1em; }
      .md-link { color: var(--color-link); }
      .md-wiki {
        padding: 0;
        border: 0;
        background: none;
        color: var(--color-link);
        font: inherit;
        text-decoration: underline dotted;
        cursor: pointer;
      }
      .md-wiki--missing { color: inherit; cursor: help; }
      .md-quote, .md-callout {
        margin: 0.6em 0;
        padding: 0.35em 0.9em;
        border-left: 3px solid var(--color-border-strong);
        background: var(--color-surface-sunken);
        border-radius: 0 6px 6px 0;
      }
      .md-callout { border-left-color: var(--color-primary); background: var(--color-accent-soft); }
      .md-callout__title { margin: 0.2em 0; font-weight: 600; text-transform: capitalize; }
      .md-table { max-width: 100%; overflow-x: auto; margin: 0.6em 0; }
      .md-table table { border-collapse: collapse; font-size: 0.88rem; }
      .md-table th, .md-table td {
        padding: 0.3em 0.6em;
        border: 1px solid var(--color-border);
        vertical-align: top;
        text-align: left;
      }
      .md-table th { background: var(--color-surface-sunken); color: var(--color-heading); }
      .md-img { display: block; max-width: 100%; height: auto; margin: 0.5em 0; border-radius: 6px; }
      .md-sound { display: inline-flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; }
      .md-sound audio { max-width: 100%; }
      .md-sound__name { color: var(--color-text-muted); font-size: 0.8rem; }
      .md-front { margin: 0 0 0.5em; color: var(--color-text-secondary); font-size: 0.82rem; }

      @media (max-width: 1000px) {
        .notes-body { grid-template-columns: minmax(0, 1fr); }
        .notes-list { max-height: 35vh; border-right: 0; border-bottom: 1px solid var(--color-border); }
        .notes-note { padding: 0.6rem 0.75rem 1.5rem; }
      }
    `}</style>
  );
}
