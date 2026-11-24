import { inject, Injectable, signal } from '@angular/core';
import { SwPush } from '@angular/service-worker';
import { firstValueFrom } from 'rxjs';
import { API_CLIENT } from '../api/api-client';

/**
 * - unsupported: no service worker (dev server), no Push API, or push not configured on the server
 * - ios: iPhone/iPad Safari, where push needs the app added to the Home Screen first
 * - denied: the user blocked notifications for this site
 */
export type PushState = 'loading' | 'unsupported' | 'ios' | 'denied' | 'off' | 'on';

/**
 * F2-02: Web Push for the signed-in user on this device. The Angular service worker shows the notifications
 * (payload format "notification" + onActionClick), so there is no custom worker code.
 */
@Injectable({ providedIn: 'root' })
export class PushService {
  private readonly swPush = inject(SwPush);
  private readonly api = inject(API_CLIENT);

  readonly state = signal<PushState>('loading');
  readonly busy = signal(false);
  private publicKey: string | null = null;

  /** Reads the current state; an existing subscription is saved again so it belongs to the signed-in user. */
  async init(): Promise<void> {
    if (!this.swPush.isEnabled || !('Notification' in window)) {
      this.state.set(this.isIosBrowser() ? 'ios' : 'unsupported');
      return;
    }

    const { data } = await this.api.GET('/api/notifications/push/config');
    this.publicKey = data?.publicKey ?? null;
    if (!this.publicKey) {
      this.state.set('unsupported');
      return;
    }

    if (Notification.permission === 'denied') {
      this.state.set('denied');
      return;
    }

    const subscription = await firstValueFrom(this.swPush.subscription);
    if (subscription && Notification.permission === 'granted') {
      this.state.set((await this.save(subscription)) ? 'on' : 'off');
    } else {
      this.state.set('off');
    }
  }

  /** Asks the browser for permission and subscribes. Returns false when it did not work. */
  async enable(): Promise<boolean> {
    if (!this.publicKey) return false;
    this.busy.set(true);
    try {
      const subscription = await this.swPush.requestSubscription({ serverPublicKey: this.publicKey });
      const saved = await this.save(subscription);
      this.state.set(saved ? 'on' : 'off');
      return saved;
    } catch {
      this.state.set(Notification.permission === 'denied' ? 'denied' : 'off');
      return Notification.permission === 'denied';
    } finally {
      this.busy.set(false);
    }
  }

  async disable(): Promise<void> {
    this.busy.set(true);
    try {
      const subscription = await firstValueFrom(this.swPush.subscription);
      if (subscription) {
        await this.forget(subscription.endpoint);
        await this.swPush.unsubscribe();
      }
      this.state.set('off');
    } finally {
      this.busy.set(false);
    }
  }

  /** On sign-out: this device stops receiving the user's notifications (the browser keeps the permission). */
  async signOut(): Promise<void> {
    if (!this.swPush.isEnabled) return;
    const subscription = await firstValueFrom(this.swPush.subscription);
    if (subscription) await this.forget(subscription.endpoint);
    this.state.set('loading');
  }

  private async save(subscription: PushSubscription): Promise<boolean> {
    const json = subscription.toJSON();
    const { error } = await this.api.PUT('/api/notifications/push/subscriptions', {
      body: { endpoint: subscription.endpoint, p256dh: json.keys?.['p256dh'] ?? '', auth: json.keys?.['auth'] ?? '' },
    });
    return !error;
  }

  private async forget(endpoint: string): Promise<void> {
    await this.api.DELETE('/api/notifications/push/subscriptions', { params: { query: { endpoint } } });
  }

  private isIosBrowser(): boolean {
    return /iPhone|iPad|iPod/.test(navigator.userAgent) && !('PushManager' in window);
  }
}
