import { Component, inject, input, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';
import { PushService } from '../../core/push/push.service';

/** F2-02: turn push notifications on/off for this device. Hidden where push cannot work. */
@Component({
  selector: 'app-push-toggle',
  imports: [MatButtonModule, MatIconModule, TranslocoPipe],
  template: `
    @let state = push.state();
    @if (state !== 'loading' && state !== 'unsupported') {
      <section class="push" [class.on]="state === 'on'">
        <mat-icon class="lead" aria-hidden="true">{{ state === 'on' ? 'notifications_active' : 'notifications' }}</mat-icon>
        <div class="text">
          <strong>{{ 'push.title' | transloco }}</strong>
          <span class="muted">
            @switch (state) {
              @case ('on') { {{ 'push.on' | transloco }} }
              @case ('denied') { {{ 'push.denied' | transloco }} }
              @case ('ios') { {{ 'push.ios' | transloco }} }
              @default { {{ (audience() === 'staff' ? 'push.staff' : 'push.client') | transloco }} }
            }
          </span>
          @if (failed()) {
            <span class="error" role="alert">{{ 'push.error' | transloco }}</span>
          }
        </div>
        @if (state === 'off') {
          <button mat-flat-button (click)="enable()" [disabled]="push.busy()">{{ 'push.enable' | transloco }}</button>
        } @else if (state === 'on') {
          <button mat-button (click)="push.disable()" [disabled]="push.busy()">{{ 'push.disable' | transloco }}</button>
        }
      </section>
    }
  `,
  styles: `
    .push {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      margin: 0 0 var(--space-5);
      padding: var(--space-3) var(--space-4);
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: var(--radius-l);
    }
    .push.on { background: color-mix(in srgb, var(--mat-sys-primary) 5%, transparent); }
    .lead { color: var(--mat-sys-primary); flex: none; }
    .text { flex: 1; display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .muted { font: var(--mat-sys-body-small); }
    .error { font: var(--mat-sys-body-small); color: var(--mat-sys-error); }
    button { flex: none; }
    @media (max-width: 599px) {
      .push { flex-wrap: wrap; }
      button { margin-left: auto; }
    }
  `,
})
export class PushToggle implements OnInit {
  protected readonly push = inject(PushService);

  /** Which explanation to show: clients hear about their bookings, staff about the calendar. */
  readonly audience = input<'client' | 'staff'>('client');

  protected readonly failed = signal(false);

  ngOnInit(): void {
    void this.push.init();
  }

  protected async enable(): Promise<void> {
    this.failed.set(!(await this.push.enable()));
  }
}
