import { HttpClient } from '@angular/common/http';
import { inject, Injectable, isDevMode } from '@angular/core';
import { provideTransloco, Translation, TranslocoLoader } from '@jsverse/transloco';

export const AVAILABLE_LANGS = ['it', 'es', 'en'] as const;
export type Lang = (typeof AVAILABLE_LANGS)[number];
export const DEFAULT_LANG: Lang = 'it';
const STORAGE_KEY = 'lang';

/**
 * Changes the translation URLs. Early builds served i18n/*.json with a 1-year immutable cache, so browsers
 * that loaded them keep the old copy for the bare URL. The server now sends no-cache for these files;
 * bump this only if that ever happens again.
 */
const I18N_URL_VERSION = '2';

@Injectable({ providedIn: 'root' })
export class HttpTranslocoLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(lang: string) {
    return this.http.get<Translation>(`i18n/${lang}.json?v=${I18N_URL_VERSION}`);
  }
}

/** Saved choice, otherwise Italian (the product targets Italian businesses, whatever the browser language). */
export function initialLang(): Lang {
  const stored = globalThis.localStorage?.getItem(STORAGE_KEY);
  return stored && (AVAILABLE_LANGS as readonly string[]).includes(stored) ? (stored as Lang) : DEFAULT_LANG;
}

export function saveLang(lang: Lang): void {
  globalThis.localStorage?.setItem(STORAGE_KEY, lang);
  document.documentElement.lang = lang;
}

export function provideI18n() {
  return provideTransloco({
    config: {
      availableLangs: [...AVAILABLE_LANGS],
      defaultLang: initialLang(),
      fallbackLang: DEFAULT_LANG,
      reRenderOnLangChange: true,
      prodMode: !isDevMode(),
    },
    loader: HttpTranslocoLoader,
  });
}
