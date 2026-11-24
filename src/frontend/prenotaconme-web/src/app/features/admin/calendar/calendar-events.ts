import type { EventInput } from '@fullcalendar/core';
import type { Schemas } from '../../../core/api/api-client';

type Appointment = Schemas['CalendarAppointmentDto'];
type Staff = Schemas['CalendarStaffDto'];

/** Distinct, readable colours for staff members (cycled by position in the list). */
export const STAFF_COLORS = ['#2563eb', '#db2777', '#059669', '#d97706', '#7c3aed', '#0891b2', '#dc2626', '#4d7c0f'];

export function staffColor(staff: readonly Staff[], staffId: string): string {
  const index = staff.findIndex((s) => s.id === staffId);
  return STAFF_COLORS[(index < 0 ? 0 : index) % STAFF_COLORS.length];
}

/** API appointments → FullCalendar events ("Anna · Taglio capelli", coloured by staff member). */
export function toEvents(appointments: readonly Appointment[], staff: readonly Staff[]): EventInput[] {
  return appointments.map((a) => {
    const color = staffColor(staff, a.staffMemberId);
    return {
      id: a.id,
      title: [a.clientName, a.serviceName].filter(Boolean).join(' · '),
      start: a.startsAt,
      end: a.endsAt,
      backgroundColor: color,
      borderColor: color,
      extendedProps: { appointment: a },
    };
  });
}

/** "2026-12-02" + "09:30" in the browser's time zone → ISO instant. */
export function localToIso(date: string, time: string): string {
  return new Date(`${date}T${time}`).toISOString();
}

/** Date → { date: "2026-12-02", time: "09:30" } in the browser's time zone (for form inputs). */
export function toLocalInputs(at: Date): { date: string; time: string } {
  const pad = (n: number) => String(n).padStart(2, '0');
  return {
    date: `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}`,
    time: `${pad(at.getHours())}:${pad(at.getMinutes())}`,
  };
}
