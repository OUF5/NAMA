import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { NAME_MAX_LENGTH, nameValidator, notInListValidator } from '../../shared/names';

export interface NameDialogData<T> {
  title: string;
  label: string;
  name: string;
  // The other names in the same group (PRD 4.7: unique ignoring case). The API checks again.
  taken: string[];
  duplicateMessage: string;
  save: (name: string) => Promise<T>;
}

// Add or rename a bank account, category or sub-category (STG-04, STG-05). Closes with what save returns.
@Component({
  selector: 'app-name-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  providers: [OUTLINE_FORM_FIELDS],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content>
        <mat-form-field>
          <mat-label>{{ data.label }}</mat-label>
          <input matInput [formControl]="name" [maxlength]="nameMax" autocomplete="off" />
          @if (name.hasError('duplicate')) {
            <mat-error>{{ data.duplicateMessage }}</mat-error>
          } @else {
            <mat-error>Enter 1 to {{ nameMax }} characters.</mat-error>
          }
        </mat-form-field>
        @if (error()) {
          <p class="error" role="alert">{{ error() }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions>
        <button mat-stroked-button type="button" mat-dialog-close>Cancel</button>
        <button mat-flat-button type="submit" [disabled]="busy()">Save</button>
      </mat-dialog-actions>
    </form>
  `,
  styleUrl: './dialog.scss',
})
export class NameDialog<T> {
  private readonly dialogRef = inject<MatDialogRef<NameDialog<T>, T>>(MatDialogRef);
  protected readonly data = inject<NameDialogData<T>>(MAT_DIALOG_DATA);

  protected readonly nameMax = NAME_MAX_LENGTH;
  protected readonly name = new FormControl(this.data.name, {
    nonNullable: true,
    validators: [Validators.required, nameValidator(), notInListValidator(() => this.data.taken)],
  });
  // [formGroup] makes (ngSubmit) work and stops the browser's own submit.
  protected readonly form = new FormGroup({ name: this.name });

  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected async save(): Promise<void> {
    if (this.name.invalid) {
      this.name.markAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      this.dialogRef.close(await this.data.save(this.name.value.trim()));
    } catch (error: unknown) {
      if (error instanceof HttpErrorResponse && error.status === 400) {
        this.name.setErrors({ duplicate: true });
        this.name.markAsTouched();
      } else {
        this.error.set('It could not be saved. Please try again.');
      }
    } finally {
      this.busy.set(false);
    }
  }
}
