import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { fetchLessonPackage, putLessonPackageEdit } from '../api';
import {
  countWordRows,
  dayLabel,
  playFromSec,
  tagLabel,
  trackAudioUrl,
  whoLabel,
  wordRows,
  type LessonPackageView as PackageView,
  type PackageEdit,
  type PackageGrammar,
  type PackagePlace,
} from '../utils/lessonPackage';
import { EditableText } from './EditableText';
import { LessonPackageWords } from './LessonPackageWords';

interface LessonPackageViewProps {
  /** YYYY-MM-DD of the lesson. */
  date: string;
  t: (key: string) => string;
  onClose: () => void;
  /** Opens the job of a recording (its transcript) in the main view. */
  onOpenJob: (jobId: string) => void;
}

type Tab = 'words' | 'grammar' | 'topics' | 'examples';
const TABS: readonly Tab[] = ['words', 'grammar', 'topics', 'examples'];
const TAB_LABEL: Record<Tab, string> = {
  words: 'pkgTabWords',
  grammar: 'pkgTabGrammar',
  topics: 'pkgTabTopics',
  examples: 'pkgTabExamples',
};

/**
 * The package of a lesson inside the app: its words, grammar, topics and examples, read from the packages
 * folder through the API. Which words are new is counted by the API against every earlier lesson each
 * time the package is opened; every text can be written anew in place (the edit is kept next to the
 * package, the package itself is not changed), and a place of the recording can be listened to.
 */
export function LessonPackageView({
  date,
  t,
  onClose,
  onOpenJob,
}: LessonPackageViewProps) {
  const [state, setState] = useState<{
    date: string;
    view: PackageView | null;
    error: string | null;
  } | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [tab, setTab] = useState<Tab>('words');
  const [showLeftOut, setShowLeftOut] = useState(false);
  const [playing, setPlaying] = useState<{ period: number; at: string } | null>(
    null
  );
  const audioRef = useRef<HTMLAudioElement | null>(null);
  /** Where the playing is to start once the audio of the track is known: the place asked for last. */
  const playFrom = useRef<number | null>(null);
  /** The edits go to the service one at a time, so the answer shown last is the one of the last edit. */
  const edits = useRef<Promise<unknown>>(Promise.resolve());

  useEffect(() => {
    let cancelled = false;
    fetchLessonPackage(date)
      .then((view) => {
        if (!cancelled) setState({ date, view, error: null });
      })
      .catch((e: unknown) => {
        if (!cancelled) {
          setState({
            date,
            view: null,
            error: e instanceof Error ? e.message : String(e),
          });
        }
      });
    return () => {
      cancelled = true;
    };
  }, [date]);

  // The package of the lesson that was open before is not shown under the next one.
  const current = state && state.date === date ? state : null;
  const view = current?.view ?? null;

  const onEdit = useCallback(
    (id: string, change: PackageEdit): Promise<boolean> => {
      const saved = edits.current.then(async () => {
        try {
          const fresh = await putLessonPackageEdit(date, id, change);
          // Another lesson may have been opened meanwhile: its package is not put under this answer.
          setState((shown) =>
            shown && shown.date !== date
              ? shown
              : { date, view: fresh, error: null }
          );
          setSaveError(null);
          return true;
        } catch (e) {
          setSaveError(e instanceof Error ? e.message : String(e));
          return false;
        }
      });
      edits.current = saved;
      return saved;
    },
    [date]
  );

  const periods = useMemo(() => view?.package.periods ?? [], [view]);
  const canPlay = useCallback(
    (period: number) => !!periods.find((p) => p.n === period)?.audioTrack,
    [periods]
  );
  const onPlay = useCallback(
    (period: number, sec: number | null) => {
      const audio = audioRef.current;
      const from = playFromSec(sec);
      const track = periods.find((p) => p.n === period)?.audioTrack;
      if (!audio || from === null || !track) return;
      playFrom.current = from;
      const start = () => {
        if (playFrom.current === null) return;
        audio.currentTime = playFrom.current;
        void audio.play().catch(() => {
          // The browser refused to play (no gesture, no codec): the player stays, the owner presses it.
        });
      };
      if (audio.dataset.track !== track) {
        audio.dataset.track = track;
        // The owner asked to play: with "none" some browsers open nothing, and the start would never come.
        audio.preload = 'metadata';
        audio.src = trackAudioUrl(track);
        audio.addEventListener('loadedmetadata', start, { once: true });
        audio.load();
      } else if (audio.readyState >= HTMLMediaElement.HAVE_METADATA) {
        start();
      }
      // Else the track is still being opened: it starts from the place asked for last.
      const m = Math.floor(from / 60);
      const s = Math.floor(from % 60);
      setPlaying({ period, at: `${m}:${String(s).padStart(2, '0')}` });
    },
    [periods]
  );

  const words = useMemo(
    () => (view ? countWordRows(wordRows(view.package)) : null),
    [view]
  );

  if (!current) {
    return (
      <div className="pkg" data-testid="lesson-package">
        <p className="pkg-loading">{t('pkgLoading')}</p>
        <PackageStyles />
      </div>
    );
  }
  if (!view || !words) {
    return (
      <div className="pkg" data-testid="lesson-package">
        <div className="pkg-head">
          <h2 className="pkg-title">
            {t('lessonPackage')} · {dayLabel(date)}
          </h2>
          <button type="button" className="pkg-btn" onClick={onClose}>
            {t('pkgBack')}
          </button>
        </div>
        <p className="pkg-error" role="alert">
          {t('pkgLoadFailed')}: {current.error}
        </p>
        <PackageStyles />
      </div>
    );
  }

  const pkg = view.package;
  const newTotal =
    words.newByList.new + words.newByList.vocabulary + words.newByList.code;
  const wordsTotal =
    words.byList.new + words.byList.vocabulary + words.byList.code;
  const visible = <T extends { leftOut?: boolean }>(items: T[]) =>
    showLeftOut ? items : items.filter((x) => !x.leftOut);
  const tabCount: Record<Tab, number> = {
    words: wordsTotal,
    grammar: visible(pkg.grammar).length + visible(pkg.wordNotes).length,
    topics: visible(pkg.topics).length,
    examples: visible(pkg.examples).length,
  };

  const place = (p: PackagePlace) => (
    <span className="pkg-place">
      <button
        type="button"
        className="pkg-play"
        disabled={p.sec === null || p.sec === undefined || !canPlay(p.period)}
        onClick={() => onPlay(p.period, p.sec ?? null)}
        title={canPlay(p.period) ? t('pkgPlay') : t('pkgNoAudio')}
        aria-label={t('pkgPlay')}
      >
        ▶
      </button>
      <span className="pkg-place__at">
        {p.period} · {p.at || '—'}
      </span>
      <span className="pkg-sub">{whoLabel(p.who, t)}</span>
    </span>
  );

  const leaveOut = (item: { id: string; leftOut?: boolean }) => (
    <button
      type="button"
      className="pkg-btn pkg-btn--quiet"
      onClick={() => void onEdit(item.id, { leftOut: !item.leftOut })}
      title={t('pkgLeaveOutHint')}
    >
      {item.leftOut ? t('pkgPutBack') : t('pkgLeaveOut')}
    </button>
  );

  const grammarCard = (g: PackageGrammar) => (
    <article
      key={g.id}
      className={g.leftOut ? 'pkg-card pkg-card--out' : 'pkg-card'}
      data-testid="package-grammar-card"
    >
      <header className="pkg-card__head">
        <h4 className="pkg-card__title">
          <EditableText
            value={g.topic ?? ''}
            was={g.edited?.topic}
            t={t}
            onSave={(text) => onEdit(g.id, { fields: { topic: text } })}
          />
        </h4>
        <span className="pkg-card__meta">
          {g.kind ? (
            <span className="pkg-tag">{tagLabel(g.kind, t)}</span>
          ) : null}
          <span className="pkg-sub">
            {t('pkgRecording')} {g.period}
          </span>
          {leaveOut(g)}
        </span>
      </header>
      <div className="pkg-card__rule">
        <EditableText
          value={g.rule ?? ''}
          was={g.edited?.rule}
          multiline
          t={t}
          onSave={(text) => onEdit(g.id, { fields: { rule: text } })}
        />
      </div>
      {g.examples.length > 0 && (
        <ul className="pkg-quotes">
          {g.examples.map((x) => (
            <li key={x.id} className="pkg-quotes__li">
              {place(x)}
              <span className="pkg-quote">
                <EditableText
                  value={x.quote ?? ''}
                  was={x.edited?.quote}
                  multiline
                  hint={
                    x.said && x.said !== x.quote ? `«${x.said}»` : undefined
                  }
                  t={t}
                  onSave={(text) => onEdit(x.id, { fields: { quote: text } })}
                />
              </span>
            </li>
          ))}
        </ul>
      )}
      {g.parts.length > 1 && (
        <details className="pkg-parts">
          <summary>
            {t('pkgParts')}: {g.parts.length}
          </summary>
          <ul>
            {g.parts.map((part, i) => (
              <li key={i}>
                <b>{part.topic}</b> — {part.rule}
              </li>
            ))}
          </ul>
        </details>
      )}
    </article>
  );

  return (
    <div className="pkg" data-testid="lesson-package">
      <div className="pkg-head">
        <div className="pkg-head__main">
          <h2 className="pkg-title">
            {t('lessonPackage')} · {dayLabel(view.date)}
          </h2>
          <span className="pkg-periods">
            {periods.map((p) => (
              <button
                key={p.n}
                type="button"
                className="pkg-btn"
                disabled={!p.job}
                onClick={() => p.job && onOpenJob(p.job)}
                title={t('pkgOpenJob')}
              >
                {t('pkgRecording')} {p.n}
                {p.start ? ` · ${p.start}` : ''}
                {p.endAt ? ` · ${p.endAt}` : ''}
              </button>
            ))}
          </span>
        </div>
        <button
          type="button"
          className="pkg-btn"
          onClick={onClose}
          data-testid="package-close"
        >
          {t('pkgBack')}
        </button>
      </div>

      <div className="pkg-facts">
        <span
          className="pkg-fact pkg-fact--new"
          data-testid="package-new-count"
        >
          <b>{newTotal}</b> {t('pkgFactNew')}
          <span className="pkg-sub">
            {t('pkgFactOf')} {wordsTotal}
          </span>
        </span>
        <span className="pkg-fact">
          {view.compared.lessons > 0 ? (
            <>
              {t('pkgCompared')}: <b>{view.compared.lessons}</b>
              <span className="pkg-sub">
                {dayLabel(view.compared.from)} — {dayLabel(view.compared.to)}
              </span>
            </>
          ) : (
            t('pkgComparedNone')
          )}
        </span>
        <span className="pkg-fact">
          {t('pkgEdits')}: <b>{view.counts.edited}</b>
          <span className="pkg-sub">
            {t('pkgLeftOutCount')}: {view.counts.leftOut}
          </span>
        </span>
        {view.counts.stale > 0 && (
          <span className="pkg-fact pkg-fact--warn">
            {view.counts.stale} {t('pkgStale')}
          </span>
        )}
        <label className="pkg-check">
          <input
            type="checkbox"
            checked={showLeftOut}
            onChange={(e) => setShowLeftOut(e.target.checked)}
          />
          {t('pkgShowLeftOut')}
        </label>
        <span className="pkg-sub pkg-facts__built">
          {t('pkgBuilt')}: {pkg.builtAt?.slice(0, 10) ?? '—'}
          {pkg.requests ? ` · ${t('pkgRequests')}: ${pkg.requests}` : ''}
          {pkg.costUsd ? ` · $${pkg.costUsd.toFixed(2)}` : ''}
        </span>
      </div>

      {saveError && (
        <p className="pkg-error" role="alert" data-testid="package-save-error">
          {t('pkgSaveFailed')}: {saveError}
        </p>
      )}

      <div className="pkg-tabs" role="tablist">
        {TABS.map((id) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={tab === id}
            className={tab === id ? 'pkg-tab on' : 'pkg-tab'}
            onClick={() => setTab(id)}
            data-testid={`package-tab-${id}`}
          >
            {t(TAB_LABEL[id])} <b>{tabCount[id]}</b>
          </button>
        ))}
      </div>

      <div className="pkg-body">
        {tab === 'words' && (
          <LessonPackageWords
            pkg={pkg}
            comparedLessons={view.compared.lessons}
            showLeftOut={showLeftOut}
            t={t}
            onEdit={onEdit}
            onPlay={onPlay}
            canPlay={canPlay}
          />
        )}

        {tab === 'grammar' && (
          <>
            <div className="pkg-cards">
              {visible(pkg.grammar).map(grammarCard)}
            </div>
            {visible(pkg.wordNotes).length > 0 && (
              <>
                <h3 className="pkg-h3">{t('pkgWordNotes')}</h3>
                <div className="pkg-cards">
                  {visible(pkg.wordNotes).map(grammarCard)}
                </div>
              </>
            )}
            {tabCount.grammar === 0 && (
              <p className="pkg-empty">{t('pkgEmpty')}</p>
            )}
          </>
        )}

        {tab === 'topics' && (
          <div className="pkg-columns">
            {periods.map((p) => (
              <section key={p.n} className="pkg-column">
                <h3 className="pkg-h3">
                  {t('pkgRecording')} {p.n}
                  {p.start ? ` · ${p.start}` : ''}
                </h3>
                <ol className="pkg-topics">
                  {visible(pkg.topics)
                    .filter((x) => x.period === p.n)
                    .map((topic) => (
                      <li
                        key={topic.id}
                        className={
                          topic.leftOut
                            ? 'pkg-topic pkg-topic--out'
                            : 'pkg-topic'
                        }
                      >
                        <span className="pkg-place">
                          <button
                            type="button"
                            className="pkg-play"
                            disabled={
                              topic.fromSec === null ||
                              topic.fromSec === undefined ||
                              !canPlay(p.n)
                            }
                            onClick={() => onPlay(p.n, topic.fromSec ?? null)}
                            title={
                              canPlay(p.n) ? t('pkgPlay') : t('pkgNoAudio')
                            }
                            aria-label={t('pkgPlay')}
                          >
                            ▶
                          </button>
                          <span className="pkg-place__at">
                            {topic.fromAt ?? '—'}–{topic.toAt ?? '—'}
                          </span>
                        </span>
                        <div className="pkg-topic__text">
                          <div className="pkg-topic__title">
                            <EditableText
                              value={topic.title ?? ''}
                              was={topic.edited?.title}
                              t={t}
                              onSave={(text) =>
                                onEdit(topic.id, { fields: { title: text } })
                              }
                            />
                          </div>
                          <div className="pkg-topic__summary">
                            <EditableText
                              value={topic.summary ?? ''}
                              was={topic.edited?.summary}
                              multiline
                              t={t}
                              onSave={(text) =>
                                onEdit(topic.id, { fields: { summary: text } })
                              }
                            />
                          </div>
                        </div>
                        {leaveOut(topic)}
                      </li>
                    ))}
                </ol>
              </section>
            ))}
          </div>
        )}

        {tab === 'examples' && (
          <div className="pkg-table-wrap">
            <table className="pkg-table pkg-table--examples">
              <thead>
                <tr>
                  <th style={{ width: 200 }}>{t('pkgColPlace')}</th>
                  <th>{t('pkgColPhrase')}</th>
                  <th>{t('pkgColRu')}</th>
                  <th>{t('pkgColShows')}</th>
                  <th style={{ width: 96 }} />
                </tr>
              </thead>
              <tbody>
                {visible(pkg.examples).map((x) => (
                  <tr
                    key={x.id}
                    className={x.leftOut ? 'pkg-row pkg-row--out' : 'pkg-row'}
                  >
                    <td>{place(x)}</td>
                    <td className="pkg-phrase">
                      <EditableText
                        value={x.quote ?? ''}
                        was={x.edited?.quote}
                        multiline
                        hint={
                          x.said && x.said !== x.quote
                            ? `«${x.said}»`
                            : undefined
                        }
                        t={t}
                        onSave={(text) =>
                          onEdit(x.id, { fields: { quote: text } })
                        }
                      />
                    </td>
                    <td>
                      <EditableText
                        value={x.ru ?? ''}
                        was={x.edited?.ru}
                        multiline
                        t={t}
                        onSave={(text) =>
                          onEdit(x.id, { fields: { ru: text } })
                        }
                      />
                    </td>
                    <td>
                      <EditableText
                        value={x.shows ?? ''}
                        was={x.edited?.shows}
                        multiline
                        t={t}
                        onSave={(text) =>
                          onEdit(x.id, { fields: { shows: text } })
                        }
                      />
                    </td>
                    <td>{leaveOut(x)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {tabCount.examples === 0 && (
              <p className="pkg-empty">{t('pkgEmpty')}</p>
            )}
          </div>
        )}
      </div>

      <div className={playing ? 'pkg-player on' : 'pkg-player'}>
        <span className="pkg-player__label">
          {playing
            ? `${t('pkgRecording')} ${playing.period} · ${playing.at}`
            : t('pkgPlayerIdle')}
        </span>
        <audio
          ref={audioRef}
          controls
          preload="none"
          className="pkg-player__audio"
        />
      </div>
      <PackageStyles />
    </div>
  );
}

/** The styles of the package view and of what it is made of (the words table, the editable text). */
function PackageStyles() {
  return (
    <style>{`
      .pkg {
        flex: 1;
        min-height: 0;
        display: flex;
        flex-direction: column;
        gap: 0.6rem;
        color: var(--color-text);
        font-size: 0.9rem;
      }
      .pkg-loading { margin: 2rem 0; color: var(--color-text-secondary); }
      .pkg-error {
        margin: 0;
        padding: 0.5rem 0.75rem;
        border: 1px solid var(--color-danger, #c62828);
        border-radius: 6px;
        background: color-mix(in srgb, var(--color-danger, #c62828) 10%, var(--color-surface));
      }
      .pkg-head { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 0.75rem; }
      .pkg-head__main { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem 1rem; min-width: 0; }
      .pkg-title { margin: 0; font-size: 1.2rem; color: var(--color-heading); font-variant-numeric: tabular-nums; }
      .pkg-periods { display: inline-flex; flex-wrap: wrap; gap: 0.35rem; font-variant-numeric: tabular-nums; }
      .pkg-h3 { margin: 0.9rem 0 0.5rem; font-size: 1rem; color: var(--color-heading); }
      .pkg-muted, .pkg-sub { color: var(--color-text-muted); }
      .pkg-sub { display: block; font-size: 0.78rem; line-height: 1.3; }
      .pkg-btn {
        padding: 0.2rem 0.6rem;
        border: 1px solid var(--color-border-strong);
        border-radius: 6px;
        background: var(--color-surface);
        color: var(--color-text);
        font-size: 0.8rem;
        cursor: pointer;
        white-space: nowrap;
      }
      .pkg-btn:hover:not(:disabled) { background: var(--color-surface-hover); }
      .pkg-btn:disabled { opacity: 0.5; cursor: default; }
      .pkg-btn--primary { background: var(--color-primary); color: var(--color-on-primary); border-color: var(--color-primary); }
      .pkg-btn--primary:hover:not(:disabled) { background: var(--color-primary-hover); }
      .pkg-btn--quiet { border-color: transparent; background: transparent; color: var(--color-text-muted); }

      /* The facts of the lesson: a band of cells that wraps in a narrow window. */
      .pkg-facts { display: flex; flex-wrap: wrap; align-items: stretch; gap: 0.5rem; }
      .pkg-fact {
        padding: 0.35rem 0.75rem;
        border: 1px solid var(--color-border);
        border-radius: 8px;
        background: var(--color-surface);
        font-variant-numeric: tabular-nums;
      }
      .pkg-fact b { font-size: 1.05rem; }
      .pkg-fact--new { border-color: var(--color-primary); background: var(--color-accent-soft); }
      .pkg-fact--new b { font-size: 1.35rem; }
      .pkg-fact--warn { border-color: var(--color-warning, #b8860b); }
      .pkg-check { display: inline-flex; align-items: center; gap: 0.35rem; font-size: 0.8rem; color: var(--color-text-secondary); cursor: pointer; }
      .pkg-facts__built { align-self: center; margin-left: auto; }

      .pkg-tabs { display: flex; flex-wrap: wrap; gap: 0.25rem; border-bottom: 1px solid var(--color-border); }
      .pkg-tab {
        padding: 0.4rem 0.9rem;
        border: 1px solid transparent;
        border-bottom: none;
        border-radius: 8px 8px 0 0;
        background: transparent;
        color: var(--color-text-secondary);
        font-size: 0.9rem;
        cursor: pointer;
      }
      .pkg-tab b { margin-left: 0.25rem; font-weight: 600; font-variant-numeric: tabular-nums; }
      .pkg-tab:hover { background: var(--color-surface-hover); }
      .pkg-tab.on { border-color: var(--color-border); background: var(--color-surface); color: var(--color-heading); font-weight: 600; }

      /* The table scrolls inside itself, so its headings stay in sight; the other tabs scroll as a whole. */
      .pkg-body { flex: 1; min-height: 0; overflow: auto; display: flex; flex-direction: column; }
      .pkg-body > * { flex-shrink: 0; }
      .pkg-body > .pkg-words, .pkg-body > .pkg-table-wrap { flex: 1 1 auto; min-height: 16rem; }
      .pkg-words { display: flex; flex-direction: column; gap: 0.5rem; }
      .pkg-words > .pkg-table-wrap { flex: 1 1 auto; min-height: 0; }
      .pkg-words__note { margin: 0; font-size: 0.8rem; }
      .pkg-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem 0.75rem; }
      .pkg-toolbar__shown { margin-left: auto; font-variant-numeric: tabular-nums; }
      .pkg-seg { display: inline-flex; border: 1px solid var(--color-border-strong); border-radius: 8px; overflow: hidden; }
      .pkg-seg__btn { padding: 0.35rem 0.8rem; border: none; background: var(--color-surface); color: var(--color-text); font-size: 0.85rem; cursor: pointer; }
      .pkg-seg__btn + .pkg-seg__btn { border-left: 1px solid var(--color-border-strong); }
      .pkg-seg__btn.on { background: var(--color-primary); color: var(--color-on-primary); }
      .pkg-seg__btn b { margin-left: 0.3rem; font-variant-numeric: tabular-nums; }
      .pkg-chips { display: inline-flex; flex-wrap: wrap; gap: 0.3rem; }
      .pkg-chip {
        display: inline-block;
        padding: 0.15rem 0.6rem;
        border: 1px solid var(--color-border-strong);
        border-radius: 999px;
        background: transparent;
        color: var(--color-text-muted);
        font-size: 0.78rem;
        cursor: pointer;
        white-space: nowrap;
      }
      .pkg-chip.on { color: var(--color-text); background: var(--color-surface); }
      .pkg-chip--new.on { border-color: #2e7d32; box-shadow: inset 3px 0 0 #2e7d32; }
      .pkg-chip--vocabulary.on { border-color: #1565c0; box-shadow: inset 3px 0 0 #1565c0; }
      .pkg-chip--code.on { border-color: #b26a00; box-shadow: inset 3px 0 0 #b26a00; }
      .pkg-chip--codeOther.on { border-color: var(--color-text-muted); box-shadow: inset 3px 0 0 var(--color-text-muted); }
      .pkg-chip--still { cursor: default; }
      .pkg-chip b { margin-left: 0.2rem; font-variant-numeric: tabular-nums; }
      .pkg-search, .pkg-select {
        padding: 0.3rem 0.55rem;
        border: 1px solid var(--color-border-strong);
        border-radius: 6px;
        background: var(--color-surface);
        color: var(--color-text);
        font-size: 0.85rem;
      }
      .pkg-search { flex: 0 1 22rem; min-width: 10rem; }

      .pkg-table-wrap { overflow: auto; border: 1px solid var(--color-border); border-radius: 8px; background: var(--color-surface); }
      .pkg-table-wrap--drag { user-select: none; cursor: col-resize; }
      .pkg-table { width: 100%; min-width: 1100px; border-collapse: collapse; table-layout: fixed; }
      .pkg-table th {
        position: sticky;
        top: 0;
        z-index: 1;
        padding: 0.4rem 0.6rem;
        border-bottom: 1px solid var(--color-border-strong);
        background: var(--color-surface-sunken);
        color: var(--color-text-secondary);
        font-size: 0.78rem;
        font-weight: 600;
        text-align: left;
        white-space: nowrap;
      }
      .pkg-th { padding: 0; border: none; background: none; color: inherit; font: inherit; cursor: pointer; }
      .pkg-th:hover { color: var(--color-heading); }
      .pkg-th__dir { display: inline-block; width: 1em; margin-left: 0.25rem; font-size: 0.65rem; }
      .pkg-th__grip { position: absolute; top: 0; right: -3px; width: 7px; height: 100%; cursor: col-resize; touch-action: none; }
      .pkg-th__grip:hover, .pkg-table-wrap--drag .pkg-th__grip { background: var(--color-primary); opacity: 0.4; }
      .pkg-table td { padding: 0.45rem 0.6rem; border-bottom: 1px solid var(--color-border); vertical-align: top; overflow-wrap: anywhere; }
      .pkg-row:hover td { background: var(--color-surface-hover); }
      .pkg-row--out td, .pkg-card--out, .pkg-topic--out { opacity: 0.5; }
      .pkg-word { font-size: 1.02rem; font-weight: 600; color: var(--color-heading); }
      .pkg-phrase { font-style: italic; line-height: 1.4; }
      .pkg-mark { padding: 0 0.1em; border-radius: 3px; background: color-mix(in srgb, var(--color-primary) 22%, transparent); color: inherit; font-weight: 600; }
      .pkg-unheard { display: inline-block; margin-left: 0.35rem; padding: 0 0.4rem; border-radius: 999px; border: 1px solid var(--color-warning, #b8860b); color: var(--color-warning, #b8860b); font-size: 0.75rem; font-weight: 700; cursor: help; vertical-align: middle; }
      .pkg-chip--unheard.on { border-color: var(--color-warning, #b8860b); box-shadow: inset 3px 0 0 var(--color-warning, #b8860b); }
      .pkg-badge-new { display: inline-block; padding: 0.05rem 0.5rem; border-radius: 999px; background: #2e7d32; color: #fff; font-size: 0.75rem; font-weight: 600; }
      .pkg-place { display: inline-flex; flex-wrap: wrap; align-items: baseline; gap: 0.15rem 0.4rem; font-variant-numeric: tabular-nums; white-space: nowrap; }
      .pkg-place .pkg-sub { flex-basis: 100%; }
      .pkg-held { color: var(--color-warning, #b8860b); }
      .pkg-play {
        width: 1.6rem;
        height: 1.6rem;
        padding: 0;
        border: 1px solid var(--color-border-strong);
        border-radius: 50%;
        background: var(--color-surface);
        color: var(--color-link);
        font-size: 0.65rem;
        line-height: 1;
        cursor: pointer;
      }
      .pkg-play:hover:not(:disabled) { background: var(--color-accent-soft); border-color: var(--color-primary); }
      .pkg-play:disabled { opacity: 0.35; cursor: default; }
      .pkg-empty { margin: 0; padding: 1.5rem; text-align: center; color: var(--color-text-muted); }

      /* A text the owner may write anew in place. */
      .pkg-edit { display: inline; }
      .pkg-edit__btn {
        margin-left: 0.25rem;
        padding: 0 0.2rem;
        border: none;
        background: none;
        color: var(--color-link);
        font-size: 0.85em;
        font-style: normal;
        font-weight: 400;
        cursor: pointer;
        opacity: 0;
      }
      .pkg-edit:hover .pkg-edit__btn, .pkg-edit__btn:focus-visible, .pkg-edit__btn--revert { opacity: 1; }
      .pkg-edit--edited > .pkg-edit__text { text-decoration: underline dotted var(--color-primary); text-underline-offset: 3px; }
      .pkg-edit--on { display: flex; flex-direction: column; gap: 0.3rem; font-style: normal; font-weight: 400; }
      .pkg-edit__box {
        width: 100%;
        box-sizing: border-box;
        padding: 0.3rem 0.45rem;
        border: 2px solid var(--color-primary);
        border-radius: 6px;
        background: var(--color-surface);
        color: var(--color-text);
        font: inherit;
        font-size: 0.9rem;
        resize: vertical;
      }
      .pkg-edit__actions { display: inline-flex; gap: 0.3rem; }

      /* Grammar: cards in as many columns as the window has room for. */
      .pkg-cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(100%, 520px), 1fr)); gap: 0.75rem; }
      .pkg-card { padding: 0.75rem 0.9rem; border: 1px solid var(--color-border); border-radius: 8px; background: var(--color-surface); }
      .pkg-card__head { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 0.4rem 0.75rem; }
      .pkg-card__title { margin: 0; font-size: 1rem; color: var(--color-heading); }
      .pkg-card__meta { display: inline-flex; align-items: baseline; gap: 0.5rem; white-space: nowrap; }
      .pkg-card__meta .pkg-sub { display: inline; }
      .pkg-card__rule { margin: 0.4rem 0 0.5rem; line-height: 1.45; }
      .pkg-tag { padding: 0.05rem 0.5rem; border: 1px solid var(--color-border-strong); border-radius: 999px; font-size: 0.75rem; color: var(--color-text-secondary); }
      .pkg-quotes { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 0.35rem; }
      .pkg-quotes__li { display: grid; grid-template-columns: 9.5rem 1fr; gap: 0.5rem; align-items: baseline; }
      .pkg-quote { font-style: italic; }
      .pkg-parts { margin-top: 0.5rem; font-size: 0.82rem; color: var(--color-text-secondary); }
      .pkg-parts summary { cursor: pointer; }
      .pkg-parts ul { margin: 0.3rem 0 0; padding-left: 1.1rem; }

      /* Topics: the recordings side by side on a wide screen, under each other in a narrow window. */
      .pkg-columns { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 460px), 1fr)); gap: 0 1.25rem; align-items: start; }
      .pkg-topics { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 0.5rem; }
      .pkg-topic { display: grid; grid-template-columns: 8.5rem 1fr auto; gap: 0.6rem; align-items: baseline; padding: 0.55rem 0.7rem; border: 1px solid var(--color-border); border-radius: 8px; background: var(--color-surface); }
      .pkg-topic__title { font-weight: 600; color: var(--color-heading); }
      .pkg-topic__summary { margin-top: 0.2rem; color: var(--color-text-secondary); line-height: 1.4; }
      .pkg-table--examples { min-width: 900px; }

      .pkg-player { flex-shrink: 0; display: flex; flex-wrap: wrap; align-items: center; gap: 0.75rem; padding: 0.35rem 0.6rem; border: 1px solid var(--color-border); border-radius: 8px; background: var(--color-surface-sunken); }
      .pkg-player__label { font-size: 0.8rem; color: var(--color-text-secondary); font-variant-numeric: tabular-nums; white-space: nowrap; }
      .pkg-player.on .pkg-player__label { color: var(--color-heading); font-weight: 600; }
      .pkg-player__audio { flex: 1 1 320px; min-width: 0; height: 2rem; }
    `}</style>
  );
}
