# Design

Visual system of PrenotaConMe. Strategy and audience are in `PRODUCT.md`.

## Theme
Light only. Restrained color: pure white surfaces, one deep indigo brand color for primary actions, selection and progress. Nothing decorative is colored.

- Angular Material 3, theme in `src/frontend/prenotaconme-web/src/styles.scss`; palettes generated from primary `#2a3c97` and tertiary `#00858d` (`src/theme/_theme-colors.scss`, `ng generate @angular/material:theme-color`).
- Use Material system tokens (`--mat-sys-*`), never hard-coded colors:
  - `primary` / `on-primary`: primary buttons, selected day, progress, brand mark.
  - `primary-container`: hover on time slots, staff avatars, the public-link panel.
  - `tertiary-container`: success (confirmed booking, completed setup step, "Collegato").
  - `error-container`: inline alerts (full border + tint, never a side stripe).
  - `surface-container-low`: side nav, row hover; `outline-variant`: hairlines and panel borders.

## Typography
One family: **Figtree** (400/500/600/700), loaded in `index.html`, set as Material plain and brand family. Fixed rem scale from Material (`--mat-sys-*` fonts). h1 = headline-small 700, h2 = title-large 650, h3 = title-medium 600. Numbers (prices, times, dates) use `tabular-nums`.

## Tokens (`styles.scss`)
- Spacing: `--space-1..7` = 4, 8, 12, 16, 24, 32, 48 px.
- Radii: `--radius-s` 8 (time slots), `--radius-m` 12 (day chips, alerts), `--radius-l` 16 (panels, lists, tables).
- Motion: `--duration` 180 ms, `--ease-out` (quint). State changes only; `prefers-reduced-motion` disables all.
- `--touch` 48 px minimum target.

## Components and patterns
- **Choice list** (booking): one bordered panel with divided rows (min 72 px), chevron at the end. Not separate cards.
- **Day chips**: 60×76 px, weekday / number / month; selected = primary fill; unavailable = grey, struck-through number.
- **Time slots**: grid of 48 px buttons grouped Mattina / Pomeriggio / Sera.
- **Progress**: "Passo n di N" + 4 px bar, back button on the left.
- **Primary CTA on phones**: full width, sticky at the bottom with safe-area padding.
- **Summary**: bordered definition list with icons.
- **Backoffice page**: `.page-header` (h1 + primary action, optional description), tables inside `.table-card`, secondary columns `.hide-sm` under 720 px, `*matNoDataRow` empty state, status as `.pill` (`.ok` for positive).
- **Loading**: skeletons (`.sk`), not spinners in content.
- **Empty states**: icon + one sentence that says what to do next.
