import { firstErrorCode } from './api-errors';

describe('firstErrorCode', () => {
  it('returns the first validation error code', () => {
    expect(firstErrorCode({ errors: { workingHours: ['workingHours.outsideOpeningHours'] } })).toBe('workingHours.outsideOpeningHours');
  });

  it('uses a coded problem title (409 conflicts)', () => {
    expect(firstErrorCode({ title: 'email.taken' })).toBe('email.taken');
  });

  it('ignores human-readable titles', () => {
    expect(firstErrorCode({ title: 'Not Found' })).toBe('generic');
    expect(firstErrorCode(undefined)).toBe('generic');
  });
});
