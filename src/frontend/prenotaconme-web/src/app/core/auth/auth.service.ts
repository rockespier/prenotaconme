import { computed, Injectable, signal } from '@angular/core';
import { initializeApp } from 'firebase/app';
import { getAuth, GoogleAuthProvider, onAuthStateChanged, signInWithPopup, signOut, User } from 'firebase/auth';
import { firebaseConfig } from '../../../environments/firebase';

/** Firebase Authentication (Google SSO). Exposes the signed-in user as signals. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly auth = getAuth(initializeApp(firebaseConfig));
  private readonly readyPromise: Promise<void>;

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

  async signInWithGoogle(): Promise<void> {
    const provider = new GoogleAuthProvider();
    provider.setCustomParameters({ prompt: 'select_account' });
    await signInWithPopup(this.auth, provider);
  }

  signOut(): Promise<void> {
    return signOut(this.auth);
  }

  /** Fresh ID token for the API (Firebase refreshes it transparently when close to expiry). */
  async idToken(): Promise<string | null> {
    return (await this.auth.currentUser?.getIdToken()) ?? null;
  }
}
