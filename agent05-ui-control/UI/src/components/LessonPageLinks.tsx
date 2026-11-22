import {
  dayPageHref,
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
  /** YYYY-MM-DD of the lesson day that has its page, and whether an edit is newer than it; nothing when none. */
  dayPage?: { date: string; stale: boolean } | null;
}

/**
 * What was made of a lesson: its package inside the app (a button), the page of its day that agent05
 * gives from the packages folder and the pages made of it elsewhere (links that open in a new tab).
 * Renders nothing for a lesson that has none of them. An address that is not http(s) is never made a link.
 * Its styles are in index.css: the jobs list draws one of these for every lesson.
 */
export function LessonPageLinks({
  pages,
  t,
  packageDate,
  onOpenPackage,
  notes,
  onOpenNotes,
  dayPage,
}: LessonPageLinksProps) {
  const links = openablePages(pages);
  const inApp = packageDate && onOpenPackage ? packageDate : null;
  const notesOf = notes && notes.notes > 0 && onOpenNotes ? notes : null;
  if (links.length === 0 && !inApp && !notesOf && !dayPage) return null;
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
      {dayPage && (
        <a
          className={`lesson-pages__link${dayPage.stale ? ' lesson-pages__link--stale' : ''}`}
          href={dayPageHref(dayPage.date)}
          target="_blank"
          rel="noopener noreferrer"
          onClick={(e) => e.stopPropagation()}
          title={
            dayPage.stale
              ? `${t('lessonDayPageOpenHint')}\n${t('lessonDayPageStale')}\npython -m builder page ${dayPage.date} --packages <packages>`
              : t('lessonDayPageOpenHint')
          }
          data-testid="lesson-day-page"
        >
          <span aria-hidden="true">📘</span>
          <span className="lesson-pages__name">{t('lessonDayPage')}</span>
          {dayPage.stale && (
            <span aria-hidden="true" data-testid="lesson-day-page-stale">
              ⚠
            </span>
          )}
          <span aria-hidden="true">↗</span>
        </a>
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
