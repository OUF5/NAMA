import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { ErrorStateMatcher, provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { formatMoney, toMinor } from '../../shared/money';
import { formatDate, fromDateString, toDateString } from '../../shared/month-label';
import { TouchedErrorStateMatcher } from '../add-entry/entry-fields';
import { Loan, LoansApi, LoansResponse } from './loans-api';

export interface SettleDialogData {
  loan: Loan;
  today: string;
  currency: string;
}

// R-LON-03: a Lent loan is returned, a Borrowed one paid, in full, on a date on or after the loan's
// (default today). Closes with the new GET /api/loans.
@Component({
  selector: 'app-settle-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDatepickerModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  providers: [OUTLINE_FORM_FIELDS, provideNativeDateAdapter(), { provide: ErrorStateMatcher, useClass: TouchedErrorStateMatcher }],
  templateUrl: './settle-dialog.html',
  styleUrl: '../settings/dialog.scss',
})
export class SettleDialog {
  private readonly api = inject(LoansApi);
  private readonly dialogRef = inject<MatDialogRef<SettleDialog, LoansResponse>>(MatDialogRef);
  protected readonly data = inject<SettleDialogData>(MAT_DIALOG_DATA);

  private readonly lent = this.data.loan.type === 'lent';
  protected readonly title = this.lent ? 'Mark returned' : 'Mark paid';
  protected readonly question = this.lent
    ? `${this.data.loan.name} returned ${this.money()} on`
    : `You paid ${this.data.loan.name} ${this.money()} on`;
  protected readonly minDate = fromDateString(this.data.loan.date);
  protected readonly loanDate = formatDate(this.data.loan.date);

  protected readonly date = new FormControl<Date | null>(
    fromDateString(this.data.today < this.data.loan.date ? this.data.loan.date : this.data.today),
    Validators.required,
  );
  protected readonly form = new FormGroup({ date: this.date });
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected async save(): Promise<void> {
    if (this.date.invalid) {
      this.date.markAsTouched();
      this.date.markAsDirty();
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      this.dialogRef.close(await this.api.settle(this.data.loan.id, toDateString(this.date.value!)));
    } catch {
      this.error.set('It could not be saved. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private money(): string {
    return formatMoney(toMinor(this.data.loan.amount), this.data.currency);
  }
}
