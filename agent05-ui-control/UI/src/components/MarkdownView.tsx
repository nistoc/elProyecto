import { Fragment, type ReactNode } from 'react';
import { type Block, type Inline } from '../utils/markdown';

interface MarkdownViewProps {
  blocks: Block[];
  /** The address of a picture or a sound the text shows by name; null when it cannot be shown. */
  embedUrl: (name: string) => string | null;
  /** Opens another note the text links to; null when no such note is at hand (the link stays text). */
  onOpenNote?: (target: string) => (() => void) | null;
  /** Words of the screen: the title of the front matter, the hint of a link that leads nowhere. */
  t: (key: string) => string;
}

const IMAGE = /\.(png|jpe?g|gif|webp)$/i;
const SOUND = /\.(mp3|m4a|wav|ogg)$/i;

/** http(s) and mailto only: anything else of a note is never made a link. */
function safeHref(href: string): string | null {
  return /^(https?:|mailto:)/i.test(href) ? href : null;
}

/**
 * Draws a note of the vault from its tree (utils/markdown): React elements only, never HTML of the note.
 * Its styles are in LessonNotesView.
 */
export function MarkdownView({
  blocks,
  embedUrl,
  onOpenNote,
  t,
}: MarkdownViewProps) {
  const inline = (nodes: Inline[]): ReactNode =>
    nodes.map((n, i) => <Fragment key={i}>{inlineNode(n)}</Fragment>);

  const inlineNode = (n: Inline): ReactNode => {
    switch (n.t) {
      case 'text':
        return n.v;
      case 'br':
        return <br />;
      case 'code':
        return <code className="md-code">{n.v}</code>;
      case 'strong':
        return <strong>{inline(n.c)}</strong>;
      case 'em':
        return <em>{inline(n.c)}</em>;
      case 'del':
        return <del>{inline(n.c)}</del>;
      case 'mark':
        return <mark className="md-mark">{inline(n.c)}</mark>;
      case 'link': {
        const href = safeHref(n.href);
        if (href) {
          return (
            <a
              href={href}
              target="_blank"
              rel="noopener noreferrer"
              className="md-link"
            >
              {inline(n.c)}
            </a>
          );
        }
        // A link to another note of the vault by its file: opened like a wiki link.
        const target = decodeTarget(n.href);
        const open = target && onOpenNote ? onOpenNote(target) : null;
        return open ? (
          <button type="button" className="md-wiki" onClick={open}>
            {inline(n.c)}
          </button>
        ) : (
          <span
            className="md-wiki md-wiki--missing"
            title={t('notesLinkMissing')}
          >
            {inline(n.c)}
          </span>
        );
      }
      case 'wiki': {
        const open = onOpenNote ? onOpenNote(n.target) : null;
        return open ? (
          <button type="button" className="md-wiki" onClick={open}>
            {n.label}
          </button>
        ) : (
          <span
            className="md-wiki md-wiki--missing"
            title={t('notesLinkMissing')}
          >
            {n.label}
          </span>
        );
      }
      case 'embed': {
        const url = embedUrl(n.name);
        if (url && IMAGE.test(n.name)) {
          return (
            <img
              className="md-img"
              src={url}
              alt={n.label || n.name}
              loading="lazy"
            />
          );
        }
        if (url && SOUND.test(n.name)) {
          return (
            <span className="md-sound">
              <audio controls preload="none" src={url} />
              <span className="md-sound__name">{n.label || n.name}</span>
            </span>
          );
        }
        const open = onOpenNote ? onOpenNote(n.name) : null;
        return open ? (
          <button type="button" className="md-wiki" onClick={open}>
            📄 {n.label || n.name}
          </button>
        ) : (
          <span
            className="md-wiki md-wiki--missing"
            title={t('notesLinkMissing')}
          >
            📎 {n.label || n.name}
          </span>
        );
      }
    }
  };

  const block = (b: Block, key: number): ReactNode => {
    switch (b.t) {
      case 'heading': {
        const Tag = `h${Math.min(6, b.level + 1)}` as 'h2';
        return (
          <Tag key={key} className={`md-h md-h${b.level}`}>
            {inline(b.c)}
          </Tag>
        );
      }
      case 'paragraph':
        return (
          <p key={key} className="md-p">
            {inline(b.c)}
          </p>
        );
      case 'hr':
        return <hr key={key} className="md-hr" />;
      case 'code':
        return (
          <pre key={key} className="md-pre">
            <code>{b.v}</code>
          </pre>
        );
      case 'frontmatter':
        return (
          <details key={key} className="md-front">
            <summary>{t('notesProperties')}</summary>
            <pre className="md-pre">{b.v}</pre>
          </details>
        );
      case 'quote':
        return (
          <blockquote
            key={key}
            className={
              b.callout ? `md-callout md-callout--${b.callout}` : 'md-quote'
            }
          >
            {b.callout && (
              <p className="md-callout__title">
                {b.title ? inline(b.title) : b.callout}
              </p>
            )}
            {b.c.map(block)}
          </blockquote>
        );
      case 'list': {
        const items = b.items.map((it, i) => (
          <li key={i} className={it.task === null ? undefined : 'md-task'}>
            {it.task !== null && (
              <input
                type="checkbox"
                checked={it.task}
                readOnly
                tabIndex={-1}
                aria-hidden="true"
              />
            )}
            {it.c.length === 1 && it.c[0].t === 'paragraph'
              ? inline(it.c[0].c)
              : it.c.map(block)}
          </li>
        ));
        return b.ordered ? (
          <ol
            key={key}
            className="md-list"
            start={b.start === 1 ? undefined : b.start}
          >
            {items}
          </ol>
        ) : (
          <ul key={key} className="md-list">
            {items}
          </ul>
        );
      }
      case 'table':
        return (
          <div key={key} className="md-table">
            <table>
              <thead>
                <tr>
                  {b.head.map((c, i) => (
                    <th
                      key={i}
                      style={
                        b.align[i] ? { textAlign: b.align[i]! } : undefined
                      }
                    >
                      {inline(c)}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {b.rows.map((row, r) => (
                  <tr key={r}>
                    {b.head.map((_, i) => (
                      <td
                        key={i}
                        style={
                          b.align[i] ? { textAlign: b.align[i]! } : undefined
                        }
                      >
                        {row[i] ? inline(row[i]) : null}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        );
    }
  };

  return <div className="md">{blocks.map(block)}</div>;
}

/** The note a relative link names: its file name without `.md`, percent-decoding undone. */
function decodeTarget(href: string): string | null {
  if (/^[a-z][a-z0-9+.-]*:/i.test(href)) return null;
  let name = href.split('#')[0].split(/[\\/]/).pop() ?? '';
  try {
    name = decodeURIComponent(name);
  } catch {
    // stays as written
  }
  name = name.replace(/\.md$/i, '').trim();
  return name || null;
}
