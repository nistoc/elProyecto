import { useState, type KeyboardEvent, type ReactNode } from 'react';

interface EditableTextProps {
  /** The text as it reads now. */
  value: string;
  /**
   * What the builder had here, when the owner wrote the text anew; undefined — the text is the builder's own.
   */
  was?: string | null;
  /** A long text: edited in a box of several lines. */
  multiline?: boolean;
  /** How the text is drawn when it is not being edited; the plain text when absent. */
  display?: ReactNode;
  /** Shown by the pointer over the text. */
  hint?: string;
  /**
   * Writes the text; null — back to what the builder had. Resolves false when the text was not saved:
   * the box then stays open with what was typed.
   */
  onSave: (text: string | null) => Promise<boolean>;
  t: (key: string) => string;
}

/**
 * A text of a lesson package the owner may write anew in place: a pencil opens a box, Enter saves, Esc
 * leaves it. A text written anew is underlined, tells what stood there before and can be put back.
 * Its styles are in LessonPackageView.
 */
export function EditableText({
  value,
  was,
  multiline,
  display,
  hint,
  onSave,
  t,
}: EditableTextProps) {
  const [draft, setDraft] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const edited = was !== undefined;

  const save = async (text: string | null) => {
    if (busy) return;
    if (text !== null && text.trim() === value.trim()) {
      setDraft(null);
      return;
    }
    setBusy(true);
    const saved = await onSave(text === null ? null : text.trim());
    setBusy(false);
    if (saved) setDraft(null);
  };

  const onKeyDown = (e: KeyboardEvent<HTMLElement>) => {
    if (e.key === 'Escape') {
      e.preventDefault();
      setDraft(null);
    } else if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      void save(draft);
    }
  };

  if (draft !== null) {
    return (
      <span className="pkg-edit pkg-edit--on">
        {multiline ? (
          <textarea
            className="pkg-edit__box"
            value={draft}
            rows={Math.min(8, Math.max(2, Math.ceil(draft.length / 60)))}
            autoFocus
            disabled={busy}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={onKeyDown}
          />
        ) : (
          <input
            className="pkg-edit__box"
            type="text"
            value={draft}
            autoFocus
            disabled={busy}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={onKeyDown}
          />
        )}
        <span className="pkg-edit__actions">
          <button
            type="button"
            className="pkg-btn pkg-btn--primary"
            disabled={busy}
            onClick={() => void save(draft)}
          >
            {t('pkgSave')}
          </button>
          <button
            type="button"
            className="pkg-btn"
            disabled={busy}
            onClick={() => setDraft(null)}
          >
            {t('pkgCancel')}
          </button>
        </span>
      </span>
    );
  }

  const title = edited
    ? `${t('pkgWas')}: ${was?.trim() ? was : '—'}${hint ? `\n\n${hint}` : ''}`
    : hint;
  return (
    <span className={edited ? 'pkg-edit pkg-edit--edited' : 'pkg-edit'}>
      <span
        className="pkg-edit__text"
        title={title}
        onDoubleClick={() => setDraft(value)}
      >
        {display ??
          (value !== '' ? value : <span className="pkg-muted">—</span>)}
      </span>
      <button
        type="button"
        className="pkg-edit__btn"
        onClick={() => setDraft(value)}
        title={t('pkgEdit')}
        aria-label={t('pkgEdit')}
      >
        ✎
      </button>
      {edited && (
        <button
          type="button"
          className="pkg-edit__btn pkg-edit__btn--revert"
          disabled={busy}
          onClick={() => void save(null)}
          title={`${t('pkgRevert')}: ${was?.trim() ? was : '—'}`}
          aria-label={t('pkgRevert')}
        >
          ↺
        </button>
      )}
    </span>
  );
}
