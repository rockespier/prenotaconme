import type { Schemas } from '../../../core/api/api-client';
import { fromMinutes, toApiTime, toMinutes } from '../../../shared/time';

type OpeningHours = Schemas['OpeningHoursDto'];
type WorkingHours = Schemas['WorkingHoursDto'];

/**
 * F5-02: staff hours equal to the opening hours of a location, cut to whole hours (staff work in 1-hour blocks:
 * open 9:30–19:00 becomes 10:00–19:00). Intervals shorter than an hour are dropped.
 */
export function workingHoursFromOpening(locationId: string, opening: OpeningHours[]): WorkingHours[] {
  return opening.flatMap((h) => {
    const start = Math.ceil(toMinutes(h.opens) / 60) * 60;
    const end = Math.floor(toMinutes(h.closes) / 60) * 60;
    return end > start ? [{ locationId, day: h.day, start: toApiTime(fromMinutes(start)), end: toApiTime(fromMinutes(end)) }] : [];
  });
}

/** Total weekly hours of a schedule (for the summary next to each person). */
export function weeklyHours(hours: { start: string; end: string }[]): number {
  return hours.reduce((sum, h) => sum + (toMinutes(h.end) - toMinutes(h.start)) / 60, 0);
}
