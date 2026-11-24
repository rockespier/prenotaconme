/** Short date in the browser's time zone: "2 dic 2026" (null → "—"). */
export function shortDate(iso: string | null | undefined, lang: string): string {
  if (!iso) return '—';
  return new Intl.DateTimeFormat(lang, { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(iso));
}

/** Date and time of an appointment in its location's time zone: "mer 2 dic 2026, 09:30". */
export function appointmentDate(iso: string, timeZone: string, lang: string): string {
  return new Intl.DateTimeFormat(lang, {
    timeZone, weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
  }).format(new Date(iso));
}
