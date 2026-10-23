import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Summary } from '../../core/summary';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { INCOME_COUNTS, IncomeCounts, countsHint, untilNotBeforeFrom } from '../../shared/income-months';
import { amountValidator, formatMoney, minorToInput, parseAmount, toMinor } from '../../shared/money';
import { PeriodInfo, formatDate, monthKeyLabel } from '../../shared/month-label';
import { NAME_MAX_LENGTH, nameValidator } from '../../shared/names';
import { BankAccount } from '../add-entry/entry-api';
import { Income, IncomeItem, IncomeItemChange } from './settings-api';
import { MonthGroup, monthGroups } from './settings-lines';

// R-INC-05: an Actual or Both item names the account it goes into. Only an item that already goes
// through the income split (made in the wizard, or before owner note 17) may keep "" (the split).
function accountValidator(allowSplit: boolean) {
  return (control: AbstractControl): ValidationErrors | null =>
    control.parent?.get('counts')?.value !== 'planned' && !control.value && !allowSplit ? { required: true } : null;
}

// R-INC-03 to R-INC-05: an additional income item's form, the same in the Settings dialog and on Add
// (INC-01, owner note 17): Label, Amount, Type, From (or Month), Until, Counts in, and for Actual or
// Both the Bank account. A new item is Monthly from defaultKey, counts in both and goes into
// defaultAccountId.
export function createIncomeItemForm(item: IncomeItem | null, defaultKey: string | null, defaultAccountId: string | null) {
  const allowSplit = !!item && item.counts !== 'planned' && !item.bankAccountId;
  const form = new FormGroup(
    {
      label: new FormControl(item?.label ?? '', { nonNullable: true, validators: nameValidator() }),
      amount: new FormControl(item ? minorToInput(toMinor(item.amount)) : '', { nonNullable: true, validators: amountValidator() }),
      type: new FormControl<'once' | 'monthly'>(item?.type ?? 'monthly', { nonNullable: true, validators: Validators.required }),
      from: new FormControl(item?.fromPeriodKey ?? defaultKey ?? '', { nonNullable: true, validators: Validators.required }),
      to: new FormControl(item?.type === 'monthly' ? (item.toPeriodKey ?? '') : '', { nonNullable: true }),
      counts: new FormControl<IncomeCounts>(item?.counts ?? 'both', { nonNullable: true }),
      bankAccountId: new FormControl(item?.bankAccountId ?? (allowSplit ? '' : (defaultAccountId ?? '')), {
        nonNullable: true,
        validators: accountValidator(allowSplit),
      }),
    },
    { validators: untilNotBeforeFrom },
  );
  // The validator reads Counts in, which exists only now.
  form.controls.bankAccountId.updateValueAndValidity();
  form.controls.counts.valueChanges.subscribe(() => form.controls.bankAccountId.updateValueAndValidity());
  return form;
}

export type IncomeItemForm = ReturnType<typeof createIncomeItemForm>;

// What POST and PUT /api/income/additional take; null (and every error shown) when the form is invalid.
export function incomeItemChange(form: IncomeItemForm): IncomeItemChange | null {
  if (form.invalid) {
    form.markAllAsTouched();
    return null;
  }
  const value = form.getRawValue();
  return {
    label: value.label.trim(),
    amount: parseAmount(value.amount)! / 100,
    counts: value.counts,
    type: value.type,
    fromPeriodKey: value.from,
    toPeriodKey: value.type === 'monthly' && value.to ? value.to : null,
    bankAccountId: value.counts === 'planned' || !value.bankAccountId ? null : value.bankAccountId,
  };
}

// What the preview needs, in minor units. periods are the stored months, oldest first (each payday is a
// month's first day). item is the edited one, so its own amounts are not counted twice.
export interface IncomeItemContext {
  groups: MonthGroup[];
  periods: PeriodInfo[];
  accounts: BankAccount[];
  today: string | null;
  plannedIncome: Record<string, number>;
  balances: Record<string, { balance: number | null; openingDate: string | null }>;
  item: IncomeItem | null;
}

// From GET /api/income, the top bar's summary (R-BAL-03 balances) and /api/me's today.
export function incomeItemContext(income: Income, summary: Summary | null, today: string | null, item: IncomeItem | null): IncomeItemContext {
  return {
    groups: monthGroups(income.fiscalYears),
    periods: income.fiscalYears.flatMap((fy) => fy.periods),
    accounts: income.bankAccounts,
    today,
    plannedIncome: Object.fromEntries(income.totals.map((month) => [month.periodKey, toMinor(month.planned)])),
    balances: Object.fromEntries(
      (summary?.accounts ?? []).map((account) => [
        account.id,
        { balance: account.balance === null ? null : toMinor(account.balance), openingDate: account.openingDate },
      ]),
    ),
    item,
  };
}

interface ItemShape {
  amount: number;
  type: 'once' | 'monthly';
  from: string;
  to: string | null;
}

// The fields and, under them, what the item changes (owner note 17): for Actual or Both the account's
// balance today and after (R-BAL-03: only the paydays that started from its opening date count), for
// Planned or Both Plan's income in the From month. A preview only; the saved numbers come from the API.
// The dialog and Add own the form and its Save. The month pickers offer the stored months (R-CAL-09).
@Component({
  selector: 'app-income-item-fields',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule],
  providers: [OUTLINE_FORM_FIELDS],
  templateUrl: './income-item-fields.html',
  styleUrl: './income-item-fields.scss',
})
export class IncomeItemFields implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  readonly form = input.required<IncomeItemForm>();
  readonly context = input.required<IncomeItemContext>();
  readonly currency = input.required<string>();

  protected readonly nameMax = NAME_MAX_LENGTH;
  protected readonly countsOptions = INCOME_COUNTS;

  private readonly value = signal<ReturnType<IncomeItemForm['getRawValue']> | null>(null);
  protected readonly type = computed(() => this.value()?.type ?? 'monthly');
  protected readonly counts = computed(() => this.value()?.counts ?? 'both');
  protected readonly hint = computed(() => countsHint(this.counts()));

  // Archived accounts are never offered, except the one an edited item already has.
  protected readonly accountOptions = computed(() =>
    this.context().accounts.filter((account) => !account.archived || account.id === this.context().item?.bankAccountId),
  );
  // An item that already goes through the split may keep it.
  protected readonly offersSplit = computed(() => {
    const item = this.context().item;
    return !!item && item.counts !== 'planned' && !item.bankAccountId;
  });

  // "Into SNB · SAR 4,100 → SAR 4,600", and the edited item's old account when it changes.
  protected readonly bankPreview = computed(() => {
    const value = this.value();
    const context = this.context();
    if (!value || value.counts === 'planned') {
      return null;
    }
    const shape = this.shape(value);
    const old = context.item && context.item.counts !== 'planned' ? this.oldShape(context.item) : null;
    const accountId = value.bankAccountId;
    const lines: { caption: string; name: string; now: string | null; after: string | null; negative: boolean }[] = [];
    const line = (caption: string, id: string, change: number) => {
      const name = context.accounts.find((account) => account.id === id)?.name ?? '';
      const balance = context.balances[id]?.balance ?? null;
      lines.push({
        caption,
        name,
        now: balance === null ? null : this.money(balance),
        after: balance === null || change === 0 ? null : this.money(balance + change),
        negative: balance !== null && balance + change < 0,
      });
    };
    if (accountId) {
      const sameAccount = old !== null && context.item?.bankAccountId === accountId;
      line('Into', accountId, this.arrived(shape, accountId) - (sameAccount ? this.arrived(old, accountId) : 0));
    }
    const oldAccount = context.item?.bankAccountId;
    if (old && oldAccount && oldAccount !== accountId) {
      line('Out of', oldAccount, -this.arrived(old, oldAccount));
    }
    let note = accountId ? this.arrivalNote(shape) : 'The income split shares it out on payday (Banks).';
    if (old && accountId && !oldAccount) {
      note += ' Until now the income split shared it, so the other accounts change too.';
    }
    return { lines, note };
  });

  // "Plan's income in Sep 2026 · SAR 17,000 → SAR 17,500".
  protected readonly planPreview = computed(() => {
    const value = this.value();
    const context = this.context();
    if (!value || value.counts === 'actual' || !value.from) {
      return null;
    }
    const shape = this.shape(value);
    const item = context.item;
    const known = value.from in context.plannedIncome;
    const before =
      (context.plannedIncome[value.from] ?? 0) -
      (item && item.counts !== 'actual' && this.covers(this.oldShape(item), value.from) ? toMinor(item.amount) : 0);
    return {
      month: monthKeyLabel(value.from),
      now: known ? this.money(before) : null,
      after: known && shape.amount > 0 ? this.money(before + shape.amount) : null,
      added: shape.amount > 0 ? this.money(shape.amount) : null,
      note:
        value.type === 'once'
          ? null
          : value.to
            ? `And every month to ${monthKeyLabel(value.to)}.`
            : 'And every month after it.',
    };
  });

  ngOnInit(): void {
    const form = this.form();
    this.value.set(form.getRawValue());
    form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.value.set(form.getRawValue()));
  }

  private shape(value: ReturnType<IncomeItemForm['getRawValue']>): ItemShape {
    return {
      amount: parseAmount(value.amount) ?? 0,
      type: value.type,
      from: value.from,
      to: value.type === 'once' ? value.from : value.to || null,
    };
  }

  private oldShape(item: IncomeItem): ItemShape {
    return { amount: toMinor(item.amount), type: item.type, from: item.fromPeriodKey, to: item.toPeriodKey };
  }

  // Month keys are YYYY-MM, so string order is calendar order.
  private covers(shape: ItemShape, key: string): boolean {
    return !!shape.from && shape.from <= key && (!shape.to || key <= shape.to);
  }

  // R-BAL-03: what has arrived in the account by today: one amount per payday from its opening date.
  private arrived(shape: ItemShape, accountId: string): number {
    const { today, balances, periods } = this.context();
    const opening = balances[accountId]?.openingDate;
    if (!today || !opening) {
      return 0;
    }
    return (
      periods.filter((period) => this.covers(shape, period.key) && opening <= period.startDate && period.startDate <= today).length *
      shape.amount
    );
  }

  // "Arrives on 27 Oct 2026, Oct 2026's payday." or "SAR 500 on every payday from Sep 2026 to Dec 2026."
  private arrivalNote(shape: ItemShape): string {
    const first = this.context().periods.find((period) => period.key === shape.from);
    if (!first || shape.amount === 0) {
      return 'It goes into this account on payday.';
    }
    if (shape.type === 'once') {
      const today = this.context().today;
      return today && first.startDate > today
        ? `Arrives on ${formatDate(first.startDate)}, ${first.label}'s payday.`
        : `Counted from ${formatDate(first.startDate)}, ${first.label}'s payday.`;
    }
    const until = shape.to ? ` to ${monthKeyLabel(shape.to)}` : '';
    return `${this.money(shape.amount)} on every payday from ${first.label}${until}.`;
  }

  private money(minor: number): string {
    return formatMoney(minor, this.currency());
  }
}
