import { computed, Injectable, signal } from '@angular/core';
import { FirebaseError, initializeApp } from 'firebase/app';
import {
  AuthCredential,
  AuthProvider,
  getAuth,
  GoogleAuthProvider,
  linkWithCredential,
  OAuthProvider,
  onAuthStateChanged,
  signInWithPopup,
  signOut,
  deleteUser,
  reauthenticateWithPopup,
  User,
} from 'firebase/auth';
import { firebaseConfig } from '../../../environments/firebase';
import { signInErrorKey } from './in-app-browser';

/** Sign-in providers enabled in the Firebase project. */
export type SignInProvider = 'google' | 'microsoft';

/** Firebase "one account per e-mail": the e-mail already signed in with another provider. */
const ACCOUNT_EXISTS = 'auth/account-exists-with-different-credential';
/** The user closed the popup or opened a second one: not an error worth showing. */
const CANCELLED = new Set(['auth/popup-closed-by-user', 'auth/cancelled-popup-request', 'auth/user-cancelled']);

/**
 * Translation key for a failed sign-in, or null when the user just closed the popup. An e-mail that already
 * uses Google asks to sign in with Google once: the Microsoft account is linked to it then.
 */
export function signInError(error: unknown, userAgent?: string): string | null {
  const code = error instanceof FirebaseError ? error.code : '';
  if (CANCELLED.has(code)) return null;
  if (code === ACCOUNT_EXISTS) return 'login.useGoogleFirst';
  return signInErrorKey(userAgent);
}

/** Firebase Authentication (Google and Microsoft SSO). Exposes the signed-in user as signals. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly auth = getAuth(initializeApp(firebaseConfig));
  private readonly readyPromise: Promise<void>;
  /** Microsoft credential refused because its e-mail already has a Google account; linked after the Google sign-in. */
  private pendingLink: AuthCredential | null = null;

  readonly user = signal<User | null>(null);
  /** False until Firebase has restored (or ruled out) a persisted session. */
  readonly ready = signal(false);
  readonly isSignedIn = computed(() => this.user() !== null);

  constructor() {
    this.readyPromise = new Promise((resolve) => {
      onAuthStateChanged(this.auth, (user) => {
        this.user.set(user);
        this.ready.set(true);
        resolve();
      });
    });
  }

  /** Resolves once the initial auth state is known (use in guards). */
  whenReady(): Promise<void> {
    return this.readyPromise;
  }

  /** Opens the provider's popup. Errors are Firebase errors: map them with {@link signInError}. */
  async signIn(provider: SignInProvider): Promise<void> {
    try {
      const result = await signInWithPopup(this.auth, this.provider(provider));
      if (this.pendingLink && provider === 'google') {
        const pending = this.pendingLink;
        this.pendingLink = null;
        // Best effort: the user is signed in either way; next time Microsoft works directly.
        await linkWithCredential(result.user, pending).catch(() => undefined);
      }
    } catch (error) {
      if (error instanceof FirebaseError && error.code === ACCOUNT_EXISTS) {
        this.pendingLink = OAuthProvider.credentialFromError(error);
      }
      throw error;
    }
  }

  /**
   * F5-04: removes the Firebase account (after the API deleted the local data). Firebase asks for a recent sign-in:
   * when it does, the same provider's popup opens once more.
   */
  async deleteAccount(): Promise<void> {
    const user = this.auth.currentUser;
    if (!user) return;
    try {
      await deleteUser(user);
    } catch (error) {
      if (!(error instanceof FirebaseError) || error.code !== 'auth/requires-recent-login') throw error;
      const provider = user.providerData.some((p) => p.providerId === 'microsoft.com') ? 'microsoft' : 'google';
      await reauthenticateWithPopup(user, this.provider(provider));
      await deleteUser(user);
    }
  }

  signOut(): Promise<void> {
    this.pendingLink = null;
    return signOut(this.auth);
  }

  /** Fresh ID token for the API (Firebase refreshes it transparently when close to expiry). */
  async idToken(): Promise<string | null> {
    return (await this.auth.currentUser?.getIdToken()) ?? null;
  }

  private provider(provider: SignInProvider): AuthProvider {
    if (provider === 'microsoft') {
      // "common" tenant: work/school and personal Microsoft accounts.
      const microsoft = new OAuthProvider('microsoft.com');
      microsoft.setCustomParameters({ prompt: 'select_account' });
      return microsoft;
    }

    const google = new GoogleAuthProvider();
    google.setCustomParameters({ prompt: 'select_account' });
    return google;
  }
}
