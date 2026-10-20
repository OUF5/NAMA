import { BreakpointObserver } from '@angular/cdk/layout';
import { DOCUMENT, NgTemplateOutlet } from '@angular/common';
import { Component, Injector, afterNextRender, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MeService } from '../../core/me';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { formatMoney, toMinor } from '../../shared/money';
import { formatDate, formatDay } from '../../shared/month-label';
import { MonthSummary } from '../../shared/month-summary';
import { RolloverBanner } from '../../shared/rollover-banner';
import { EntriesResponse, Entry, EntryApi } from '../add-entry/entry-api';
import { openEntryDialog } from '../entries/entry-dialog';
import { PlanApi } from '../plan/plan-api';
import { STATUS_LABELS, SpendLine, availableMinor, nameMaps, spendLine } from './month-lines';
import { Cell, CategoryRow, MonthOverview, OverviewApi, OverviewResponse } from './overview-api';
import { MonthsView, loadView, saveView } from './view-choice';
import { YearGrid } from './year-grid';

const PHONE = '(max-width: 767.98px)';

// The entries of one expanded month (MON-02), kept while the page is open. data stays while a
// reload runs, so the list does not flicker.
interface MonthEntries {
  state: 'loading' | 'error' | 'ok';
  data?: EntriesResponse;
}

// PRD 5.6 Months (MON-01 to MON-04). Page load is the overview, plus the entries of the in-progress
// month, which opens expanded (NFR-01). The API makes every sum (R-MON-01); the page only formats.
@Component({
  selector: 'app-months',
  imports: [
    NgTemplateOutlet,
    RouterLink,
    MatButtonModule,
    MatButtonToggleModule,
    MatCheckboxModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatIconModule,
    MatProgressBarModule,
    MatSelectModule,
    MonthSummary,
    RolloverBanner,
    YearGrid,
  ],
  providers: [OUTLINE_FORM_FIELDS],
  templateUrl: './months.html',
  styleUrl: './months.scss',
})
export class Months {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly overviewApi = inject(OverviewApi);
  private readonly planApi = inject(PlanApi);
  private readonly entryApi = inject(EntryApi);
  private readonly meService = inject(MeService);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(BreakpointObserver);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);

  protected readonly formatDate = formatDate;
  // A month card lists only its newest entries (owner's choice, M7).
  protected readonly entryLimit = 5;
  protected readonly statusLabels = STATUS_LABELS;

  protected readonly loadError = signal('');
  protected readonly data = signal<OverviewResponse | null>(null);
  protected readonly status = signal('');
  // MON-02: the last choice is remembered (Months by default).
  protected readonly view = signal<MonthsView>(loadView());
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());
  protected readonly monthEntries = signal<Record<string, MonthEntries>>({});

  protected readonly currency = computed(() => this.meService.me()?.profile?.currency ?? 'SAR');
  protected readonly fiscalYear = computed(() => {
    const data = this.data();
    return data?.fiscalYears.find((fiscalYear) => fiscalYear.id === data.fyId) ?? null;
  });

  private readonly names = computed(() => nameMaps(this.data()?.categories ?? []));

  // R-PLN-06: "Oct 2026" when the carry starts after the FY's first month; null otherwise.
  protected readonly carryFrom = computed(() => {
    const data = this.data();
    const key = data?.carryFromPeriodKey;
    return data && key && key !== data.months[0]?.period.key
      ? (data.months.find((month) => month.period.key === key)?.period.label ?? null)
      : null;
  });

  // Tracker's filter (owner's choice, 20 Oct 2026), in the URL like Entries. The API narrows the
  // rows and sums to the category or sub-category; "Over plan only" hides the lines that are not
  // over plan, with no sums of its own.
  protected readonly categoryId = signal('');
  protected readonly subCategoryId = signal('');
  protected readonly overOnly = signal(false);
  protected readonly filtered = computed(() => !!this.categoryId() || this.overOnly());
  protected readonly subCategoryOptions = computed(
    () => this.data()?.categories.find((category) => category.id === this.categoryId())?.subCategories ?? [],
  );
  // "Food", "Food · Groceries", or the FY label, for the empty states.
  protected readonly scopeLabel = computed(() => {
    const names = this.names();
    const category = names.categories.get(this.categoryId());
    const sub = names.subs.get(this.subCategoryId());
    return [category, sub].filter(Boolean).join(' · ');
  });

  // The Year grid's rows: with "Over plan only", the sub-categories over plan in at least one month.
  protected readonly gridData = computed(() => {
    const data = this.data();
    if (!data || !this.overOnly()) {
      return data;
    }
    return { ...data, rows: this.overRows(data.rows) };
  });

  // MON-02 Months view: one card per month, and the categories and sub-categories with a plan or
  // spending in that month (owner's choice, M7).
  protected readonly cards = computed(() => {
    const data = this.data();
    if (!data) {
      return [];
    }
    const names = this.names();
    return data.months.map((month, i) => {
      const categories = data.rows
        .map((row) => ({
          id: row.categoryId,
          name: names.categories.get(row.categoryId) ?? '',
          line: this.line(row.months[i]),
          subs: row.subCategories
            .filter((sub) => (this.overOnly() ? this.isOver(sub.months[i]) : this.hasMoney(sub.months[i])))
            .map((sub) => ({
              id: sub.subCategoryId,
              name: names.subs.get(sub.subCategoryId) ?? '',
              line: this.line(sub.months[i]),
            })),
        }))
        .filter((category) => category.subs.length > 0);
      return {
        month,
        key: month.period.key,
        dates: `${formatDay(month.period.startDate)} to ${formatDay(month.period.endDate)}`,
        categories,
      };
    });
  });

  // The rows of the expanded months' entry lists, with names looked up by id.
  protected readonly entryRows = computed(() => {
    const names = this.names();
    const rows: Record<
      string,
      { entry: Entry; day: string; sub: string; amount: string; editLabel: string }[]
    > = {};
    for (const [key, month] of Object.entries(this.monthEntries())) {
      rows[key] = (month.data?.entries ?? []).map((entry) => {
        const sub = names.subs.get(entry.subCategoryId) ?? '';
        const amount = this.money(toMinor(entry.amount));
        return {
          entry,
          day: formatDay(entry.date),
          sub,
          amount,
          editLabel: `Edit ${sub}, ${amount}, ${formatDate(entry.date)}`,
        };
      });
    }
    return rows;
  });

  private urlFyId: string | null = null;
  private request = 0;
  private readonly entriesRequests = new Map<string, number>();

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      this.urlFyId = params.get('fyId');
      this.categoryId.set(params.get('categoryId') ?? '');
      this.subCategoryId.set(params.get('subCategoryId') ?? '');
      this.overOnly.set(params.get('over') === '1');
      // Plan's "See the year grid" link (M8) opens the Year view.
      const view = params.get('view');
      if (view === 'months' || view === 'year') {
        this.setView(view);
      }
      void this.load(true);
    });
  }

  protected setView(view: MonthsView): void {
    this.view.set(view);
    saveView(view);
  }

  // MON-03: the selected FY lives in the URL, so a reload keeps it. The filters stay.
  protected setFiscalYear(fyId: string): void {
    this.navigate({ fyId });
  }

  // A new category clears the sub-category, as on Entries.
  protected setCategory(categoryId: string): void {
    this.navigate({ categoryId: categoryId || null, subCategoryId: null });
  }

  protected setSubCategory(subCategoryId: string): void {
    this.navigate({ subCategoryId: subCategoryId || null });
  }

  protected setOverOnly(on: boolean): void {
    this.navigate({ over: on ? '1' : null });
  }

  protected clearFilters(): void {
    this.navigate({ categoryId: null, subCategoryId: null, over: null });
  }

  // "See all" opens Entries with the same month and filter.
  protected entriesLink(fyId: string, periodKey: string): Record<string, string> {
    return Object.fromEntries(
      Object.entries({ fyId, periodKey, categoryId: this.categoryId(), subCategoryId: this.subCategoryId() }).filter(
        ([, value]) => !!value,
      ),
    );
  }

  protected openMonth(key: string): void {
    if (!this.expanded().has(key)) {
      this.expanded.update((keys) => new Set(keys).add(key));
    }
    if (!this.monthEntries()[key]) {
      void this.loadEntries(key);
    }
  }

  protected closeMonth(key: string): void {
    this.expanded.update((keys) => {
      const next = new Set(keys);
      next.delete(key);
      return next;
    });
  }

  protected retryEntries(key: string): void {
    void this.loadEntries(key);
  }

  // TBL-02, TBL-03, ENT-04: the Entries edit dialog. A save or delete can change any month, so the
  // overview and every open month's entries are loaded again.
  protected edit(entry: Entry, key: string): void {
    const me = this.meService.me();
    const dialog = openEntryDialog(this.dialog, this.breakpoints.isMatched(PHONE), {
      entry,
      categories: this.data()?.categories ?? [],
      accounts: this.monthEntries()[key]?.data?.bankAccounts ?? [],
      currency: this.currency(),
      firstDate: me?.firstDate ?? null,
      lastDate: me?.lastDate ?? null,
    });
    dialog.afterClosed().subscribe((result) => {
      if (result) {
        this.status.set(result === 'saved' ? 'Entry updated.' : 'Entry deleted.');
        void this.load(false);
      }
    });
  }

  // FY-01, FY-02: the new FY exists, so the banner goes and the months are loaded again. Without an
  // FY in the URL the page follows the new /api/me (the FY that now contains today).
  protected rolledOver(label: string): void {
    this.status.set(`${label} started.`);
    void this.load(true);
  }

  // A new FY (fresh = true) starts with only the in-progress month open, scrolled into view
  // (MON-02). Otherwise the open months stay open and their entries are loaded again.
  private async load(fresh: boolean): Promise<void> {
    const request = ++this.request;
    try {
      const me = await this.meService.load();
      const fyId = this.urlFyId || me.currentFyId || (await this.planApi.fiscalYears()).at(-1)?.id;
      if (!fyId) {
        this.loadError.set('There is no fiscal year yet.');
        return;
      }
      const data = await this.overviewApi.overview(fyId, {
        categoryId: this.categoryId(),
        subCategoryId: this.subCategoryId(),
      });
      if (request !== this.request) {
        return;
      }
      this.data.set(data);
      this.loadError.set('');
      // A link with only a sub-category (the API picks its category) shows the category too.
      if (!this.categoryId() && this.subCategoryId()) {
        const category = data.categories.find((item) => item.subCategories.some((sub) => sub.id === this.subCategoryId()));
        this.categoryId.set(category?.id ?? '');
      }
      if (fresh) {
        const current = data.months.find((month) => month.status === 'inProgress');
        this.expanded.set(new Set(current ? [current.period.key] : []));
        this.monthEntries.set({});
        if (current && this.view() === 'months') {
          this.scrollTo(current);
        }
      } else {
        this.monthEntries.update((months) =>
          Object.fromEntries(Object.entries(months).filter(([key]) => this.expanded().has(key))),
        );
      }
      // In the Year view the cards are not shown; an open card loads its entries when it appears.
      if (this.view() === 'months') {
        for (const key of this.expanded()) {
          void this.loadEntries(key);
        }
      }
    } catch {
      if (request === this.request) {
        this.loadError.set(
          this.categoryId() || this.subCategoryId()
            ? 'This category could not be loaded. Clear the filters and try again.'
            : 'The months could not be loaded. Please reload the page.',
        );
      }
    }
  }

  // GET /api/entries for one month (M6), which also brings the accounts. Only the latest answer
  // for a month, in the FY still shown, is kept.
  private async loadEntries(key: string): Promise<void> {
    const fyId = this.data()?.fyId;
    if (!fyId) {
      return;
    }
    const request = (this.entriesRequests.get(key) ?? 0) + 1;
    this.entriesRequests.set(key, request);
    this.monthEntries.update((months) => ({
      ...months,
      [key]: { state: 'loading', data: months[key]?.data },
    }));
    let next: MonthEntries;
    try {
      next = {
        state: 'ok',
        data: await this.entryApi.entries({
          fyId,
          periodKey: key,
          categoryId: this.categoryId(),
          subCategoryId: this.subCategoryId(),
        }),
      };
    } catch {
      next = { state: 'error' };
    }
    if (this.entriesRequests.get(key) === request && this.data()?.fyId === fyId) {
      this.monthEntries.update((months) => ({ ...months, [key]: next }));
    }
  }

  private scrollTo(month: MonthOverview): void {
    afterNextRender(
      () =>
        this.document
          .getElementById(`month-${month.period.key}`)
          ?.scrollIntoView?.({ block: 'start' }),
      { injector: this.injector },
    );
  }

  private navigate(changes: Record<string, string | null>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: changes,
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  // A plan, spending, or money (or an overspend) carried in from last month.
  private hasMoney(cell: Cell): boolean {
    return cell.planned > 0 || cell.spent > 0 || cell.carriedIn !== 0;
  }

  // The "Over by" status of docs/DESIGN.md: spent above what was available (R-PLN-05); spending
  // without a plan counts.
  private isOver(cell: Cell): boolean {
    return this.line(cell).status === 'danger';
  }

  private overRows(rows: CategoryRow[]): CategoryRow[] {
    return rows
      .map((row) => ({ ...row, subCategories: row.subCategories.filter((sub) => sub.months.some((cell) => this.isOver(cell))) }))
      .filter((row) => row.subCategories.length > 0);
  }

  // docs/DESIGN.md category rows: name and chip, progress bar, "SAR 1,240 of 2,000" (available).
  private line(cell: Cell): SpendLine {
    return spendLine(availableMinor(cell), toMinor(cell.spent), this.currency());
  }

  private money(minor: number): string {
    return formatMoney(minor, this.currency());
  }
}
