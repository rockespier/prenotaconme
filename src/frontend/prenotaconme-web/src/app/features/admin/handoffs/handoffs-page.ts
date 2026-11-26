import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { Notifier } from '../../../shared/api-errors';

type Handoff = Schemas['HandoffDto'];

/**
 * F3-06: chats where a client asked for a person. The assistant is paused there until the owner turns it back
 * on (or 24 hours pass); the page shows who to call and what they wrote.
 */
@Component({
  selector: 'app-handoffs-page',
  imports: [DatePipe, MatButtonModule, MatIconModule, TranslocoPipe],
  template: `
    <header class="page-header">
      <h1>{{ 'nav.handoffs' | transloco }}</h1>
    </header>
    <p class="muted intro">{{ 'handoffs.intro' | transloco }}</p>

    @for (h of handoffs(); track h.id) {
      <article class="card">
        <div class="head">
          <h2>{{ h.clientName || ('handoffs.unknownClient' | transloco) }}</h2>
          <span class="muted when">{{ h.handoffAt | date: 'd MMM, HH:mm' : undefined : lang() }}</span>
        </div>
        @if (h.reason) {
          <p class="reason"><strong>{{ 'handoffs.reason' | transloco }}:</strong> {{ h.reason }}</p>
        }
        <div class="contact">
          @if (h.clientPhone) {
            <a mat-stroked-button [href]="'tel:' + h.clientPhone"><mat-icon>call</mat-icon>{{ h.clientPhone }}</a>
          }
          @if (h.clientEmail) {
            <a mat-button [href]="'mailto:' + h.clientEmail"><mat-icon>mail</mat-icon>{{ h.clientEmail }}</a>
          }
        </div>
        @if (h.messages.length) {
          <details>
            <summary>{{ 'handoffs.chat' | transloco: { count: h.messages.length } }}</summary>
            <ol class="chat">
              @for (m of h.messages; track $index) {
                <li [class.client]="m.fromClient">{{ m.text }}</li>
              }
            </ol>
          </details>
        }
        <div class="foot">
          <span class="muted paused"><mat-icon inline>pause_circle</mat-icon>{{ 'handoffs.paused' | transloco }}</span>
          <span class="spacer"></span>
          <button mat-flat-button (click)="resume(h)" [disabled]="busy()">
            <mat-icon>smart_toy</mat-icon>{{ 'handoffs.resume' | transloco }}
          </button>
        </div>
      </article>
    } @empty {
      @if (loaded()) {
        <div class="empty">
          <mat-icon>support_agent</mat-icon>
          <p class="muted">{{ 'handoffs.empty' | transloco }}</p>
        </div>
      }
    }
  `,
  styles: `
    .intro { margin: calc(-1 * var(--space-3)) 0 var(--space-5); max-width: 640px; }
    .card {
      padding: var(--space-4) var(--space-5);
      margin-bottom: var(--space-3);
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: var(--radius-l);
      background: var(--mat-sys-surface);
    }
    .head { display: flex; align-items: baseline; gap: var(--space-3); flex-wrap: wrap; }
    h2 { margin: 0; font: var(--mat-sys-title-medium); font-weight: 600; flex: 1; }
    .when { font: var(--mat-sys-body-small); }
    .reason { margin: var(--space-2) 0; }
    .reason strong { font-weight: 600; }
    .contact { display: flex; flex-wrap: wrap; gap: var(--space-2); margin: var(--space-2) 0; }
    .contact a { overflow-wrap: anywhere; }
    details { margin: var(--space-2) 0; }
    summary { cursor: pointer; color: var(--mat-sys-primary); font: var(--mat-sys-label-large); }
    .chat { list-style: none; padding: 0; margin: var(--space-3) 0 0; display: flex; flex-direction: column; gap: var(--space-2); }
    .chat li {
      max-width: 85%;
      padding: var(--space-2) var(--space-3);
      border-radius: var(--radius-m, 12px);
      background: var(--mat-sys-surface-container);
      white-space: pre-line;
      align-self: flex-start;
    }
    .chat li.client { align-self: flex-end; background: var(--mat-sys-secondary-container); }
    .foot { display: flex; align-items: center; gap: var(--space-2); flex-wrap: wrap; margin-top: var(--space-3); }
    .paused { display: inline-flex; align-items: center; gap: var(--space-1); font: var(--mat-sys-body-small); }
    .spacer { flex: 1; }
    .empty { text-align: center; padding: var(--space-7) 0; }
    .empty mat-icon { font-size: 40px; width: 40px; height: 40px; color: var(--mat-sys-on-surface-variant); }
  `,
})
export class HandoffsPage implements OnInit {
  private readonly api = inject(API_CLIENT);
  private readonly notifier = inject(Notifier);
  private readonly transloco = inject(TranslocoService);

  protected readonly lang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });
  protected readonly handoffs = signal<Handoff[]>([]);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);

  ngOnInit(): void {
    void this.load();
  }

  protected async resume(h: Handoff): Promise<void> {
    this.busy.set(true);
    const { error } = await this.api.POST('/api/chatbot/handoffs/{id}/resume', { params: { path: { id: h.id } } });
    this.busy.set(false);
    if (error) this.notifier.error(error);
    await this.load();
  }

  private async load(): Promise<void> {
    const { data } = await this.api.GET('/api/chatbot/handoffs');
    this.handoffs.set(data ?? []);
    this.loaded.set(true);
  }
}
