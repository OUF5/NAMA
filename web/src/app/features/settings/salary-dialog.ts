import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { amountValidator, parseAmount } from '../../shared/money';
import { Income, SettingsApi } from './settings-api';
import { MonthGroup } from './settings-lines';

export interface SalaryDialogData {
  groups: MonthGroup[];
  // The current month, or null when no FY contains today (then a month is picked).
  defaultKey: string | null;
  currency: string;
}

// R-INC-02, STG-03: a new salary from a month on. Earlier months keep the old salary. 0 is allowed
// (as SET-01). Closes with the new income.
@Component({
  selector: 'app-salary-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  providers: [OUTLINE_FORM_FIELDS],
  templateUrl: './salary-dialog.html',
  styleUrl: './dialog.scss',
})
export class SalaryDialog {
  private readonly api = inject(SettingsApi);
  private readonly dialogRef = inject<MatDialogRef<SalaryDialog, Income>>(MatDialogRef);
  protected readonly data = inject<SalaryDialogData>(MAT_DIALOG_DATA);

  protected readonly form = inject(NonNullableFormBuilder).group({
    amount: ['', amountValidator(true)],
    from: [this.data.defaultKey ?? '', Validators.required],
  });

  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.busy.set(true);
    this.error.set('');
    try {
      this.dialogRef.close(await this.api.changeSalary(parseAmount(value.amount, true)! / 100, value.from));
    } catch (error: unknown) {
      this.error.set(
        error instanceof HttpErrorResponse && error.status === 400
          ? 'The salary was not accepted. Please check the amount and the month.'
          : 'The salary could not be saved. Please try again.',
      );
    } finally {
      this.busy.set(false);
    }
  }
}
