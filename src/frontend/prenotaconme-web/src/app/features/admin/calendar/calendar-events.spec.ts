import { localToIso, STAFF_COLORS, staffColor, toEvents, toLocalInputs } from './calendar-events';

const staff = [
  { id: 'a', displayName: 'Mario', isActive: true, locationIds: [], serviceIds: [] },
  { id: 'b', displayName: 'Luigi', isActive: true, locationIds: [], serviceIds: [] },
];

describe('calendar events', () => {
  it('colours by staff position', () => {
    expect(staffColor(staff, 'b')).toBe(STAFF_COLORS[1]);
    expect(staffColor(staff, 'unknown')).toBe(STAFF_COLORS[0]);
  });

  it('maps appointments to events', () => {
    const [event] = toEvents(
      [
        {
          id: '1', locationId: 'l', serviceId: 's', serviceName: 'Taglio', price: 20, staffMemberId: 'b',
          startsAt: '2026-12-02T08:00:00Z', endsAt: '2026-12-02T08:30:00Z', status: 'Confirmed',
          clientName: 'Anna', clientEmail: null, clientPhone: null, isGuest: true,
        },
      ],
      staff,
    );
    expect(event.title).toBe('Anna · Taglio');
    expect(event.backgroundColor).toBe(STAFF_COLORS[1]);
    expect(event.start).toBe('2026-12-02T08:00:00Z');
  });

  it('round-trips local form inputs', () => {
    const at = new Date(2026, 11, 2, 9, 30);
    const inputs = toLocalInputs(at);
    expect(inputs).toEqual({ date: '2026-12-02', time: '09:30' });
    expect(localToIso(inputs.date, inputs.time)).toBe(at.toISOString());
  });
});
