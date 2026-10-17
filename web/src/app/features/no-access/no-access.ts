import { Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { AuthService } from '../../core/auth';

// AUTH-02: shown when the API answers 403, because the email is not in ALLOWED_EMAILS.
@Component({
  selector: 'app-no-access',
  imports: [MatButtonModule],
  template: `
    <section class="page">
      <h1>Access not allowed</h1>
      <p>
        @if (email(); as email) {
          {{ email }} is not allowed to use NAMA.
        } @else {
          This Google account is not allowed to use NAMA.
        }
      </p>
      <button mat-flat-button type="button" (click)="useAnotherAccount()">Use another account</button>
    </section>
  `,
  styles: `
    .page {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 16px;
      padding-top: 15vh;
      text-align: center;
    }
  `,
})
export class NoAccess {
  private readonly auth = inject(AuthService);

  protected readonly email = computed(() => this.auth.user()?.email ?? null);

  protected useAnotherAccount(): void {
    void this.auth.signOut();
  }
}
