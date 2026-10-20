import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { balanceValidator, minorToInput, parseBalance, toMinor } from '../../shared/money';
import { formatDate, fromDateString, toDateString } from '../../shared/month-label';
import { NAME_MAX_LENGTH, nameValidator, notInListValidator } from '../../shared/names';
import { Account, BanksApi, BanksResponse } from './banks-api';

export interface AccountDialogData {
  // null adds a new account.
  account: Account | null;
  // The other accounts' names, archived ones included (PRD 4.7).
  taken: string[];
  currency: string;
  today: string;
  // The first and last day of the stored FYs (R-BAL-01: the date is inside one).
  firstDate: string;
  lastDate: string;
}

// "-1200.50" for the input; a minus sign is allowed (R-BAL-01).
function balanceToInput(amount: number): string {
  const minor = toMinor(amount);
  return minor < 0 ? `-${minorToInput(-minor)}` : minorToInput(minor);
}

// BNK-02, R-BAL-01: an account's name and its opening balance, the balance it had at the start of a
// day. Closes with the new GET /api/banks.
@Component({
  selector: 'app-account-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDatepickerModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  providers: [OUTLINE_FORM_FIELDS, provideNativeDateAdapter()],
  templateUrl: './account-dialog.html',
  styleUrl: '../settings/dialog.scss',
})
export class AccountDialog {
  private readonly api = inject(BanksApi);
  private readonly dialogRef = inject<MatDialogRef<AccountDialog, BanksResponse>>(MatDialogRef);
  protected readonly data = inject<AccountDialogData>(MAT_DIALOG_DATA);

  protected readonly nameMax = NAME_MAX_LENGTH;
  protected readonly minDate = fromDateString(this.data.firstDate);
  protected readonly maxDate = fromDateString(this.data.lastDate);
  protected readonly range = `${formatDate(this.data.firstDate)} to ${formatDate(this.data.lastDate)}`;
  private readonly account = this.data.account;

  // A new account, or one without a balance yet, starts at today (inside the FYs) and an empty balance.
  protected readonly form = new FormGroup({
    name: new FormControl(this.account?.name ?? '', {
      nonNullable: true,
      validators: [nameValidator(), notInListValidator(() => this.data.taken)],
    }),
    balance: new FormControl(this.account?.openingBalance != null ? balanceToInput(this.account.openingBalance) : '', {
      nonNullable: true,
      validators: balanceValidator(),
    }),
    date: new FormControl<Date | null>(
      fromDateString(this.account?.openingDate ?? this.clamp(this.data.today)),
      Validators.required,
    ),
  });

  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const change = {
      name: value.name.trim(),
      openingBalance: parseBalance(value.balance)! / 100,
      openingDate: toDateString(value.date!),
    };
    this.busy.set(true);
    this.error.set('');
    try {
      this.dialogRef.close(
        await (this.account
          ? this.api.updateAccount(this.account.id, { ...change, archived: this.account.archived })
          : this.api.addAccount(change)),
      );
    } catch (error: unknown) {
      const errors = error instanceof HttpErrorResponse && error.status === 400 ? ((error.error?.errors ?? {}) as Record<string, string[]>) : null;
      if (errors?.['Name']) {
        this.form.controls.name.setErrors({ duplicate: true });
      } else if (errors?.['OpeningDate']) {
        this.form.controls.date.setErrors({ outside: true });
      } else {
        this.error.set(
          error instanceof HttpErrorResponse && error.status === 404
            ? 'This account no longer exists. Please reload the page.'
            : 'The account could not be saved. Please try again.',
        );
      }
    } finally {
      this.busy.set(false);
    }
  }

  // Today when it is inside the stored FYs, else the nearest end.
  private clamp(date: string): string {
    return date < this.data.firstDate ? this.data.firstDate : date > this.data.lastDate ? this.data.lastDate : date;
  }
}
