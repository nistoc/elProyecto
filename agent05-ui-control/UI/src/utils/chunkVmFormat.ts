import type { ChunkVirtualModelEntry } from '../types';

export function formatMmSs(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(totalSeconds));
  const m = Math.floor(s / 60);
  const sec = s % 60;
  return `${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}`;
}

export function elapsedSeconds(
  row: ChunkVirtualModelEntry,
  nowMs: number
): number | null {
  if (!row.startedAt) return null;
  const start = Date.parse(row.startedAt);
  if (Number.isNaN(start)) return null;
  const end = row.completedAt ? Date.parse(row.completedAt) : nowMs;
  if (Number.isNaN(end)) return null;
  return (end - start) / 1000;
}

/** BCP 47 tag for Date#toLocaleString */
export function localeTag(uiLocale: string): string {
  if (uiLocale === 'ru') return 'ru-RU';
  if (uiLocale === 'es') return 'es-ES';
  return 'en-US';
}

export function formatIsoDateTime(
  iso: string | null | undefined,
  uiLocale: string
): string {
  if (!iso?.trim()) return '';
  const ms = Date.parse(iso);
  if (Number.isNaN(ms)) return iso;
  return new Date(ms).toLocaleString(localeTag(uiLocale), {
    dateStyle: 'short',
    timeStyle: 'medium',
  });
}
