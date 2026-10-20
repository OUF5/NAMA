import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { incomeMonths, untilNotBeforeFrom } from '../../shared/income-months';
import { amountValidator, formatMoney, minorToInput, parseAmount, toMinor } from '../../shared/money';
import { NAME_MAX_LENGTH, nameValidator } from '../../shared/names';
import { Income, IncomeItem, SettingsApi } from './settings-api';
import { MonthGroup } from './settings-lines';

export interface IncomeItemDialogData {
  // null adds a new item.
  item: IncomeItem | null;
  groups: MonthGroup[];
  defaultKey: string | null;
  currency: string;
}

// R-INC-03: an additional income item, Once (one month) or Monthly (from a month, optionally until a
// month). Edit and delete fix mistakes; the delete confirmation replaces the form, never a second
// dialog (UX-04). Closes with the new income.
@Component({
  selector: 'app-income-item-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  providers: [OUTLINE_FORM_FIELDS],
  templateUrl: './income-item-dialog.html',
  styleUrl: './dialog.scss',
})
export class IncomeItemDialog {
  private readonly api = inject(SettingsApi);
  private readonly dialogRef = inject<MatDialogRef<IncomeItemDialog, Income>>(MatDialogRef);
  protected readonly data = inject<IncomeItemDialogData>(MAT_DIALOG_DATA);

  protected readonly nameMax = NAME_MAX_LENGTH;
  private readonly item = this.data.item;

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      label: [this.item?.label ?? '', nameValidator()],
      amount: [this.item ? minorToInput(toMinor(this.item.amount)) : '', amountValidator()],
      type: [this.item?.type ?? ('monthly' as 'once' | 'monthly'), Validators.required],
      from: [this.item?.fromPeriodKey ?? this.data.defaultKey ?? '', Validators.required],
      to: [this.item?.type === 'monthly' ? (this.item.toPeriodKey ?? '') : ''],
    },
    { validators: untilNotBeforeFrom },
  );

  protected readonly type = toSignal(this.form.controls.type.valueChanges, { initialValue: this.form.controls.type.value });

  protected readonly confirming = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  // "Bonus · SAR 500.25 · Sep 2026" for the delete confirmation.
  protected summary(): string {
    const item = this.item!;
    return `${item.label} · ${formatMoney(toMinor(item.amount), this.data.currency)} · ${incomeMonths(item)}`;
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const change = {
      label: value.label.trim(),
      amount: parseAmount(value.amount)! / 100,
      type: value.type,
      fromPeriodKey: value.from,
      toPeriodKey: value.type === 'monthly' && value.to ? value.to : null,
    };
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
