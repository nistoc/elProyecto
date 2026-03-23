function refinerDebugEnabled(): boolean {
  try {
    const v = globalThis.localStorage?.getItem('xtractDebugRefiner')?.trim();
    return v === '1' || v?.toLowerCase() === 'true';
  } catch {
    return false;
  }
}

/**
 * Logs to the console when localStorage `xtractDebugRefiner` is `1` or `true` (reload after setting).
 * Uses console.log (not .debug) so messages show with default DevTools filters — Verbose/Debug is often hidden.
 */
export function refinerUiDebug(...args: unknown[]): void {
  if (!refinerDebugEnabled()) return;
  console.log('[refiner-ui]', new Date().toISOString(), ...args);
}
