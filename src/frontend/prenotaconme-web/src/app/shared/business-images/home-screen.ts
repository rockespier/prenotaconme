import { DOCUMENT, Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { assetUrl } from '../../core/api/api-client';

const PLATFORM = { name: 'PrenotaConMe', manifest: 'manifest.webmanifest', icon: 'apple-touch-icon.png' };

/**
 * F5-10: while a booking page is open, "Add to Home Screen" (iOS reads these tags at that moment, Android reads the
 * manifest) saves the business: its name, its logo and a start URL that opens its booking page.
 */
@Injectable({ providedIn: 'root' })
export class HomeScreen {
  private readonly document = inject(DOCUMENT);
  private readonly title = inject(Title);

  show(business: { name: string; slug: string; iconUrl?: string | null }): void {
    this.apply(
      business.name,
      assetUrl(`/api/tenancy/tenants/${encodeURIComponent(business.slug)}/manifest`)!,
      assetUrl(business.iconUrl) ?? PLATFORM.icon,
    );
  }

  reset(): void {
    this.apply(PLATFORM.name, PLATFORM.manifest, PLATFORM.icon);
  }

  private apply(name: string, manifest: string, icon: string): void {
    this.title.setTitle(name);
    this.element('link', 'rel', 'manifest').setAttribute('href', manifest);
    this.element('link', 'rel', 'apple-touch-icon').setAttribute('href', icon);
    this.element('meta', 'name', 'apple-mobile-web-app-title').setAttribute('content', name);
  }

  private element(tag: 'link' | 'meta', key: string, value: string): Element {
    const head = this.document.head;
    let element = head.querySelector(`${tag}[${key}="${value}"]`);
    if (!element) {
      element = this.document.createElement(tag);
      element.setAttribute(key, value);
      head.appendChild(element);
    }
    return element;
  }
}
