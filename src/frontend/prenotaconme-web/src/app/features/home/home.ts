import { Component, inject, OnInit, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { TranslocoPipe } from '@jsverse/transloco';
import { API_CLIENT } from '../../core/api/api-client';

type ApiStatus = 'checking' | 'online' | 'offline';

@Component({
  selector: 'app-home',
  imports: [MatCardModule, TranslocoPipe],
  template: `
    <h1>{{ 'home.title' | transloco }}</h1>
    <p>{{ 'home.subtitle' | transloco }}</p>
    <mat-card appearance="outlined">
      <mat-card-content>
        {{ 'home.apiStatus' | transloco }}: <strong>{{ 'home.status.' + status() | transloco }}</strong>
        @if (version()) {
          <span>(v{{ version() }})</span>
        }
      </mat-card-content>
    </mat-card>
  `,
})
export class Home implements OnInit {
  private readonly api = inject(API_CLIENT);

  protected readonly status = signal<ApiStatus>('checking');
  protected readonly version = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      const { data } = await this.api.GET('/api/info');
      this.version.set(data?.version ?? null);
      this.status.set(data ? 'online' : 'offline');
    } catch {
      this.status.set('offline');
    }
  }
}
