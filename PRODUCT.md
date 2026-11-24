# Product

## Register

product

## Users
- **Clients** of barbers, hairdressers and beauty salons in Italy. They open the business's link (`/{slug}`) on their phone, often from WhatsApp or Instagram, mid-day, in daylight, wanting to book in under a minute. Any age; many are not tech-savvy.
- **Salon owners (Admin) and staff**: small businesses (1–10 people). They manage locations, services, staff hours and appointments from a phone or a tablet at the counter, between clients, in bright shop lighting. They are not software people: labels must be plain Italian.

## Product Purpose
Online appointment booking for salons (a Reservio-like SaaS). Success: a client books without help or a phone call; an owner sets up the business in minutes and trusts the calendar. The public booking page is the priority surface: it carries the salon's name and must convert. The backoffice is clean and functional.

## Brand Personality
Modern, practical, trustworthy. Direct, fast, app-like (think Booksy/Fresha), minimal decoration. Friendly Italian voice ("Scegli giorno e ora", not jargon). Calm confidence rather than excitement.

## Anti-references
- Generic blue SaaS admin template (the stock Angular Material azure look).
- Kitsch salon clichés: stock photos of scissors, gold, marble or leather textures, ornaments.
- Dark / neon / developer-tool aesthetics.
- A visual copy of Reservio.

## Design Principles
1. **One obvious next step.** Every booking screen has a single primary action; progress is always visible.
2. **Thumb-first.** Built for one hand on a phone: large targets, primary action within reach, no hover-only affordances.
3. **Plain words over UI.** Clear Italian labels and helpful empty states do more than decoration.
4. **The salon is the hero.** On public pages the business name leads; PrenotaConMe stays in the background.
5. **Consistent and quiet.** Same components and states everywhere; color marks actions and selection, not decoration.

## Accessibility & Inclusion
WCAG 2.2 AA: text contrast ≥ 4.5:1, touch targets ≥ 44 px, visible focus, full keyboard support, `prefers-reduced-motion` respected. Default language Italian (also es/en); layouts must survive longer translations.
