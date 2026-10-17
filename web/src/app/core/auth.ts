import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { initializeApp } from 'firebase/app';
import {
  GoogleAuthProvider,
  User,
  getAuth,
  onAuthStateChanged,
  signInWithPopup,
  signOut,
} from 'firebase/auth';
import { environment } from '../../environments/environment';
import { MeService } from './me';

// AUTH-01: Google sign-in with the Firebase Auth popup (PRD 7.6). Only firebase/auth is used.
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly router = inject(Router);
  private readonly me = inject(MeService);
  private readonly auth = getAuth(initializeApp(environment.firebase));

  // The signed-in Google user, or null. Reliable once ready() has resolved.
  readonly user = signal<User | null>(null);

  constructor() {
    onAuthStateChanged(this.auth, (user) => this.user.set(user));
  }

  // Resolves once Firebase has restored (or ruled out) the previous session.
  async ready(): Promise<void> {
    await this.auth.authStateReady();
    this.user.set(this.auth.currentUser);
  }

  async signIn(): Promise<void> {
    const provider = new GoogleAuthProvider();
    // Always show the account chooser, so "Use another account" really offers one.
    provider.setCustomParameters({ prompt: 'select_account' });
    await signInWithPopup(this.auth, provider);
  }

  // AUTH-04
  async signOut(): Promise<void> {
    await signOut(this.auth);
    this.me.clear();
    await this.router.navigate(['/sign-in']);
  }

  // The current ID token, or null when signed out. The SDK refreshes it by itself.
  async idToken(): Promise<string | null> {
    await this.auth.authStateReady();
    return (await this.auth.currentUser?.getIdToken()) ?? null;
  }
}
