import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { incomeMonths } from '../../shared/income-months';
import { formatMoney, toMinor } from '../../shared/money';
import { IncomeItemContext, IncomeItemFields, createIncomeItemForm, incomeItemChange } from './income-item-fields';
import { Income, IncomeItem, SettingsApi } from './settings-api';

export interface IncomeItemDialogData {
  // null adds a new item.
  item: IncomeItem | null;
  // The pickers and the preview (owner note 17); its item is the same item.
  context: IncomeItemContext;
  defaultKey: string | null;
  // R-INC-05: a new item goes into the last used account.
  defaultAccountId: string | null;
  currency: string;
}

// R-INC-03: an additional income item, Once (one month) or Monthly (from a month, optionally until a
// month), with what it counts in (R-INC-04). The fields are Add's Income fields too (INC-01). Edit and
// delete fix mistakes; the delete confirmation replaces the form, never a second dialog (UX-04).
// Closes with the new income.
@Component({
  selector: 'app-income-item-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, IncomeItemFields],
  templateUrl: './income-item-dialog.html',
  styleUrl: './dialog.scss',
})
export class IncomeItemDialog {
  private readonly api = inject(SettingsApi);
  private readonly dialogRef = inject<MatDialogRef<IncomeItemDialog, Income>>(MatDialogRef);
  protected readonly data = inject<IncomeItemDialogData>(MAT_DIALOG_DATA);

  private readonly item = this.data.item;

  protected readonly form = createIncomeItemForm(this.item, this.data.defaultKey, this.data.defaultAccountId);

  protected readonly confirming = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  // "Bonus · SAR 500.25 · Sep 2026" for the delete confirmation.
  protected summary(): string {
    const item = this.item!;
    return `${item.label} · ${formatMoney(toMinor(item.amount), this.data.currency)} · ${incomeMonths(item, this.data.context.monthKeyOffset)}`;
  }

  protected async save(): Promise<void> {
    const change = incomeItemChange(this.form);
    if (!change) {
      return;
    }
    await this.run(
      () => (this.item ? this.api.updateIncome(this.item.id, change) : this.api.addIncome(change)),
      'The income could not be saved. Please try again.',
    );
  }

  // After the confirmation step.
  protected async remove(): Promise<void> {
    await this.run(() => this.api.deleteIncome(this.item!.id), 'The income could not be deleted. Please try again.');
  }

  protected confirm(on: boolean): void {
    this.error.set('');
    this.confirming.set(on);
  }

  private async run(call: () => Promise<Income>, failed: string): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      this.dialogRef.close(await call());
    } catch (error: unknown) {
      this.error.set(
        error instanceof HttpErrorResponse && error.status === 404
          ? 'This income item no longer exists. Please reload the page.'
          : error instanceof HttpErrorResponse && error.status === 400
            ? 'Some values were not accepted. Please check them.'
            : failed,
      );
    } finally {
      this.busy.set(false);
    }
  }
}
