import type { Schemas } from '../core/api/api-client';

export type DayOfWeek = Schemas['DayOfWeek'];

/** Monday-first week, as shown in the UI (the API uses .NET DayOfWeek names). */
export const WEEK_DAYS: DayOfWeek[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

/** "09:00:00" → "09:00" */
export function toHm(time: string): string {
  return time.slice(0, 5);
}

/** "9:00" / "09:00" → "09:00:00" (API TimeOnly format). */
export function toApiTime(hm: string): string {
  const [h, m] = hm.split(':');
  return `${h.padStart(2, '0')}:${(m ?? '00').padStart(2, '0')}:00`;
}

/** "09:30" → 570 */
export function toMinutes(time: string): number {
  const [h, m] = time.split(':').map(Number);
  return h * 60 + (m || 0);
}

/** 570 → "09:30" */
export function fromMinutes(minutes: number): string {
  return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
}

export interface HourRange {
  day: DayOfWeek;
  start: number; // hour 0..23
  end: number; // exclusive, 1..24
}

/** Turns selected whole-hour cells into merged ranges: {Tue:[9,10,11,14]} → Tue 9–12, Tue 14–15. */
export function hoursToRanges(cells: Map<DayOfWeek, Set<number>>): HourRange[] {
  const ranges: HourRange[] = [];
  for (const day of WEEK_DAYS) {
    const hours = [...(cells.get(day) ?? [])].sort((a, b) => a - b);
    let start: number | null = null;
    let prev = -2;
    for (const h of hours) {
      if (start === null || h !== prev + 1) {
        if (start !== null) ranges.push({ day, start, end: prev + 1 });
        start = h;
      }
      prev = h;
    }
    if (start !== null) ranges.push({ day, start, end: prev + 1 });
  }
  return ranges;
}

/** Inverse of hoursToRanges for whole-hour ranges. */
export function rangesToHours(ranges: { day: DayOfWeek; start: string; end: string }[]): Map<DayOfWeek, Set<number>> {
  const cells = new Map<DayOfWeek, Set<number>>();
  for (const r of ranges) {
    const set = cells.get(r.day) ?? new Set<number>();
    for (let h = Math.floor(toMinutes(r.start) / 60); h < Math.ceil(toMinutes(r.end) / 60); h++) set.add(h);
    cells.set(r.day, set);
  }
  return cells;
}
