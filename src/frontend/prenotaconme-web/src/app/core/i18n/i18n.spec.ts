import { DEFAULT_LANG, initialLang, saveLang } from './i18n';

describe('initialLang', () => {
  afterEach(() => localStorage.clear());

  it('uses the saved language when valid', () => {
    saveLang('es');
    expect(initialLang()).toBe('es');
  });

  it('ignores unsupported saved values', () => {
    localStorage.setItem('lang', 'fr');
    expect(initialLang()).toBe(DEFAULT_LANG);
  });

  it('defaults to Italian regardless of the browser language', () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('es-ES');
    expect(initialLang()).toBe('it');
  });
});
