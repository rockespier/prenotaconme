/** Formatting for the public booking page, in the visitor's UI language. */

export function formatPrice(price: number, lang: string): string {
  return new Intl.NumberFormat(lang, { style: 'currency', currency: 'EUR' }).format(price);
}

/** "2026-12-02" → "mercoledì 2 dicembre". The date is a calendar day of the location, not an instant. */
export function formatDay(isoDate: string, lang: string, style: 'long' | 'short' = 'long'): string {
  const [y, m, d] = isoDate.split('-').map(Number);
  const utcNoon = new Date(Date.UTC(y, m - 1, d, 12));
  return new Intl.DateTimeFormat(lang, {
    timeZone: 'UTC',
    weekday: style,
    day: 'numeric',
    month: style === 'long' ? 'long' : 'short',
  }).format(utcNoon);
}

/** 90 → "1 h 30 min", 30 → "30 min" */
export function formatDuration(minutes: number): string {
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return [h ? `${h} h` : '', m ? `${m} min` : ''].filter(Boolean).join(' ');
}

/** Today's calendar date in the browser, as "YYYY-MM-DD" (the API drops past slots anyway). */
export function todayIso(now = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

export function addDaysIso(isoDate: string, days: number): string {
  const [y, m, d] = isoDate.split('-').map(Number);
  const date = new Date(Date.UTC(y, m - 1, d + days));
  return date.toISOString().slice(0, 10);
}
