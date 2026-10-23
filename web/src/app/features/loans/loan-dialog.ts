import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { formatMoney, minorToInput, toMinor } from '../../shared/money';
import { formatDate, fromDateString } from '../../shared/month-label';
import { LoanFields, applyLoanServerErrors, createLoanForm, loanFromForm, showAllLoanErrors } from './loan-fields';
import { Loan, LoanAccount, LoansApi, LoansResponse } from './loans-api';

export interface LoanDialogData {
  loan: Loan;
  accounts: LoanAccount[];
  currency: string;
}

// LON-03: edit a loan with the New loan fields, or delete it after a confirmation that replaces the
// form inside this dialog (UX-04). Closes with the new GET /api/loans; the settled date stays.
@Component({
  selector: 'app-loan-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, LoanFields],
  templateUrl: './loan-dialog.html',
  styleUrl: '../entries/entry-dialog.scss',
})
export class LoanDialog {
  private readonly api = inject(LoansApi);
  private readonly dialogRef = inject<MatDialogRef<LoanDialog, LoansResponse>>(MatDialogRef);
  protected readonly data = inject<LoanDialogData>(MAT_DIALOG_DATA);

  protected readonly confirming = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly form = createLoanForm();

  // "Lent · Ahmed · SAR 1,000" and "10 Oct 2026".
  protected readonly summary = {
    title: `${this.data.loan.type === 'lent' ? 'Lent' : 'Borrowed'} · ${this.data.loan.name} · ${formatMoney(toMinor(this.data.loan.amount), this.data.currency)}`,
    details: formatDate(this.data.loan.date) + (this.data.loan.note ? ` · ${this.data.loan.note}` : ''),
  };

  constructor() {
    const loan = this.data.loan;
    this.form.setValue({
      type: loan.type,
      name: loan.name,
      amount: minorToInput(toMinor(loan.amount)),
      date: fromDateString(loan.date),
      bankAccountId: loan.bankAccountId,
      note: loan.note,
    });
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      showAllLoanErrors(this.form);
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      this.dialogRef.close(await this.api.update(this.data.loan.id, loanFromForm(this.form)));
    } catch (error: unknown) {
      if (!applyLoanServerErrors(this.form, error)) {
        this.error.set(this.gone(error) ? 'This loan no longer exists.' : 'The loan could not be saved. Please try again.');
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      this.dialogRef.close(await this.api.remove(this.data.loan.id));
    } catch (error: unknown) {
      this.error.set(this.gone(error) ? 'This loan no longer exists.' : 'The loan could not be deleted. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }

  protected confirm(on: boolean): void {
    this.error.set('');
    this.confirming.set(on);
  }

  private gone(error: unknown): boolean {
    return error instanceof HttpErrorResponse && error.status === 404;
  }
}
