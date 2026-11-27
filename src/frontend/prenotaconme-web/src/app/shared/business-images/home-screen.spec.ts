import { TestBed } from '@angular/core/testing';
import { HomeScreen } from './home-screen';

describe('HomeScreen', () => {
  const head = (selector: string, attribute: string) => document.head.querySelector(selector)?.getAttribute(attribute);

  it('shows the business while its booking page is open and goes back to the platform after', () => {
    const homeScreen = TestBed.inject(HomeScreen);

    homeScreen.show({ name: 'Barbiere Navigli', slug: 'barbiere-navigli', iconUrl: '/api/tenancy/tenants/barbiere-navigli/images/icon?v=1' });

    expect(document.title).toBe('Barbiere Navigli');
    expect(head('meta[name="apple-mobile-web-app-title"]', 'content')).toBe('Barbiere Navigli');
    expect(head('link[rel="manifest"]', 'href')).toContain('/api/tenancy/tenants/barbiere-navigli/manifest');
    expect(head('link[rel="apple-touch-icon"]', 'href')).toContain('/images/icon?v=1');

    homeScreen.reset();

    expect(document.title).toBe('PrenotaConMe');
    expect(head('link[rel="manifest"]', 'href')).toBe('manifest.webmanifest');
    expect(head('link[rel="apple-touch-icon"]', 'href')).toBe('apple-touch-icon.png');
  });

  it('uses the platform icon when the business has no logo', () => {
    TestBed.inject(HomeScreen).show({ name: 'Senza logo', slug: 'senza-logo', iconUrl: null });

    expect(head('link[rel="apple-touch-icon"]', 'href')).toBe('apple-touch-icon.png');
  });
});
