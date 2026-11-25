export type AnnouncementStatus = 'active' | 'scheduled' | 'ended';

/** Where an announcement stands on `today` (ISO dates compare as strings). */
export function announcementStatus(startsOn: string, endsOn: string, today: string): AnnouncementStatus {
  if (today < startsOn) return 'scheduled';
  if (today > endsOn) return 'ended';
  return 'active';
}

/** "20 dic – 2 gen" (or a single day), in the UI language. */
export function formatPeriod(startsOn: string, endsOn: string, lang: string): string {
  const fmt = new Intl.DateTimeFormat(lang, { timeZone: 'UTC', day: 'numeric', month: 'short' });
  const toDate = (iso: string) => {
    const [y, m, d] = iso.split('-').map(Number);
    return new Date(Date.UTC(y, m - 1, d, 12));
  };
  return startsOn === endsOn ? fmt.format(toDate(startsOn)) : `${fmt.format(toDate(startsOn))} – ${fmt.format(toDate(endsOn))}`;
}
