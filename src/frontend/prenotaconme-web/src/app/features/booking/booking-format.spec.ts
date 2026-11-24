import { addDaysIso, formatDay, formatDuration, formatPrice, todayIso } from './booking-format';

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
});
