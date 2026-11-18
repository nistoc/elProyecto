import {
  lessonPageHref,
  lessonPageLabel,
  openablePages,
  type LessonPage,
} from '../utils/lessons';

interface LessonPageLinksProps {
  pages: readonly LessonPage[] | null | undefined;
  t: (key: string) => string;
}

/**
 * The pages made of a lesson (the lesson package) as links that open in a new tab. Renders nothing for
 * a lesson without a page. An address that is not http(s) is never made a link.
 * Its styles are in index.css: the jobs list draws one of these for every lesson.
 */
export function LessonPageLinks({ pages, t }: LessonPageLinksProps) {
  const links = openablePages(pages);
  if (links.length === 0) return null;
  return (
    <span className="lesson-pages" data-testid="lesson-pages">
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
