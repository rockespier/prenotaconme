import { HttpClient } from '@angular/common/http';
import { inject, Injectable, isDevMode } from '@angular/core';
import { provideTransloco, Translation, TranslocoLoader } from '@jsverse/transloco';

export const AVAILABLE_LANGS = ['it', 'es', 'en'] as const;
export type Lang = (typeof AVAILABLE_LANGS)[number];
export const DEFAULT_LANG: Lang = 'it';
const STORAGE_KEY = 'lang';

@Injectable({ providedIn: 'root' })
export class HttpTranslocoLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(lang: string) {
    return this.http.get<Translation>(`i18n/${lang}.json`);
  }
}

/** Saved choice → browser language → default (it). */
export function initialLang(): Lang {
  const isLang = (v: string | null | undefined): v is Lang =>
    !!v && (AVAILABLE_LANGS as readonly string[]).includes(v);

  const stored = globalThis.localStorage?.getItem(STORAGE_KEY);
  if (isLang(stored)) return stored;

  const browser = globalThis.navigator?.language?.slice(0, 2);
  return isLang(browser) ? browser : DEFAULT_LANG;
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
