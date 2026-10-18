import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { formatDate } from '../../shared/month-label';

export interface InviteLinkDialogData {
  link: string;
  expiresOn: string;
}

// AUTH-06: the new link, shown once (only its hash is stored). Copy is the one filled button.
@Component({
  selector: 'app-invite-link-dialog',
  imports: [MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  providers: [OUTLINE_FORM_FIELDS],
  template: `
    <h2 mat-dialog-title>Invite link</h2>
    <mat-dialog-content>
      <p>Send this link to the person you want to invite. It works once and expires on {{ expiresOn }}.</p>
      <mat-form-field>
        <mat-label>Invite link</mat-label>
        <input #field matInput readonly [value]="data.link" (focus)="field.select()" />
      </mat-form-field>
      <p class="note">Copy it now. NAMA shows it only once.</p>
      <p class="status" role="status">{{ copied() }}</p>
    </mat-dialog-content>
    <mat-dialog-actions>
      <button mat-stroked-button type="button" mat-dialog-close>Done</button>
      <button mat-flat-button type="button" (click)="copy()">Copy link</button>
    </mat-dialog-actions>
  `,
  styleUrl: './dialog.scss',
})
export class InviteLinkDialog {
  protected readonly data = inject<InviteLinkDialogData>(MAT_DIALOG_DATA);
  protected readonly expiresOn = formatDate(this.data.expiresOn);
  protected readonly copied = signal('');

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.data.link);
      this.copied.set('Copied.');
    } catch {
      this.copied.set('It could not be copied. Select the link and copy it.');
    }
  }
}

// AUTH-07: removing a person is confirmed first (UX-04). Closes with true to remove. Cancel comes
// first, so it has the focus when the dialog opens.
@Component({
  selector: 'app-remove-person-dialog',
  imports: [MatButtonModule, MatDialogModule],
  template: `
    <h2 mat-dialog-title>Remove {{ email }}?</h2>
    <mat-dialog-content>
      <p>They can no longer use NAMA. Their data is kept, and it comes back if you invite the same Google account again.</p>
    </mat-dialog-content>
    <mat-dialog-actions>
      <button mat-stroked-button type="button" mat-dialog-close>Cancel</button>
      <button mat-flat-button type="button" [mat-dialog-close]="true">Remove</button>
    </mat-dialog-actions>
  `,
  styleUrl: './dialog.scss',
})
export class RemovePersonDialog {
  protected readonly email = inject<string>(MAT_DIALOG_DATA);
}
