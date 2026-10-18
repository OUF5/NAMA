import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth';

// AUTH-01: Google sign-in only. Afterwards the guards pick Setup or Dashboard (AUTH-05).
// The button follows Google's sign-in branding (light variant, official "G" logo).
// Owner's request (M9): the page covers the whole screen, top bar included, with the NAMA mark, the
// background artwork (web/public/sign-in-bg.svg) and "Built by Ouf.".
@Component({
  selector: 'app-sign-in',
  template: `
    <img class="art" src="sign-in-bg.svg" alt="" aria-hidden="true" />
    <section class="card" aria-labelledby="sign-in-title">
      <span class="logo" aria-hidden="true"></span>
      <h1 id="sign-in-title">NAMA</h1>
      <p class="caption">Plan and track your spending, salary to salary.</p>
      <button class="google" type="button" [disabled]="busy()" (click)="signIn()">
        <img src="google-g.svg" alt="" width="18" height="18" />
        <span>Sign in with Google</span>
      </button>
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
    </section>
    <p class="credit">Built by Ouf.</p>
  `,
  styles: `
    :host {
      position: fixed;
      inset: 0;
      z-index: 10;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 24px;
      overflow: auto;
      padding: 32px 16px calc(24px + env(safe-area-inset-bottom));
      box-sizing: border-box;
      background: var(--bg);
    }

    .art {
      position: absolute;
      inset: 0;
      width: 100%;
      height: 100%;
      object-fit: cover;
      pointer-events: none;
    }

    .card {
      position: relative;
      box-sizing: border-box;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 16px;
      width: 100%;
      max-width: 360px;
      padding: 32px 24px;
      text-align: center;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 12px;
    }

    // The NAMA mark, as in the top bar and the tab icon.
    .logo {
      box-sizing: border-box;
      width: 40px;
      height: 40px;
      background: var(--bg);
      border: 3px solid var(--primary);
    }

    h1 {
      margin: 0;
      letter-spacing: 0.08em;
    }

    .caption {
      margin: -8px 0 8px;
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

    .credit {
      position: relative;
      margin: 0;
      font-size: 13px;
      color: var(--text-2);
    }

    @media (max-width: 767.98px) {
      .google {
        height: 44px;
      }
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
