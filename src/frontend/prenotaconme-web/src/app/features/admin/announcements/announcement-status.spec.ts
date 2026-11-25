import { announcementStatus, formatPeriod } from './announcement-status';

describe('announcementStatus', () => {
  it('follows the inclusive date range', () => {
    expect(announcementStatus('2026-12-20', '2026-12-26', '2026-12-19')).toBe('scheduled');
    expect(announcementStatus('2026-12-20', '2026-12-26', '2026-12-20')).toBe('active');
    expect(announcementStatus('2026-12-20', '2026-12-26', '2026-12-26')).toBe('active');
    expect(announcementStatus('2026-12-20', '2026-12-26', '2026-12-27')).toBe('ended');
  });
});

describe('formatPeriod', () => {
  it('shows one day or a range', () => {
    expect(formatPeriod('2026-12-24', '2026-12-24', 'en-GB')).toBe('24 Dec');
    expect(formatPeriod('2026-12-24', '2027-01-06', 'en-GB')).toBe('24 Dec – 6 Jan');
  });
});
