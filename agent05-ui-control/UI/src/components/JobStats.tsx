import { useMemo } from 'react';
import {
  conversationBlocks,
  languageShareBlocks,
  statBlockLine,
  type JobLanguageShareReport,
  type StatBlock,
} from '../utils/languageShare';

interface JobStatsProps {
  report: JobLanguageShareReport | null | undefined;
  t: (key: string) => string;
}

/**
 * How much of a job's speech is Spanish and Russian and, for a dialogue, how the talk goes: a card for the
 * whole and one per participant. The cards stand side by side while there is room and go under each other
 * in a narrow window; the two kinds of cards stay two groups.
 */
export function JobStats({ report, t }: JobStatsProps) {
  const groups = useMemo(
    () =>
      [languageShareBlocks(report, t), conversationBlocks(report, t)].filter(
        (g) => g.length > 0
      ),
    [report, t]
  );
  if (groups.length === 0) return null;

  const card = (block: StatBlock, key: number) => (
    <li key={key} className="job-stats__card" title={statBlockLine(block)}>
      <div className="job-stats__head">
        <span className="job-stats__title">{block.title}</span>
        {block.lead && <span className="job-stats__lead">{block.lead}</span>}
      </div>
      {block.parts.length > 0 && (
        <ul className="job-stats__parts">
          {block.parts.map((part) => (
            <li key={part}>{part}</li>
          ))}
        </ul>
      )}
    </li>
  );

  return (
    <div className="job-stats" data-testid="job-language-share">
      {groups.map((group, i) => (
        <ul key={i} className="job-stats__group">
          {group.map(card)}
        </ul>
      ))}
      <style>{`
        .job-stats {
          display: flex;
          flex-wrap: wrap;
          gap: 0.5rem 0.75rem;
          margin: 0 0 1rem 0;
        }
        .job-stats__group {
          flex: 1 1 760px;
          min-width: 0;
          list-style: none;
          margin: 0;
          padding: 0;
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(min(100%, 240px), 1fr));
          gap: 0.5rem;
        }
        .job-stats__card {
          min-width: 0;
          /* A job with one card: the card does not stretch across the whole screen. */
          max-width: 760px;
          padding: 0.5rem 0.75rem;
          border: 1px solid var(--color-border);
          border-radius: 6px;
          background: var(--color-surface);
          font-size: 0.8125rem;
        }
        .job-stats__head { display: flex; flex-wrap: wrap; align-items: baseline; gap: 0 0.6rem; }
        .job-stats__title { font-weight: 600; color: var(--color-heading); }
        .job-stats__lead { color: var(--color-text-secondary); }
        .job-stats__parts {
          list-style: none;
          margin: 0.25rem 0 0 0;
          padding: 0;
          color: var(--color-text);
          line-height: 1.4;
        }
      `}</style>
    </div>
  );
}
