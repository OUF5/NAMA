import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth';

// AUTH-01: Google sign-in only. Afterwards the guards pick Setup or Dashboard (AUTH-05).
// The button follows Google's sign-in branding (light variant, official "G" logo).
@Component({
  selector: 'app-sign-in',
  template: `
    <section class="page">
      <h1>Sign in to NAMA</h1>
      <button class="google" type="button" [disabled]="busy()" (click)="signIn()">
        <img src="google-g.svg" alt="" width="18" height="18" />
        <span>Sign in with Google</span>
      </button>
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
    </section>
  `,
  styles: `
    :host {
      display: flex;
      justify-content: center;
      padding-top: 12vh;
    }

    .page {
      box-sizing: border-box;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 16px;
      width: 100%;
      max-width: 400px;
      padding: 32px 24px;
      text-align: center;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 12px;
    }

    h1 {
      margin: 0;
    }

    .google {
      display: inline-flex;
      align-items: center;
      gap: 12px;
      height: 40px;
      padding: 0 12px;
      border: 1px solid var(--google-border);
      border-radius: 4px;
      background: var(--google-bg);
      color: var(--google-text);
      font: 500 14px var(--font);
      cursor: pointer;

      &:hover:not(:disabled) {
        background: var(--google-bg-hover);
      }

      &:disabled {
        opacity: 0.6;
        cursor: default;
      }
    }

    .error {
      margin: 0;
      color: var(--danger);
    }
  `,
})
export class SignIn {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected async signIn(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.auth.signIn();
      await this.router.navigateByUrl('/');
    } catch {
      // For example the popup was closed or blocked.
      this.error.set('Sign-in did not finish. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
