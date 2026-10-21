import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormArray, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, ValidatorFn } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { amountValidator, formatMoney, minorToInput, parseAmount, toMinor } from '../../shared/money';
import { formatDay } from '../../shared/month-label';
import { PlanApi, PlansResponse, SubCategory } from './plan-api';

export interface PlanDialogData {
  sub: SubCategory;
  categoryName: string;
  plans: PlansResponse;
  currency: string;
}

// Owner's choice (M4): an empty month or 0 means no plan for that month (R-PLN-01).
function optionalAmount(): ValidatorFn {
  const amount = amountValidator(true);
  return (control: AbstractControl): ValidationErrors | null =>
    ((control.value ?? '') as string).trim() === '' ? null : amount(control);
}

// Minor units of a month input: empty is 0, null when the text is not a valid amount.
function minorOf(text: string): number | null {
  return text.trim() === '' ? 0 : parseAmount(text, true);
}

// PLN-01, PLN-02, PLN-04: the 12 month amounts of one sub-category, labeled with the stored months and
// their dates, with "Same amount for all months" and "Copy from previous month". Owner's choice (M8):
// it is a dialog, and each month shows "Planned savings after" while you type (carried, so a change shows
// in every later month), so the effect is seen before saving. That preview is the only sum made in the browser, in exact minor units; the saved numbers
// come from the API. Closes with the new plans.
@Component({
  selector: 'app-plan-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule],
  providers: [OUTLINE_FORM_FIELDS],
  templateUrl: './plan-dialog.html',
  styleUrl: './plan-dialog.scss',
})
export class PlanDialog {
  private readonly api = inject(PlanApi);
  private readonly dialogRef = inject<MatDialogRef<PlanDialog, PlansResponse>>(MatDialogRef);
  protected readonly data = inject<PlanDialogData>(MAT_DIALOG_DATA);

  protected readonly formatDay = formatDay;
  protected readonly periods = this.data.plans.fiscalYear.periods;
  private readonly saved = this.data.plans.plans.find((plan) => plan.subCategoryId === this.data.sub.id)?.amounts ?? {};

  protected readonly months = new FormArray(
    this.periods.map((period) => {
      const amount = this.saved[period.key];
      return new FormControl(amount ? minorToInput(toMinor(amount)) : '', { nonNullable: true, validators: optionalAmount() });
    }),
  );
  protected readonly sameAmount = new FormControl('', { nonNullable: true, validators: amountValidator(true) });
  // [formGroup] makes (ngSubmit) work and stops the browser's own submit.
  protected readonly form = new FormGroup({ months: this.months });

  private readonly values = toSignal(this.months.valueChanges, { initialValue: this.months.getRawValue() });

  // "Planned savings after" per month. Unassigned carries (R-PLN-03), so a change in one month moves that
  // month and every later one: each month's saved Unassigned minus the changes made up to it.
  protected readonly after = computed(() => {
    const unassigned = new Map(this.data.plans.totals.map((row) => [row.periodKey, toMinor(row.unassigned)]));
    let change = 0;
    let valid = true;
    return this.periods.map((period, i) => {
      const typed = minorOf(this.values()[i] ?? '');
      if (typed === null) {
        valid = false;
      } else {
        change += typed - toMinor(this.saved[period.key] ?? 0);
      }
      if (!valid) {
        return null;
      }
      const minor = (unassigned.get(period.key) ?? 0) - change;
      return { text: formatMoney(minor, this.data.currency), over: minor < 0 };
    });
  });

  // The FY total as typed, or null while a month is not valid.
  protected readonly total = computed(() => {
    const minors = this.values().map(minorOf);
    if (minors.some((minor) => minor === null)) {
      return null;
    }
    return formatMoney((minors as number[]).reduce((sum, minor) => sum + minor, 0), this.data.currency);
  });

  protected readonly busy = signal(false);
  protected readonly error = signal('');

  // PLN-02 "Same amount for all months".
  protected applyToAll(): void {
    if (this.sameAmount.invalid) {
      this.sameAmount.markAsTouched();
      return;
    }
    const value = this.sameAmount.value.trim();
    for (const control of this.months.controls) {
      control.setValue(value);
    }
  }

  // PLN-02 "Copy from previous month".
  protected copyPrevious(index: number): void {
    this.months.at(index).setValue(this.months.at(index - 1).value);
  }

  // PLN-04: any month, closed months included. Empty months are left out, which clears them.
  protected async save(): Promise<void> {
    if (this.months.invalid) {
      this.months.markAllAsTouched();
      return;
    }
    const amounts: Record<string, number> = {};
    this.periods.forEach((period, i) => {
      const minor = parseAmount(this.months.at(i).value, true);
      if (minor !== null) {
        amounts[period.key] = minor / 100;
      }
    });
    this.busy.set(true);
    this.error.set('');
    try {
      this.dialogRef.close(await this.api.savePlan(this.data.plans.fiscalYear.id, this.data.sub.id, amounts));
    } catch {
      this.error.set('The amounts could not be saved. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
