import { SLUG_PATTERN, suggestSlug } from './slug';

describe('suggestSlug', () => {
  it.each([
    ['Barbería Mario & Co.', 'barberia-mario-co'],
    ['  Salón  Belleza  ', 'salon-belleza'],
    ['Studio 54', 'studio-54'],
    ['---', ''],
  ])('%s -> %s', (name, expected) => {
    expect(suggestSlug(name)).toBe(expected);
  });

  it('never exceeds 50 chars nor ends with a hyphen', () => {
    const slug = suggestSlug('a'.repeat(49) + ' b c');
    expect(slug.length).toBeLessThanOrEqual(50);
    expect(slug.endsWith('-')).toBe(false);
    expect(SLUG_PATTERN.test(slug)).toBe(true);
  });
});
