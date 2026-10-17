import { BreakpointObserver } from '@angular/cdk/layout';
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
import { debounceTime, map } from 'rxjs';
import { MeService } from '../../core/me';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { formatMoney, toMinor } from '../../shared/money';
import { formatDate, periodWithDates } from '../../shared/month-label';
import { Category, PlanApi } from '../plan/plan-api';
import { Entry, EntriesResponse, EntryApi, EntryFilters } from '../add-entry/entry-api';
import { NOTE_MAX_LENGTH } from '../add-entry/entry-fields';
import { EntryDialog, EntryDialogData, EntryDialogResult } from './entry-dialog';

const PHONE = '(max-width: 767.98px)';

// The filters live in the URL with the API parameter names, so Months can open Entries
// filtered by a sub-category and month (MON-04).
const FILTER_KEYS = ['fyId', 'periodKey', 'categoryId', 'subCategoryId', 'bankAccountId', 'q'] as const;
type FilterKey = (typeof FILTER_KEYS)[number];

// PRD 5.5 Entries (TBL-01 to TBL-06). Page load is 2 calls (NFR-01): categories, and the entries
// with the FYs, the accounts and the total. A filter change, a save or a delete is 1 call. The
// API sums the total (R-MON-01); the page only formats it.
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
  private readonly meService = inject(MeService);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(BreakpointObserver);
  private readonly document = inject(DOCUMENT);

  protected readonly noteMax = NOTE_MAX_LENGTH;
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
    return !!(filters.periodKey || filters.categoryId || filters.subCategoryId || filters.bankAccountId || filters.q?.trim());
  });

  // TBL-01: date, month, category, sub-category, amount, bank account, note. Names are looked up by id.
  protected readonly rows = computed(() => {
    const data = this.data();
    if (!data) {
      return [];
    }
    const categories = this.categories() ?? [];
    const categoryNames = new Map(categories.map((category) => [category.id, category.name]));
    const subNames = new Map(categories.flatMap((category) => category.subCategories).map((sub) => [sub.id, sub.name]));
    const accountNames = new Map(data.bankAccounts.map((account) => [account.id, account.name]));
    const months = new Map(this.fiscalYear()?.periods.map((period) => [period.key, period.label]) ?? []);
    return data.entries.map((entry) => {
      const sub = subNames.get(entry.subCategoryId) ?? '';
      const amount = this.money(entry.amount);
      const date = formatDate(entry.date);
      return {
        entry,
        date,
        month: months.get(entry.periodKey) ?? entry.periodKey,
        category: categoryNames.get(entry.categoryId) ?? '',
        sub,
        amount,
        account: accountNames.get(entry.bankAccountId) ?? '',
        editLabel: `Edit ${sub}, ${amount}, ${date}`,
      };
    });
  });

  // TBL-04: the total of the filtered rows, summed by the API.
  protected readonly summary = computed(() => {
    const data = this.data();
    if (!data) {
      return '';
    }
    const count = data.entries.length;
    return `${count} ${count === 1 ? 'entry' : 'entries'} · Total ${this.money(data.total)}`;
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

  // A new FY starts with all its months; a new category with all its sub-categories.
  protected setFilter(key: FilterKey, value: string): void {
    const next: EntryFilters = { ...this.filters(), [key]: value };
    if (key === 'fyId') {
      next.periodKey = '';
    }
    if (key === 'categoryId') {
      next.subCategoryId = '';
    }
    this.navigate(next);
  }

  protected clearFilters(): void {
    this.search.setValue('', { emitEvent: false });
    this.navigate({ fyId: this.filters().fyId });
  }

  // TBL-02, TBL-03: edit and delete in one dialog, full screen on phones (docs/DESIGN.md).
  protected edit(entry: Entry): void {
    const me = this.meService.me();
    const size = this.breakpoints.isMatched(PHONE)
      ? { width: '100vw', maxWidth: '100vw', height: '100dvh', panelClass: 'full-screen-dialog' }
      : { width: '640px', maxWidth: 'calc(100vw - 32px)' };
    const dialog = this.dialog.open<EntryDialog, EntryDialogData, EntryDialogResult>(EntryDialog, {
      ...size,
      data: {
        entry,
        categories: this.categories() ?? [],
        accounts: this.data()?.bankAccounts ?? [],
        currency: this.currency(),
        firstDate: me?.firstDate ?? null,
        lastDate: me?.lastDate ?? null,
      },
    });
    dialog.afterClosed().subscribe((result) => {
      if (result) {
        this.status.set(result === 'saved' ? 'Entry updated.' : 'Entry deleted.');
        void this.load();
      }
    });
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
