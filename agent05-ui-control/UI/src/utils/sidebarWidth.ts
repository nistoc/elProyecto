/**
 * Width of the jobs list at the left edge. The operator drags its edge; the width is kept in the browser,
 * and the wider the list is, the more it tells about each job.
 */
export const SIDEBAR_WIDTH_KEY = 'xtract-sidebar-width';
export const SIDEBAR_DEFAULT_WIDTH = 280;
export const SIDEBAR_MIN_WIDTH = 200;
export const SIDEBAR_MAX_WIDTH = 1400;
/** The list never takes more than this part of the window: the job view has to stay usable. */
export const SIDEBAR_MAX_PART = 0.6;
/** One press of an arrow key on the edge; with Shift it is four of them. */
export const SIDEBAR_KEY_STEP = 24;

/** The widest the list may be in a window of this width. */
export function sidebarMaxWidth(viewportWidth: number): number {
  if (!Number.isFinite(viewportWidth) || viewportWidth <= 0)
    return SIDEBAR_MAX_WIDTH;
  const byPart = Math.floor(viewportWidth * SIDEBAR_MAX_PART);
  return Math.max(SIDEBAR_MIN_WIDTH, Math.min(SIDEBAR_MAX_WIDTH, byPart));
}

/** A width the list can really have in a window of this width, in whole pixels. */
export function clampSidebarWidth(
  width: number,
  viewportWidth: number
): number {
  if (!Number.isFinite(width)) width = SIDEBAR_DEFAULT_WIDTH;
  return Math.round(
    Math.min(sidebarMaxWidth(viewportWidth), Math.max(SIDEBAR_MIN_WIDTH, width))
  );
}

/** What the browser kept from the last visit; null when it kept nothing that reads as a width. */
export function parseStoredSidebarWidth(
  raw: string | null | undefined
): number | null {
  if (raw == null || raw.trim() === '') return null;
  const n = Number(raw);
  return Number.isFinite(n) && n > 0 ? n : null;
}

/**
 * How much the list tells about a job at this width:
 * 0 — name on one line, state, one date (the list as it was);
 * 1 — the whole name on two lines and the phase;
 * 2 — also the job id, both dates and what the lessons list knows of the job
 *     (lesson, track or dialogue, model, share of Spanish and Russian);
 * 3 — the same as a table, a column for each.
 */
export type SidebarDetail = 0 | 1 | 2 | 3;

export const SIDEBAR_DETAIL_FROM: readonly [number, number, number] = [
  380, 560, 900,
];

export function sidebarDetail(width: number): SidebarDetail {
  if (width >= SIDEBAR_DETAIL_FROM[2]) return 3;
  if (width >= SIDEBAR_DETAIL_FROM[1]) return 2;
  if (width >= SIDEBAR_DETAIL_FROM[0]) return 1;
  return 0;
}
