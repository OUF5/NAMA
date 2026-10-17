import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { AuthService } from '../../core/auth';

// Placeholder from M0, built in M8. Sign out (AUTH-04) is here since M1.
@Component({
  selector: 'app-settings',
  imports: [MatButtonModule],
  template: `
    <h1>Settings</h1>
    <button mat-stroked-button type="button" (click)="signOut()">Sign out</button>
  `,
})
export class Settings {
  private readonly auth = inject(AuthService);

  protected signOut(): void {
    void this.auth.signOut();
  }
}
