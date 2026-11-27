import { weeklyHours, workingHoursFromOpening } from './onboarding-hours';

describe('workingHoursFromOpening', () => {
  it('keeps whole hours inside the opening hours', () => {
    expect(
      workingHoursFromOpening('loc', [
        { day: 'Tuesday', opens: '09:30:00', closes: '19:00:00' },
        { day: 'Saturday', opens: '10:15:00', closes: '10:45:00' },
      ]),
    ).toEqual([{ locationId: 'loc', day: 'Tuesday', start: '10:00:00', end: '19:00:00' }]);
  });
});

describe('weeklyHours', () => {
  it('adds up the intervals', () => {
    expect(weeklyHours([{ start: '09:00:00', end: '13:00:00' }, { start: '14:00:00', end: '19:00:00' }])).toBe(9);
  });
});
