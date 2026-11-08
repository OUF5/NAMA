import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentType } from '@angular/cdk/portal';
import { DOCUMENT } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Params, Router, RouterLink } from '@angular/router';
import { debounceTime, firstValueFrom, map } from 'rxjs';
import { MeService } from '../../core/me';
import { SummaryService } from '../../core/summary';
import { dialogSize } from '../../shared/dialog';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { countsLabel } from '../../shared/income-months';
import { formatMoney, toMinor } from '../../shared/money';
import { formatDate, periodWithDates } from '../../shared/month-label';
import { Category, PlanApi } from '../plan/plan-api';
import { Entry, EntriesResponse, EntryApi, EntryFilters, EntryType, IncomeRow, TransferRow } from '../add-entry/entry-api';
import { NOTE_MAX_LENGTH } from '../add-entry/entry-fields';
import type { TransferDialog, TransferDialogData, TransferDialogResult } from '../banks/transfer-dialog';
import type { IncomeItemDialog, IncomeItemDialogData } from '../settings/income-item-dialog';
import { Income, SettingsApi } from '../settings/settings-api';
import { openEntryDialog } from './entry-dialog';

const PHONE = '(max-width: 767.98px)';

// The filters live in the URL with the API parameter names, so Months can open Entries
// filtered by a sub-category and month (MON-04).
const FILTER_KEYS = ['fyId', 'type', 'periodKey', 'categoryId', 'subCategoryId', 'bankAccountId', 'q'] as const;
type FilterKey = (typeof FILTER_KEYS)[number];

// TBL-07 (owner note 33): the Type filter's choices, and each type's name on its chip.
export const ENTRY_TYPES: { value: EntryType; filter: string; label: string }[] = [
  { value: 'expense', filter: 'Expenses', label: 'Expense' },
  { value: 'transfer', filter: 'Transfers', label: 'Transfer' },
  { value: 'income', filter: 'Income', label: 'Income' },
];
const TYPE_LABELS = Object.fromEntries(ENTRY_TYPES.map((type) => [type.value, type.label])) as Record<EntryType, string>;

// One line of the table (or one card on phones), whatever its type.
interface Row {
  key: string;
  type: EntryType;
  typeLabel: string;
  isoDate: string;
  date: string;
  month: string;
  // The table's Details: "Food · Groceries", "–" for a transfer, "Bonus · Planned and actual".
  details: string;
  // The card's first line: the sub-category, "SNB → Al Rajhi" or the income's label; and what its
  // caption adds: the category, nothing, or what the income counts in.
  title: string;
  cardDetail: string;
  amount: string;
  account: string;
  note: string;
  editLabel: string;
  edit: () => void;
}

// PRD 5.5 Entries (TBL-01 to TBL-07). Page load is 2 calls (NFR-01): categories, and the rows of
// every type with the FYs, the accounts and each type's total. A filter change, a save or a delete
// is 1 call; editing income loads its item first (GET /api/income, only then). The API sums the
// totals (R-MON-01); the page only formats them.
@Component({
  selector: 'app-entries',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
  ],
  providers: [OUTLINE_FORM_FIELDS],
  templateUrl: './entries.html',
  styleUrl: './entries.scss',
})
export class Entries {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly planApi = inject(PlanApi);
  private readonly entryApi = inject(EntryApi);
  private readonly settingsApi = inject(SettingsApi);
  private readonly meService = inject(MeService);
  private readonly summaryService = inject(SummaryService);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(BreakpointObserver);
  private readonly document = inject(DOCUMENT);

  protected readonly noteMax = NOTE_MAX_LENGTH;
  protected readonly types = ENTRY_TYPES;
  protected readonly formatDate = formatDate;
  protected readonly periodWithDates = periodWithDates;

  // TBL-05: cards on phones, a table on desktop.
  protected readonly phone = toSignal(this.breakpoints.observe(PHONE).pipe(map((state) => state.matches)), {
    initialValue: false,
  });

  protected readonly loadError = signal('');
  protected readonly categories = signal<Category[] | null>(null);
  protected readonly data = signal<EntriesResponse | null>(null);
  protected readonly status = signal('');
  protected readonly exportError = signal('');
  protected readonly exporting = signal(false);
  protected readonly filters = signal<EntryFilters>({});
  // Typed text is sent after a pause, and the box is never overwritten while typing.
  protected readonly search = new FormControl('', { nonNullable: true });

  protected readonly currency = computed(() => this.meService.me()?.profile?.currency ?? 'SAR');
  protected readonly fiscalYear = computed(() => {
    const data = this.data();
    return data?.fiscalYears.find((fiscalYear) => fiscalYear.id === data.fyId) ?? null;
  });

  // TBL-07: a category is about expenses only, so it is off while Transfers or Income is chosen.
  protected readonly categoryOff = computed(() => this.filters().type === 'transfer' || this.filters().type === 'income');

  // A URL with only a sub-category also shows its category.
  protected readonly categoryId = computed(() => {
    const filters = this.filters();
    return filters.categoryId || this.categoryOf(filters.subCategoryId) || '';
  });
  protected readonly subCategoryOptions = computed(
    () => this.categories()?.find((category) => category.id === this.categoryId())?.subCategories ?? [],
  );

  protected readonly hasFilters = computed(() => {
    const filters = this.filters();
    return !!(
      filters.type ||
      filters.periodKey ||
      filters.categoryId ||
      filters.subCategoryId ||
      filters.bankAccountId ||
      filters.q?.trim()
    );
  });

  // TBL-01, TBL-07: date, month, type, details, amount, bank account, note, newest first; on one day,
  // expenses, then transfers, then income (as the CSV). Names are looked up by id.
  protected readonly rows = computed<Row[]>(() => {
    const data = this.data();
    if (!data) {
      return [];
    }
    const categories = this.categories() ?? [];
    const categoryNames = new Map(categories.map((category) => [category.id, category.name]));
    const subNames = new Map(categories.flatMap((category) => category.subCategories).map((sub) => [sub.id, sub.name]));
    const accountNames = new Map(data.bankAccounts.map((account) => [account.id, account.name]));
    const account = (id: string | null) => (id ? (accountNames.get(id) ?? '') : '');
    const months = new Map(this.fiscalYear()?.periods.map((period) => [period.key, period.label]) ?? []);
    const row = (type: EntryType, key: string, isoDate: string, periodKey: string, fields: Pick<Row, 'details' | 'title' | 'cardDetail' | 'account' | 'note'>, amount: number, edit: () => void): Row => {
      const money = this.money(amount);
      const date = formatDate(isoDate);
      return {
        key: `${type}-${key}`,
        type,
        typeLabel: TYPE_LABELS[type],
        isoDate,
        date,
        month: months.get(periodKey) ?? periodKey,
        amount: money,
        editLabel: `Edit ${fields.title}, ${money}, ${date}`,
        edit,
        ...fields,
      };
    };
    return [
      ...data.entries.map((entry) => {
        const category = categoryNames.get(entry.categoryId) ?? '';
        const sub = subNames.get(entry.subCategoryId) ?? '';
        return row('expense', entry.id, entry.date, entry.periodKey, {
          details: `${category} · ${sub}`,
          title: sub,
          cardDetail: category,
          account: account(entry.bankAccountId),
          note: entry.note,
        }, entry.amount, () => this.editEntry(entry));
      }),
      ...data.transfers.map((transfer) => {
        const accounts = `${account(transfer.fromBankAccountId)} → ${account(transfer.toBankAccountId)}`;
        return row('transfer', transfer.id, transfer.date, transfer.periodKey, {
          details: '–',
          title: accounts,
          cardDetail: '',
          account: accounts,
          note: transfer.note,
        }, transfer.amount, () => void this.editTransfer(transfer));
      }),
      ...data.income.map((income) =>
        row('income', `${income.incomeId}-${income.periodKey}`, income.date, income.periodKey, {
          details: `${income.label} · ${countsLabel(income.counts)}`,
          title: income.label,
          cardDetail: countsLabel(income.counts),
          account: account(income.bankAccountId) || '–',
          note: '',
        }, income.amount, () => void this.editIncome(income)),
      ),
    ].sort((a, b) => b.isoDate.localeCompare(a.isoDate));
  });

  // TBL-04, TBL-07: how many rows, and each type's total (summed by the API) for the types listed;
  // added together they would mean nothing.
  protected readonly summary = computed(() => {
    const data = this.data();
    if (!data) {
      return '';
    }
    const count = this.rows().length;
    const totals = [
      data.entries.length > 0 ? `Spent ${this.money(data.total)}` : '',
      data.transfers.length > 0 ? `Moved ${this.money(data.transferTotal)}` : '',
      data.income.length > 0 ? `Income ${this.money(data.incomeTotal)}` : '',
    ].filter(Boolean);
    return [`${count} ${count === 1 ? 'entry' : 'entries'}`, ...totals].join(' · ');
  });

  private request = 0;

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const filters: EntryFilters = {};
      for (const key of FILTER_KEYS) {
        const value = params.get(key);
        if (value) {
          filters[key] = value;
        }
      }
      this.filters.set(filters);
      if (this.search.value !== (filters.q ?? '')) {
        this.search.setValue(filters.q ?? '', { emitEvent: false });
      }
      void this.load();
    });
    this.search.valueChanges
      .pipe(debounceTime(300), takeUntilDestroyed())
      .subscribe((q) => this.navigate({ ...this.filters(), q }));
  }

  // A new FY starts with all its months; a new category with all its sub-categories; Transfers or
  // Income clears the category, which is about expenses only (TBL-07).
  protected setFilter(key: FilterKey, value: string): void {
    const next: EntryFilters = { ...this.filters(), [key]: value };
    if (key === 'fyId') {
      next.periodKey = '';
    }
    if (key === 'categoryId') {
      next.subCategoryId = '';
    }
    if (key === 'type' && (value === 'transfer' || value === 'income')) {
      next.categoryId = '';
      next.subCategoryId = '';
    }
    this.navigate(next);
  }

  protected clearFilters(): void {
    this.search.setValue('', { emitEvent: false });
    this.navigate({ fyId: this.filters().fyId });
  }

  // TBL-02, TBL-03: edit and delete in one dialog, full screen on phones (docs/DESIGN.md).
  private editEntry(entry: Entry): void {
    const me = this.meService.me();
    const dialog = openEntryDialog(this.dialog, this.breakpoints.isMatched(PHONE), {
      entry,
      categories: this.categories() ?? [],
      accounts: this.data()?.bankAccounts ?? [],
      currency: this.currency(),
      firstDate: me?.firstDate ?? null,
      lastDate: me?.lastDate ?? null,
    });
    dialog.afterClosed().subscribe((result) => {
      if (result) {
        this.status.set(result === 'saved' ? 'Entry updated.' : 'Entry deleted.');
        void this.load();
      }
    });
  }

  // TBL-07: Banks' transfer dialog (BNK-04), edit or delete. Its code loads only now, so the page
  // loads no more than before (owner note 33).
  private async editTransfer(transfer: TransferRow): Promise<void> {
    const me = this.meService.me();
    const { TransferDialog } = await import('../banks/transfer-dialog');
    const result = await this.open<TransferDialog, TransferDialogData, TransferDialogResult>(TransferDialog, {
      transfer,
      accounts: this.data()?.bankAccounts ?? [],
      currency: this.currency(),
      firstDate: me?.firstDate ?? null,
      lastDate: me?.lastDate ?? null,
    });
    if (result) {
      this.status.set(result === 'saved' ? 'Transfer updated.' : 'Transfer deleted.');
      await this.load();
    }
  }

  // TBL-07: Settings' income dialog (R-INC-03), which edits or deletes the whole item. Its pickers and
  // preview need GET /api/income, loaded only now with the dialog's code (NFR-01: the page itself
  // stays at 2 calls).
  private async editIncome(row: IncomeRow): Promise<void> {
    this.status.set('');
    let income: Income;
    let dialog: typeof import('../settings/income-item-dialog');
    let fields: typeof import('../settings/income-item-fields');
    try {
      [income, dialog, fields] = await Promise.all([
        this.settingsApi.income(),
        import('../settings/income-item-dialog'),
        import('../settings/income-item-fields'),
      ]);
    } catch {
      this.status.set('The income could not be loaded. Please try again.');
      return;
    }
    const item = income.additional.find((candidate) => candidate.id === row.incomeId);
    if (!item) {
      await this.load();
      return;
    }
    const me = this.meService.me();
    const active = income.bankAccounts.filter((account) => !account.archived);
    const result = await this.open<IncomeItemDialog, IncomeItemDialogData, Income>(dialog.IncomeItemDialog, {
      item,
      context: fields.incomeItemContext(income, this.summaryService.summary(), me, item),
      defaultKey: income.currentPeriodKey,
      defaultAccountId: (active.find((account) => account.id === me?.profile?.lastBankAccountId) ?? active[0])?.id ?? null,
      currency: this.currency(),
      note: item.type === 'monthly' ? 'Changes apply to every month of this income.' : undefined,
    });
    if (result) {
      this.status.set('Income updated.');
      await this.load();
    }
  }

  private open<C, D, R>(component: ComponentType<C>, data: D): Promise<R | undefined> {
    const ref = this.dialog.open<C, D, R>(component, { ...dialogSize(this.breakpoints.isMatched(PHONE)), data });
    return firstValueFrom(ref.afterClosed());
  }

  // TBL-06: fetched with the token (the interceptor), then saved from a temporary link.
  protected async exportCsv(): Promise<void> {
    const fyId = this.data()?.fyId;
    if (!fyId) {
      return;
    }
    this.exporting.set(true);
    this.exportError.set('');
    try {
      const blob = await this.entryApi.exportCsv({ ...this.filters(), fyId });
      const url = URL.createObjectURL(blob);
      const link = this.document.createElement('a');
      link.href = url;
      link.download = `nama-entries-${fyId}.csv`;
      link.click();
      setTimeout(() => URL.revokeObjectURL(url));
    } catch {
      this.exportError.set('The CSV file could not be made. Please try again.');
    } finally {
      this.exporting.set(false);
    }
  }

  // Only the latest answer is shown. Categories load once with the first list.
  private async load(): Promise<void> {
    const request = ++this.request;
    try {
      const [, categories, data] = await Promise.all([
        this.meService.load(),
        this.categories() ?? this.planApi.categories(),
        this.entryApi.entries(this.filters()),
      ]);
      if (request === this.request) {
        this.categories.set(categories);
        this.data.set(data);
        this.loadError.set('');
      }
    } catch {
      if (request === this.request) {
        this.loadError.set('The entries could not be loaded. Please reload the page.');
      }
    }
  }

  private navigate(filters: EntryFilters): void {
    const queryParams: Params = {};
    for (const key of FILTER_KEYS) {
      if (filters[key]) {
        queryParams[key] = filters[key];
      }
    }
    void this.router.navigate([], { relativeTo: this.route, queryParams, replaceUrl: true });
  }

  private categoryOf(subCategoryId: string | undefined): string | undefined {
    return subCategoryId
      ? this.categories()?.find((category) => category.subCategories.some((sub) => sub.id === subCategoryId))?.id
      : undefined;
  }

  private money(amount: number): string {
    return formatMoney(toMinor(amount), this.currency());
  }
}
