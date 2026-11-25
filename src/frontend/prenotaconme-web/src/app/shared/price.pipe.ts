import { inject, Pipe, PipeTransform } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { formatPrice } from '../features/booking/booking-format';

/**
 * Euro amount in the active UI language ("14,00 €" in Italian), like the public pages. Angular's currency pipe
 * would use the app locale (en-US, "€14.00"). Impure so a language switch updates it.
 */
@Pipe({ name: 'price', pure: false })
export class PricePipe implements PipeTransform {
  private readonly transloco = inject(TranslocoService);

  transform(value: number | null | undefined): string {
    return value == null ? '' : formatPrice(value, this.transloco.getActiveLang());
  }
}
