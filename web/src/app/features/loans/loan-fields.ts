import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, input } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { ErrorStateMatcher, provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { amountValidator, parseAmount } from '../../shared/money';
import { toDateString } from '../../shared/month-label';
import { NAME_MAX_LENGTH, nameValidator } from '../../shared/names';
import { NOTE_MAX_LENGTH, TouchedErrorStateMatcher } from '../add-entry/entry-fields';
import { LoanAccount, LoanType, NewLoan } from './loans-api';

// R-LON-01: the loan form of the New loan tab (LON-02) and of the edit dialog (LON-03). Any date
// counts, so the date picker has no range.
export function createLoanForm() {
  return new FormGroup({
    type: new FormControl<LoanType>('lent', { nonNullable: true }),
    name: new FormControl('', { nonNullable: true, validators: nameValidator() }),
    amount: new FormControl('', { nonNullable: true, validators: amountValidator() }),
    date: new FormControl<Date | null>(null, Validators.required),
    bankAccountId: new FormControl('', { nonNullable: true, validators: Validators.required }),
    note: new FormControl('', { nonNullable: true, validators: Validators.maxLength(NOTE_MAX_LENGTH) }),
  });
}

export type LoanForm = ReturnType<typeof createLoanForm>;

export function loanFromForm(form: LoanForm): NewLoan {
  const value = form.getRawValue();
  return {
    name: value.name.trim(),
    type: value.type,
    amount: parseAmount(value.amount)! / 100,
    date: toDateString(value.date!),
    bankAccountId: value.bankAccountId,
    note: value.note.trim(),
  };
}

export function showAllLoanErrors(form: LoanForm): void {
  form.markAllAsTouched();
  Object.values(form.controls).forEach((control) => control.markAsDirty());
}

// 400 comes back as ProblemDetails with C# property names. False when the error belongs to no field.
export function applyLoanServerErrors(form: LoanForm, error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) {
    return false;
  }
  const controls = form.controls;
  const fields: Record<string, AbstractControl> = {
    Name: controls.name,
    Type: controls.type,
    Amount: controls.amount,
    Date: controls.date,
    BankAccountId: controls.bankAccountId,
    Note: controls.note,
  };
  const known = Object.entries((error.error?.errors ?? {}) as Record<string, string[]>).filter(([field]) => fields[field]);
  for (const [field, messages] of known) {
    fields[field].setErrors({ server: messages[0] });
    fields[field].markAsTouched();
    fields[field].markAsDirty();
  }
  return known.length > 0;
}

// R-LON-01 fields: Lent | Borrowed, Name, Amount, Date | Bank account, Note. The page or dialog owns
// the form and its Save. Archived accounts are not offered, except the one an edited loan names.
@Component({
  selector: 'app-loan-fields',
  imports: [ReactiveFormsModule, MatButtonToggleModule, MatDatepickerModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  providers: [OUTLINE_FORM_FIELDS, provideNativeDateAdapter(), { provide: ErrorStateMatcher, useClass: TouchedErrorStateMatcher }],
  templateUrl: './loan-fields.html',
  styleUrls: ['../add-entry/entry-fields.scss', './loan-fields.scss'],
})
export class LoanFields {
  readonly form = input.required<LoanForm>();
  readonly accounts = input.required<LoanAccount[]>();
  readonly currency = input.required<string>();
  readonly keep = input<string | null>(null);

  protected readonly nameMax = NAME_MAX_LENGTH;
  protected readonly noteMax = NOTE_MAX_LENGTH;

  protected readonly options = computed(() => this.accounts().filter((account) => !account.archived || account.id === this.keep()));
}
