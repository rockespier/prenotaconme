import { FirebaseError } from 'firebase/app';
import { signInError } from './auth.service';

const DESKTOP = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0 Safari/537.36';

describe('signInError', () => {
  it('ignores a popup the user closed', () => {
    expect(signInError(new FirebaseError('auth/popup-closed-by-user', 'closed'), DESKTOP)).toBeNull();
  });

  it('asks to sign in with Google once when the e-mail already uses Google', () => {
    expect(signInError(new FirebaseError('auth/account-exists-with-different-credential', 'exists'), DESKTOP))
      .toBe('login.useGoogleFirst');
  });

  it('falls back to the generic message', () => {
    expect(signInError(new Error('network'), DESKTOP)).toBe('login.error');
  });
});
