import {
  lessonPageHref,
  lessonPageLabel,
  openablePages,
  type LessonPage,
} from '../utils/lessons';

interface LessonPageLinksProps {
  pages: readonly LessonPage[] | null | undefined;
  t: (key: string) => string;
  /** YYYY-MM-DD of the lesson when it has a package the app itself shows; nothing otherwise. */
  packageDate?: string | null;
  /** Opens the package of a lesson inside the app. */
  onOpenPackage?: (date: string) => void;
  /** YYYY-MM-DD of the lesson and how many notes its vault copy holds; nothing when it holds none. */
  notes?: { date: string; notes: number; homework: number } | null;
  /** Opens the notes of a lesson (homework first) inside the app. */
  onOpenNotes?: (date: string) => void;
}

/**
 * What was made of a lesson: its package inside the app (a button) and the pages made of it elsewhere
 * (links that open in a new tab). Renders nothing for a lesson that has neither. An address that is not
 * http(s) is never made a link.
 * Its styles are in index.css: the jobs list draws one of these for every lesson.
 */
export function LessonPageLinks({
  pages,
  t,
  packageDate,
  onOpenPackage,
  notes,
  onOpenNotes,
}: LessonPageLinksProps) {
  const links = openablePages(pages);
  const inApp = packageDate && onOpenPackage ? packageDate : null;
  const notesOf = notes && notes.notes > 0 && onOpenNotes ? notes : null;
  if (links.length === 0 && !inApp && !notesOf) return null;
  return (
    <span className="lesson-pages" data-testid="lesson-pages">
      {inApp && (
        <button
          type="button"
          className="lesson-pages__link lesson-pages__link--app"
          onClick={(e) => {
            e.stopPropagation();
            onOpenPackage?.(inApp);
          }}
          title={t('lessonPackageOpenHint')}
          data-testid="lesson-package-open"
        >
          <span aria-hidden="true">📗</span>
          <span className="lesson-pages__name">{t('lessonPackage')}</span>
        </button>
      )}
      {notesOf && (
        <button
          type="button"
          className="lesson-pages__link lesson-pages__link--app"
          onClick={(e) => {
            e.stopPropagation();
            onOpenNotes?.(notesOf.date);
          }}
          title={t('lessonNotesOpenHint')}
          data-testid="lesson-notes-open"
        >
          <span aria-hidden="true">📝</span>
          <span className="lesson-pages__name">
            {notesOf.homework > 0
              ? `${t('lessonNotesButton')} ${notesOf.homework}`
              : `${t('notesAll')} ${notesOf.notes}`}
          </span>
        </button>
      )}
      {links.map((page) => (
        <a
          key={page.url}
          className="lesson-pages__link"
          href={lessonPageHref(page) ?? undefined}
          target="_blank"
          rel="noopener noreferrer"
          title={`${t('lessonPageOpenHint')}\n${page.url}`}
        >
          <span aria-hidden="true">📘</span>
          <span className="lesson-pages__name">
            {lessonPageLabel(page, t('lessonPage'))}
          </span>
          <span aria-hidden="true">↗</span>
        </a>
      ))}
    </span>
  );
}
