import { Component, inject } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { TranslocoPipe } from '@jsverse/transloco';
import { SessionService } from '../../core/session/session.service';

@Component({
  selector: 'app-dashboard',
  imports: [MatCardModule, TranslocoPipe],
  template: `
    <h1>{{ 'nav.dashboard' | transloco }}</h1>
    @if (session.currentBusiness(); as business) {
      <mat-card appearance="outlined">
        <mat-card-header>
          <mat-card-title>{{ business.name }}</mat-card-title>
          <mat-card-subtitle>{{ 'dashboard.publicLink' | transloco }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <a [href]="'/b/' + business.slug" target="_blank" rel="noopener">{{ origin }}/b/{{ business.slug }}</a>
        </mat-card-content>
      </mat-card>
    }
  `,
})
export class Dashboard {
  protected readonly session = inject(SessionService);
  protected readonly origin = globalThis.location?.origin ?? '';
}
