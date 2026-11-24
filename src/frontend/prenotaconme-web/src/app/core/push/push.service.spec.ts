import { TestBed } from '@angular/core/testing';
import { SwPush } from '@angular/service-worker';
import { of } from 'rxjs';
import { API_CLIENT } from '../api/api-client';
import { PushService } from './push.service';

describe('PushService', () => {
  function setup(swEnabled: boolean, publicKey: string | null) {
    const calls: string[] = [];
    const api = {
      GET: async (path: string) => {
        calls.push(`GET ${path}`);
        return { data: { publicKey } };
      },
      PUT: async (path: string) => {
        calls.push(`PUT ${path}`);
        return {};
      },
    };
    const subscription = { endpoint: 'https://push.example/1', toJSON: () => ({ keys: { p256dh: 'k', auth: 'a' } }) };
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CLIENT, useValue: api },
        { provide: SwPush, useValue: { isEnabled: swEnabled, subscription: of(subscription) } },
      ],
    });
    return { service: TestBed.inject(PushService), calls };
  }

  it('is unsupported without a service worker', async () => {
    const { service, calls } = setup(false, 'key');
    await service.init();
    expect(['unsupported', 'ios']).toContain(service.state());
    expect(calls).toEqual([]);
  });

  it('is unsupported when the server has no VAPID key', async () => {
    const { service } = setup(true, null);
    await service.init();
    expect(service.state()).toBe('unsupported');
  });

  it('re-saves an existing subscription for the signed-in user', async () => {
    vi.stubGlobal('Notification', { permission: 'granted' });
    const { service, calls } = setup(true, 'key');
    await service.init();
    expect(service.state()).toBe('on');
    expect(calls).toContain('PUT /api/notifications/push/subscriptions');
    vi.unstubAllGlobals();
  });
});
