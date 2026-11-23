import { Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/** Public booking page for a business: /b/:slug. Booking flow arrives in F1-10. */
@Component({
  selector: 'app-booking-page',
  imports: [TranslocoPipe],
  template: `
    <h1>{{ 'booking.title' | transloco }}</h1>
    <p>{{ 'booking.comingSoon' | transloco: { slug: slug() } }}</p>
  `,
})
export class BookingPage {
  readonly slug = input.required<string>();
}
