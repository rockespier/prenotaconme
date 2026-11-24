import { DayOfWeek, fromMinutes, hoursToRanges, rangesToHours, toApiTime, toHm, toMinutes } from './time';

describe('time helpers', () => {
  it('converts between API and UI formats', () => {
    expect(toHm('09:30:00')).toBe('09:30');
    expect(toApiTime('9:30')).toBe('09:30:00');
    expect(toMinutes('09:30')).toBe(570);
    expect(fromMinutes(570)).toBe('09:30');
  });

  it('merges consecutive hours into ranges, Monday first', () => {
    const cells = new Map<DayOfWeek, Set<number>>([
      ['Tuesday', new Set([14, 9, 10, 11])],
      ['Monday', new Set([18])],
    ]);

    expect(hoursToRanges(cells)).toEqual([
      { day: 'Monday', start: 18, end: 19 },
      { day: 'Tuesday', start: 9, end: 12 },
      { day: 'Tuesday', start: 14, end: 15 },
    ]);
  });

  it('round-trips ranges and cells', () => {
    const cells = rangesToHours([
      { day: 'Friday', start: '09:00:00', end: '12:00:00' },
      { day: 'Friday', start: '15:00:00', end: '16:00:00' },
    ]);

    expect([...cells.get('Friday')!].sort((a, b) => a - b)).toEqual([9, 10, 11, 15]);
  });
});
