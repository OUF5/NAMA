import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule } from '@angular/material/dialog';

// AUTH-04 with a confirmation (owner's choice, M8). Closes with true to sign out. Cancel comes
// first, so it has the focus when the dialog opens.
@Component({
  selector: 'app-sign-out-dialog',
  imports: [MatButtonModule, MatDialogModule],
  template: `
    <h2 mat-dialog-title>Sign out?</h2>
    <mat-dialog-content>
      <p>You will need to sign in with Google again to use NAMA on this device.</p>
    </mat-dialog-content>
    <mat-dialog-actions>
      <button mat-stroked-button type="button" mat-dialog-close>Cancel</button>
      <button mat-flat-button type="button" [mat-dialog-close]="true">Sign out</button>
    </mat-dialog-actions>
  `,
  styles: `
    p {
      margin: 0;
    }

    mat-dialog-actions {
      gap: 8px;
    }

    @media (max-width: 767.98px) {
      mat-dialog-actions button {
        height: 44px;
      }
    }
  `,
})
export class SignOutDialog {}
