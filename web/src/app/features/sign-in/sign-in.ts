import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { AuthService } from '../../core/auth';

// AUTH-01: Google sign-in only. Afterwards the guards pick Setup or Dashboard (AUTH-05).
@Component({
  selector: 'app-sign-in',
  imports: [MatButtonModule],
  template: `
    <section class="page">
      <h1>Sign in to NAMA</h1>
      <button mat-flat-button type="button" [disabled]="busy()" (click)="signIn()">
        Sign in with Google
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
