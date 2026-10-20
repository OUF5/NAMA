import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { distribute, formatMoney, formatPercent, parsePercent, percentValidator, toBasisPoints, toMinor } from '../../shared/money';
import { monthKeyLabel } from '../../shared/month-label';
import { MonthGroup } from '../settings/settings-lines';
import { Account, BanksApi, BanksResponse, Split } from './banks-api';

export interface SplitDialogData {
  // The active accounts, by name.
  accounts: Account[];
  // The split shown on Banks, null when there is none yet.
  split: Split | null;
  groups: MonthGroup[];
  defaultKey: string;
  // Month key → total income, for the preview.
  income: Record<string, number>;
  currency: string;
}

// "80" for the input, "33.33" with decimals.
function percentToInput(percent: number): string {
  const basisPoints = toBasisPoints(percent);
  return basisPoints % 100 === 0 ? String(basisPoints / 100) : (basisPoints / 100).toFixed(2);
}

// BNK-03, R-BAL-02: how each month's income is split across the active accounts, from a month on
// (like the salary, R-INC-02: earlier months keep their split). The shares add up to 100%. Each one
// shows what it gives in the chosen month while you type, a preview only (exact minor units, the
// API's rounding); the saved amounts come from the API. Closes with the new GET /api/banks.
@Component({
  selector: 'app-split-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  providers: [OUTLINE_FORM_FIELDS],
  templateUrl: './split-dialog.html',
  styleUrls: ['../settings/dialog.scss', './split-dialog.scss'],
})
export class SplitDialog {
  private readonly api = inject(BanksApi);
  private readonly dialogRef = inject<MatDialogRef<SplitDialog, BanksResponse>>(MatDialogRef);
  protected readonly data = inject<SplitDialogData>(MAT_DIALOG_DATA);

  protected readonly from = new FormControl(this.data.defaultKey, { nonNullable: true, validators: Validators.required });
  // Without a split yet, all income goes to the first account until it is changed.
  protected readonly shares = new FormGroup(
    Object.fromEntries(
      this.data.accounts.map((account, i) => [
        account.id,
        new FormControl(
          this.data.split ? percentToInput(this.data.split.shares[account.id] ?? 0) : i === 0 ? '100' : '0',
          { nonNullable: true, validators: percentValidator() },
        ),
      ]),
    ),
  );
  protected readonly form = new FormGroup({ from: this.from, shares: this.shares });

  private readonly fromKey = toSignal(this.from.valueChanges, { initialValue: this.from.value });
  private readonly values = toSignal(this.shares.valueChanges, { initialValue: this.shares.getRawValue() });

  // Basis points per account, or null while one is not a valid percent.
  private readonly basisPoints = computed(() => {
    const values = this.values() as Record<string, string>;
    const result: Record<string, number> = {};
    for (const account of this.data.accounts) {
      const value = parsePercent(values[account.id] ?? '');
      if (value === null) {
        return null;
      }
      result[account.id] = value;
    }
    return result;
  });

  protected readonly total = computed(() => {
    const shares = this.basisPoints();
    return shares === null ? null : Object.values(shares).reduce((sum, share) => sum + share, 0);
  });
  protected readonly totalText = computed(() => (this.total() === null ? '–' : formatPercent(this.total()!)));
  protected readonly complete = computed(() => this.total() === 10_000);

  protected readonly monthLabel = computed(() => monthKeyLabel(this.fromKey()));
  protected readonly monthIncome = computed(() => formatMoney(toMinor(this.data.income[this.fromKey()] ?? 0), this.data.currency));

  // What each account gets in the chosen month, once the shares add up to 100%.
  protected readonly amounts = computed(() => {
    const shares = this.basisPoints();
    if (shares === null || !this.complete()) {
      return null;
    }
    const parts = distribute(toMinor(this.data.income[this.fromKey()] ?? 0), shares);
    return Object.fromEntries(this.data.accounts.map((account) => [account.id, formatMoney(parts[account.id] ?? 0, this.data.currency)]));
  });

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly tried = signal(false);

  // "Put everything in one account": that account 100%, the rest 0%.
  protected allTo(id: string): void {
    for (const account of this.data.accounts) {
      this.shares.controls[account.id].setValue(account.id === id ? '100' : '0');
    }
  }

  protected async save(): Promise<void> {
    this.tried.set(true);
    if (this.form.invalid || !this.complete()) {
      this.form.markAllAsTouched();
      return;
    }
    const shares = Object.fromEntries(
      Object.entries(this.basisPoints()!).map(([id, basisPoints]) => [id, basisPoints / 100]),
    );
    this.busy.set(true);
    this.error.set('');
    try {
      this.dialogRef.close(await this.api.setSplit(this.from.value, shares));
    } catch {
      this.error.set('The split could not be saved. Please check the shares and try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
