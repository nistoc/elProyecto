/**
 * Opt-in traces for periodic UI updates (polling, SSE, file refetch cascade).
 * In DevTools console: localStorage.setItem('xtract_debug_refresh', '1'); location.reload()
 * Disable: localStorage.removeItem('xtract_debug_refresh'); location.reload()
 */
export function isUiRefreshDebugEnabled(): boolean {
  try {
    return localStorage.getItem('xtract_debug_refresh') === '1';
  } catch {
    return false;
  }
}

export function logUiRefresh(
  source: string,
  detail?: Record<string, unknown>
): void {
  if (!isUiRefreshDebugEnabled()) return;
  const line = `[xtract-ui-refresh] ${source}`;
  if (detail && Object.keys(detail).length > 0) console.info(line, detail);
  else console.info(line);
}
