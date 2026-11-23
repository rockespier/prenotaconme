import { DEFAULT_LANG, initialLang, saveLang } from './i18n';

describe('initialLang', () => {
  afterEach(() => localStorage.clear());

  it('uses the saved language when valid', () => {
    saveLang('es');
    expect(initialLang()).toBe('es');
  });

  it('ignores unsupported saved values', () => {
    localStorage.setItem('lang', 'fr');
    const lang = initialLang();
    expect(['it', 'es', 'en']).toContain(lang);
  });

  it('falls back to the default language', () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('de-DE');
    expect(initialLang()).toBe(DEFAULT_LANG);
  });
});
