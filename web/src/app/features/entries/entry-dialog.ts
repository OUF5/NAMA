import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { formatMoney, minorToInput, parseAmount, toMinor } from '../../shared/money';
import { formatDate, fromDateString, toDateString } from '../../shared/month-label';
import { Category } from '../plan/plan-api';
import { BankAccount, Entry, EntryApi } from '../add-entry/entry-api';
import { EntryFields, KeptItems, applyServerErrors, createEntryForm } from '../add-entry/entry-fields';

export interface EntryDialogData {
  entry: Entry;
  categories: Category[];
  accounts: BankAccount[];
  currency: string;
  // The first and last day of the stored FYs (FY-03), YYYY-MM-DD.
  firstDate: string | null;
  lastDate: string | null;
}

export type EntryDialogResult = 'saved' | 'deleted';

// docs/DESIGN.md "Dialogs": full screen on phones, 640px wide on desktop. Used by Entries and Months.
export function openEntryDialog(dialog: MatDialog, phone: boolean, data: EntryDialogData): MatDialogRef<EntryDialog, EntryDialogResult> {
  const size = phone
    ? { width: '100vw', maxWidth: '100vw', height: '100dvh', panelClass: 'full-screen-dialog' }
    : { width: '640px', maxWidth: 'calc(100vw - 32px)' };
  return dialog.open<EntryDialog, EntryDialogData, EntryDialogResult>(EntryDialog, { ...size, data });
}

// TBL-02 edit with the Add form, and TBL-03 delete after a confirmation. The confirmation replaces
// the form inside this dialog, so dialogs are never stacked (UX-04).
@Component({
  selector: 'app-entry-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, EntryFields],
  templateUrl: './entry-dialog.html',
  styleUrl: './entry-dialog.scss',
})
export class EntryDialog {
  private readonly entryApi = inject(EntryApi);
  private readonly dialogRef = inject<MatDialogRef<EntryDialog, EntryDialogResult>>(MatDialogRef);
  protected readonly data = inject<EntryDialogData>(MAT_DIALOG_DATA);

  protected readonly confirming = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  // ENT-04: every field can be changed; the API moves the entry to the month of its date.
  protected readonly form = createEntryForm();
  protected readonly keep: KeptItems = {
    subCategoryId: this.data.entry.subCategoryId,
    bankAccountId: this.data.entry.bankAccountId,
  };

  // "SAR 125.50 · Groceries" and "17 Oct 2026 · Al Rajhi".
  protected readonly summary = computed(() => {
    const { entry, categories, accounts, currency } = this.data;
    const sub = categories.flatMap((category) => category.subCategories).find((s) => s.id === entry.subCategoryId);
    const account = accounts.find((a) => a.id === entry.bankAccountId);
    return {
      amount: `${formatMoney(toMinor(entry.amount), currency)} · ${sub?.name ?? ''}`,
      details: `${formatDate(entry.date)} · ${account?.name ?? ''}`,
    };
  });

  constructor() {
    const entry = this.data.entry;
    this.form.setValue({
      categoryId: entry.categoryId,
      subCategoryId: entry.subCategoryId,
      amount: minorToInput(toMinor(entry.amount)),
      bankAccountId: entry.bankAccountId,
      date: fromDateString(entry.date),
      note: entry.note,
    });
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.busy.set(true);
    this.error.set('');
    try {
      await this.entryApi.updateEntry(this.data.entry.id, {
        subCategoryId: value.subCategoryId,
        bankAccountId: value.bankAccountId,
        date: toDateString(value.date!),
        amount: parseAmount(value.amount)! / 100,
        note: value.note.trim(),
      });
      this.dialogRef.close('saved');
    } catch (error: unknown) {
      if (!applyServerErrors(this.form, error)) {
        this.error.set(this.gone(error) ? 'This entry no longer exists.' : 'The entry could not be saved. Please try again.');
      }
    } finally {
      this.busy.set(false);
    }
  }

  // TBL-03, after the confirmation step. An entry that is already gone counts as deleted.
  protected async remove(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.entryApi.deleteEntry(this.data.entry.id);
      this.dialogRef.close('deleted');
    } catch (error: unknown) {
      if (this.gone(error)) {
        this.dialogRef.close('deleted');
      } else {
        this.error.set('The entry could not be deleted. Please try again.');
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
