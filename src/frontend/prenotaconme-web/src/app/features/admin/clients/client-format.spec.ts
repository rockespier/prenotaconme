import { appointmentDate, shortDate } from './client-format';

describe('client formatting', () => {
  it('shows a dash for missing dates', () => {
    expect(shortDate(null, 'it')).toBe('—');
  });

  it('formats an appointment in the location time zone', () => {
    expect(appointmentDate('2026-12-02T08:30:00Z', 'Europe/Rome', 'it')).toContain('09:30');
  });
});
