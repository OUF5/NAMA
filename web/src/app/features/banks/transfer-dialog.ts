import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { formatMoney, minorToInput, parseAmount, toMinor } from '../../shared/money';
import { formatDate, fromDateString, toDateString } from '../../shared/month-label';
import { BankAccount } from '../add-entry/entry-api';
import { BanksApi, Transfer } from './banks-api';
import { TransferFields, applyTransferServerErrors, createTransferForm, showAllTransferErrors } from './transfer-fields';

export interface TransferDialogData {
  transfer: Transfer;
  accounts: BankAccount[];
  currency: string;
  // The first and last day of the stored FYs (FY-03), YYYY-MM-DD.
  firstDate: string | null;
  lastDate: string | null;
}

export type TransferDialogResult = 'saved' | 'deleted';

// BNK-04: edit a transfer with the Add form's fields, or delete it after a confirmation that replaces
// the form inside this dialog, so dialogs are never stacked (UX-04).
@Component({
  selector: 'app-transfer-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, TransferFields],
  templateUrl: './transfer-dialog.html',
  styleUrl: '../entries/entry-dialog.scss',
})
export class TransferDialog {
  private readonly api = inject(BanksApi);
  private readonly dialogRef = inject<MatDialogRef<TransferDialog, TransferDialogResult>>(MatDialogRef);
  protected readonly data = inject<TransferDialogData>(MAT_DIALOG_DATA);

  protected readonly confirming = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected readonly form = createTransferForm();
  protected readonly keep = [this.data.transfer.fromBankAccountId, this.data.transfer.toBankAccountId];

  // "SAR 500 · SNB to Savings" and "18 Oct 2026".
  protected readonly summary = computed(() => {
    const { transfer, accounts, currency } = this.data;
    const name = (id: string) => accounts.find((account) => account.id === id)?.name ?? '';
    return {
      amount: `${formatMoney(toMinor(transfer.amount), currency)} · ${name(transfer.fromBankAccountId)} to ${name(transfer.toBankAccountId)}`,
      details: formatDate(transfer.date) + (transfer.note ? ` · ${transfer.note}` : ''),
    };
  });

  constructor() {
    const transfer = this.data.transfer;
    this.form.setValue({
      fromBankAccountId: transfer.fromBankAccountId,
      toBankAccountId: transfer.toBankAccountId,
      amount: minorToInput(toMinor(transfer.amount)),
      date: fromDateString(transfer.date),
      note: transfer.note,
    });
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      showAllTransferErrors(this.form);
      return;
    }
    const value = this.form.getRawValue();
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.updateTransfer(this.data.transfer.id, {
        fromBankAccountId: value.fromBankAccountId,
        toBankAccountId: value.toBankAccountId,
        date: toDateString(value.date!),
        amount: parseAmount(value.amount)! / 100,
        note: value.note.trim(),
      });
      this.dialogRef.close('saved');
    } catch (error: unknown) {
      if (!applyTransferServerErrors(this.form, error)) {
        this.error.set(this.gone(error) ? 'This transfer no longer exists.' : 'The transfer could not be saved. Please try again.');
      }
    } finally {
      this.busy.set(false);
    }
  }

  // After the confirmation step. A transfer that is already gone counts as deleted.
  protected async remove(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.deleteTransfer(this.data.transfer.id);
      this.dialogRef.close('deleted');
    } catch (error: unknown) {
      if (this.gone(error)) {
        this.dialogRef.close('deleted');
      } else {
        this.error.set('The transfer could not be deleted. Please try again.');
      }
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
