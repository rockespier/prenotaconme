import { addDaysIso, dayParts, formatDay, formatDuration, formatPrice, groupByPeriod, initials, todayIso } from './booking-format';

describe('booking formatting', () => {
  it('formats prices in euro for the UI language', () => {
    expect(formatPrice(20, 'it')).toMatch(/20,00\s?€/);
  });

  it('formats a calendar day without shifting it by time zone', () => {
    expect(formatDay('2026-12-02', 'it')).toBe('mercoledì 2 dicembre');
  });

  it('formats durations', () => {
    expect(formatDuration(30)).toBe('30 min');
    expect(formatDuration(60)).toBe('1 h');
    expect(formatDuration(90)).toBe('1 h 30 min');
  });

  it('handles ISO dates across month ends', () => {
    expect(addDaysIso('2026-12-30', 3)).toBe('2027-01-02');
    expect(todayIso(new Date(2026, 0, 5))).toBe('2026-01-05');
  });

  it('splits a day into chip parts', () => {
    expect(dayParts('2026-12-02', 'it')).toEqual({ weekday: 'mer', day: '2', month: 'dic' });
  });

  it('groups times into morning, afternoon and evening', () => {
    const groups = groupByPeriod([{ time: '09:00' }, { time: '11:30' }, { time: '14:00' }, { time: '18:30' }]);
    expect(groups.map((g) => [g.period, g.slots.length])).toEqual([
      ['morning', 2],
      ['afternoon', 1],
      ['evening', 1],
    ]);
  });

  it('builds initials', () => {
    expect(initials('Barberia Demo')).toBe('BD');
    expect(initials(' mario ')).toBe('M');
  });
});
