import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentType } from '@angular/cdk/portal';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom, map } from 'rxjs';
import { MeService } from '../../core/me';
import { dialogSize } from '../../shared/dialog';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { formatMoney, parseAmount, toMinor } from '../../shared/money';
import { formatDate, fromDateString, toDateString } from '../../shared/month-label';
import { LoanDialog, LoanDialogData } from './loan-dialog';
import { LoanFields, applyLoanServerErrors, createLoanForm, loanFromForm, showAllLoanErrors } from './loan-fields';
import { Loan, LoanType, LoansApi, LoansResponse } from './loans-api';
import { SettleDialog, SettleDialogData } from './settle-dialog';

const PHONE = '(max-width: 767.98px)';

// LON-02, LON-03: in the URL as ?tab=, as Plan's tabs.
export const LOAN_TABS = [
  { id: 'new', label: 'New loan' },
  { id: 'all', label: 'All loans' },
] as const;

export type LoanTab = (typeof LOAN_TABS)[number]['id'];
type TypeFilter = 'all' | LoanType;
type StatusFilter = 'open' | 'settled' | 'all';

const DAY_MS = 24 * 60 * 60 * 1000;

// Whole days from one YYYY-MM-DD date to another.
function daysBetween(from: string, to: string): number {
  const utc = (date: string) => {
    const [year, month, day] = date.split('-').map(Number);
    return Date.UTC(year, month - 1, day);
  };
  return Math.round((utc(to) - utc(from)) / DAY_MS);
}

export function typeLabel(type: LoanType): string {
  return type === 'lent' ? 'Lent' : 'Borrowed';
}

// LON-03: "Open · 34 days", "Open · since today", "Starts 5 Dec 2026", "Returned 12 Nov 2026" or
// "Paid 12 Nov 2026".
export function loanStatus(loan: Loan, today: string): string {
  if (loan.settledOn) {
    return `${loan.type === 'lent' ? 'Returned' : 'Paid'} ${formatDate(loan.settledOn)}`;
  }
  if (loan.date > today) {
    return `Starts ${formatDate(loan.date)}`;
  }
  const days = daysBetween(loan.date, today);
  return days === 0 ? 'Open · since today' : `Open · ${days} ${days === 1 ? 'day' : 'days'}`;
}

// PRD 5.12 Loans (owner note 20): information only (R-LON-02), so nothing here changes a balance, the
// plan or Tracker. One call (GET /api/loans) serves both tabs, and every change answers with it. The
// page has no filled button but Save on New loan.
@Component({
  selector: 'app-loans',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    LoanFields,
  ],
  providers: [OUTLINE_FORM_FIELDS],
  templateUrl: './loans.html',
  styleUrl: './loans.scss',
})
export class Loans {
  private readonly api = inject(LoansApi);
  private readonly meService = inject(MeService);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(BreakpointObserver);
  private readonly route = inject(ActivatedRoute);

  protected readonly tabs = LOAN_TABS;
  protected readonly tab = toSignal(
    this.route.queryParamMap.pipe(map((params) => (params.get('tab') === 'all' ? 'all' : 'new') as LoanTab)),
    { initialValue: 'new' as LoanTab },
  );
  protected readonly phone = toSignal(this.breakpoints.observe(PHONE).pipe(map((state) => state.matches)), {
    initialValue: this.breakpoints.isMatched(PHONE),
  });

  protected readonly data = signal<LoansResponse | null>(null);
  protected readonly loadError = signal('');
  protected readonly status = signal('');
  protected readonly busy = signal(false);
  protected readonly saveError = signal('');
  protected readonly currency = computed(() => this.meService.me()?.profile?.currency ?? 'SAR');
  protected readonly typeLabel = typeLabel;

  // LON-02: the form and what it is now.
  protected readonly form = createLoanForm();
  private readonly value = signal(this.form.getRawValue());

  // LON-03 filters.
  protected readonly typeFilter = signal<TypeFilter>('all');
  protected readonly statusFilter = signal<StatusFilter>('open');
  protected readonly search = signal('');

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.value.set(this.form.getRawValue()));
    void this.load();
  }

  protected money(amount: number): string {
    return formatMoney(toMinor(amount), this.currency());
  }

  protected accountName(id: string): string {
    return this.data()?.accounts.find((account) => account.id === id)?.name ?? '';
  }

  // LON-02 totals: the chosen type large, and how the new loan changes it while you type.
  protected readonly totals = computed(() => {
    const data = this.data();
    if (!data) {
      return null;
    }
    const { type, amount } = this.value();
    const added = parseAmount(amount) ?? 0;
    const chosen = type === 'lent' ? data.totals.lent : data.totals.borrowed;
    const now = toMinor(chosen.open);
    const net = toMinor(data.totals.net);
    return {
      label: typeLabel(type),
      now: this.minor(now),
      after: added > 0 ? this.minor(now + added) : null,
      count: chosen.openCount,
      other: {
        label: typeLabel(type === 'lent' ? 'borrowed' : 'lent'),
        amount: this.money((type === 'lent' ? data.totals.borrowed : data.totals.lent).open),
      },
      net: this.minor(net),
      netAfter: added > 0 ? this.minor(net + (type === 'lent' ? added : -added)) : null,
    };
  });

  // LON-02: what NAMA knows about the chosen account and date. It does not change (R-LON-02).
  protected readonly outlook = computed(() => {
    const data = this.data();
    const { bankAccountId, date, amount } = this.value();
    if (!data || !bankAccountId || !date) {
      return null;
    }
    const day = toDateString(date);
    const month = data.months.find((end) => end.period.startDate <= day && day <= end.period.endDate);
    const first = data.months[0]?.period.startDate;
    // A month that has started, or a date before the current FY: today's balance is what NAMA knows.
    if (day <= data.today || (month && month.period.startDate <= data.today) || (first && day < first)) {
      const account = data.accounts.find((item) => item.id === bankAccountId);
      const balance = account?.balance ?? null;
      const added = parseAmount(amount) ?? 0;
      const share = balance !== null && balance > 0 && added > 0 ? Math.round((added / toMinor(balance)) * 100) : null;
      return {
        kind: 'today' as const,
        account: account?.name ?? '',
        balance: balance === null ? null : this.money(balance),
        share: share === null ? null : `${this.minor(added)} is ${share}% of it.`,
      };
    }
    if (month) {
      return {
        kind: 'projected' as const,
        month: month.period.label,
        endDate: formatDate(month.period.endDate),
        balance: month.balance === null ? null : this.money(month.balance),
      };
    }
    return { kind: 'none' as const };
  });

  // LON-03: the shown loans, newest first (the API's order).
  protected readonly rows = computed(() => {
    const data = this.data();
    if (!data) {
      return [];
    }
    const type = this.typeFilter();
    const state = this.statusFilter();
    const query = this.search().trim().toLowerCase();
    return data.loans
      .filter((loan) => type === 'all' || loan.type === type)
      .filter((loan) => state === 'all' || (state === 'open') === (loan.settledOn === null))
      .filter((loan) => !query || loan.name.toLowerCase().includes(query))
      .map((loan) => ({
        loan,
        date: formatDate(loan.date),
        type: typeLabel(loan.type),
        amount: this.money(loan.amount),
        account: this.accountName(loan.bankAccountId),
        status: loanStatus(loan, data.today),
        open: loan.settledOn === null,
        settleLabel: loan.type === 'lent' ? 'Mark returned' : 'Mark paid',
      }));
  });

  // The Note column only when a shown loan has a note.
  protected readonly hasNotes = computed(() => this.rows().some((row) => row.loan.note));

  // LON-03: the totals of the shown rows, one line under the filters (as Entries' summary):
  // "3 loans · Lent SAR 200 · Borrowed SAR 100 · Lent − Borrowed SAR 100".
  protected readonly summary = computed(() => {
    const rows = this.rows();
    const sum = (type: LoanType) => rows.filter((row) => row.loan.type === type).reduce((total, row) => total + toMinor(row.loan.amount), 0);
    const lent = sum('lent');
    const borrowed = sum('borrowed');
    return {
      count: `${rows.length} ${rows.length === 1 ? 'loan' : 'loans'}`,
      lent: this.minor(lent),
      borrowed: this.minor(borrowed),
      net: this.minor(lent - borrowed),
      negative: lent - borrowed < 0,
    };
  });

  // By person, with "–" for nothing.
  protected readonly people = computed(() => {
    const query = this.search().trim().toLowerCase();
    return (this.data()?.people ?? [])
      .filter((person) => !query || person.name.toLowerCase().includes(query))
      .map((person) => ({
        name: person.name,
        lent: person.lentOpen ? this.money(person.lentOpen) : '–',
        borrowed: person.borrowedOpen ? this.money(person.borrowedOpen) : '–',
        open: person.openCount,
      }));
  });

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      showAllLoanErrors(this.form);
      return;
    }
    const loan = loanFromForm(this.form);
    this.busy.set(true);
    this.saveError.set('');
    this.status.set('');
    try {
      this.data.set(await this.api.add(loan));
      this.status.set(
        `Added ${typeLabel(loan.type)} · ${loan.name} · ${this.money(loan.amount)} · ${loan.type === 'lent' ? 'from' : 'into'} ${this.accountName(loan.bankAccountId)}`,
      );
      this.form.controls.name.reset('');
      this.form.controls.amount.reset('');
      this.form.controls.note.reset('');
    } catch (error: unknown) {
      if (!applyLoanServerErrors(this.form, error)) {
        this.saveError.set('The loan could not be saved. Please try again.');
      }
    } finally {
      this.busy.set(false);
    }
  }

  // R-LON-03: returned or paid in full, on a date.
  protected async settle(loan: Loan): Promise<void> {
    const response = await this.open<SettleDialog, SettleDialogData, LoansResponse>(
      SettleDialog,
      { loan, today: this.data()!.today, currency: this.currency() },
      '420px',
    );
    if (response) {
      this.data.set(response);
      this.status.set(`${loan.type === 'lent' ? 'Returned' : 'Paid'}: ${loan.name} · ${this.money(loan.amount)}.`);
    }
  }

  // Undoing a mark needs no confirmation: it can be marked again.
  protected async reopen(loan: Loan): Promise<void> {
    this.status.set('');
    try {
      this.data.set(await this.api.settle(loan.id, null));
      this.status.set(`Open again: ${loan.name} · ${this.money(loan.amount)}.`);
    } catch {
      this.status.set('The loan could not be changed. Please try again.');
    }
  }

  protected async edit(loan: Loan): Promise<void> {
    const response = await this.open<LoanDialog, LoanDialogData, LoansResponse>(LoanDialog, {
      loan,
      accounts: this.data()!.accounts,
      currency: this.currency(),
    });
    if (response) {
      this.data.set(response);
      this.status.set(response.loans.some((item) => item.id === loan.id) ? 'Loan saved.' : 'Loan deleted.');
    }
  }

  private async load(): Promise<void> {
    try {
      await this.meService.load();
      const data = await this.api.loans();
      this.data.set(data);
      // Defaults: today, and the last used account (else the first active one).
      const active = data.accounts.filter((account) => !account.archived);
      this.form.patchValue({
        date: fromDateString(data.today),
        bankAccountId: (active.find((account) => account.id === data.lastBankAccountId) ?? active[0])?.id ?? '',
      });
    } catch {
      this.loadError.set('Loans could not be loaded. Please reload the page.');
    }
  }

  private minor(minor: number): string {
    return formatMoney(minor, this.currency());
  }

  private open<C, D, R>(component: ComponentType<C>, data: D, width?: string): Promise<R | undefined> {
    this.status.set('');
    const ref = this.dialog.open<C, D, R>(component, { ...dialogSize(this.breakpoints.isMatched(PHONE), width), data });
    return firstValueFrom(ref.afterClosed());
  }
}
