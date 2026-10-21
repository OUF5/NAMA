import { BreakpointObserver } from '@angular/cdk/layout';
import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MeService } from '../../core/me';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { toMinor } from '../../shared/money';
import { formatDate } from '../../shared/month-label';
import { MonthOption, MonthPicker } from '../../shared/month-picker';
import { RolloverBanner } from '../../shared/rollover-banner';
import { Entry } from '../add-entry/entry-api';
import { openEntryDialog } from '../entries/entry-dialog';
import { PlanApi } from '../plan/plan-api';
import { MonthApi } from './month-api';
import { MonthDetail, MonthDetailState } from './month-detail';
import { STATUS_LABELS, SpendLine, availableMinor, nameMaps, spendLine } from './month-lines';
import { BalancesResponse, Cell, CategoryRow, OverviewApi, OverviewResponse } from './overview-api';
import { MonthsView, loadView, saveView } from './view-choice';
import { YearGrid } from './year-grid';

const PHONE = '(max-width: 767.98px)';

// PRD 5.6 Months (MON-01 to MON-04), Tracker on screen. Owner note 9: the Months view shows one month
// at a time (the month in progress first, or ?month=), in detail (MonthDetail). Page load is the
// overview plus that month's detail (NFR-01: 2 calls); picking another month makes only its detail
// call. The Year view makes the overview and the balances call instead. The API makes every sum
// (R-MON-01); the page only formats.
@Component({
  selector: 'app-months',
  imports: [
    NgTemplateOutlet,
    RouterLink,
    MatButtonModule,
    MatButtonToggleModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatSelectModule,
    MonthDetail,
    MonthPicker,
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
  private readonly monthApi = inject(MonthApi);
  private readonly meService = inject(MeService);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(BreakpointObserver);

  protected readonly formatDate = formatDate;

  protected readonly loadError = signal('');
  protected readonly data = signal<OverviewResponse | null>(null);
  protected readonly status = signal('');
  // MON-02: the last choice is remembered (Months by default).
  protected readonly view = signal<MonthsView>(loadView());
  // Owner note 9: the month in the URL (?month=YYYY-MM), and its detail call.
  private readonly urlMonth = signal<string | null>(null);
  protected readonly monthDetail = signal<MonthDetailState | null>(null);
  // Owner note 9: the Year view's Balance row (null while it loads).
  protected readonly balances = signal<BalancesResponse | 'error' | null>(null);

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

  // The month shown: the one in the URL, else the month in progress, else the FY's first month.
  protected readonly monthKey = computed(() => {
    const months = this.data()?.months ?? [];
    return (
      months.find((month) => month.period.key === this.urlMonth())?.period.key ??
      months.find((month) => month.status === 'inProgress')?.period.key ??
      months[0]?.period.key ??
      null
    );
  });

  // The month picker: each month with its status (MON-01).
  protected readonly monthOptions = computed<MonthOption[]>(() =>
    (this.data()?.months ?? []).map((month) => ({
      key: month.period.key,
      label: month.period.label,
      hint: STATUS_LABELS[month.status],
    })),
  );

  // "See all" opens Entries with the same month and filter.
  protected readonly entriesQuery = computed<Record<string, string>>(() =>
    Object.fromEntries(
      Object.entries({
        fyId: this.data()?.fyId ?? '',
        periodKey: this.monthKey() ?? '',
        categoryId: this.categoryId(),
        subCategoryId: this.subCategoryId(),
      }).filter(([, value]) => !!value),
    ),
  );

  protected readonly today = computed(() => this.meService.me()?.today ?? null);

  private urlFyId: string | null = null;
  private request = 0;
  private monthRequest = 0;
  // What the overview was loaded for: a change of month alone needs only the month's detail.
  private loadedFor: string | null = null;

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      this.urlFyId = params.get('fyId');
      this.categoryId.set(params.get('categoryId') ?? '');
      this.subCategoryId.set(params.get('subCategoryId') ?? '');
      this.overOnly.set(params.get('over') === '1');
      this.urlMonth.set(params.get('month'));
      // Plan's "See the year grid" link (M8) opens the Year view.
      const view = params.get('view');
      if (view === 'months' || view === 'year') {
        this.setView(view);
      }
      const scope = [this.urlFyId, this.categoryId(), this.subCategoryId()].join('|');
      if (this.data() && scope === this.loadedFor) {
        void this.loadMonth();
      } else {
        void this.load();
      }
    });
  }

  protected setView(view: MonthsView): void {
    this.view.set(view);
    saveView(view);
    if (view === 'months' && this.data() && !this.monthDetail()) {
      void this.loadMonth();
    }
    if (view === 'year' && this.data() && !this.balances()) {
      void this.loadBalances();
    }
  }

  // Owner note 9: the month lives in the URL, so a reload or a shared link keeps it.
  protected setMonth(key: string): void {
    this.navigate({ month: key });
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

  protected retryMonth(): void {
    void this.loadMonth();
  }

  // TBL-02, TBL-03, ENT-04: the Entries edit dialog. A save or delete can change any month, so the
  // overview and the month are loaded again.
  protected edit(entry: Entry): void {
    const me = this.meService.me();
    const dialog = openEntryDialog(this.dialog, this.breakpoints.isMatched(PHONE), {
      entry,
      categories: this.data()?.categories ?? [],
      accounts: this.monthDetail()?.data?.bankAccounts ?? [],
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

  // FY-01, FY-02: the new FY exists, so the banner goes and the months are loaded again. Without an
  // FY in the URL the page follows the new /api/me (the FY that now contains today).
  protected rolledOver(label: string): void {
    this.status.set(`${label} started.`);
    void this.load();
  }

  // The overview, then the shown month's detail (Months view only).
  private async load(): Promise<void> {
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
      this.loadedFor = [this.urlFyId, this.categoryId(), this.subCategoryId()].join('|');
      this.loadError.set('');
      // A link with only a sub-category (the API picks its category) shows the category too.
      if (!this.categoryId() && this.subCategoryId()) {
        const category = data.categories.find((item) => item.subCategories.some((sub) => sub.id === this.subCategoryId()));
        this.categoryId.set(category?.id ?? '');
      }
      this.monthDetail.set(null);
      this.balances.set(null);
      if (this.view() === 'months') {
        void this.loadMonth();
      } else {
        void this.loadBalances();
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

  // GET .../months/{periodKey} for the shown month, with Tracker's filter. Only the latest answer, for
  // the month still shown, is kept.
  private async loadMonth(): Promise<void> {
    const fyId = this.data()?.fyId;
    const key = this.monthKey();
    if (!fyId || !key) {
      return;
    }
    const request = ++this.monthRequest;
    this.monthDetail.update((detail) => ({ state: 'loading', data: detail?.data?.period.key === key ? detail.data : undefined }));
    let next: MonthDetailState;
    try {
      next = {
        state: 'ok',
        data: await this.monthApi.month(fyId, key, { categoryId: this.categoryId(), subCategoryId: this.subCategoryId() }),
      };
    } catch {
      next = { state: 'error' };
    }
    if (request === this.monthRequest) {
      this.monthDetail.set(next);
    }
  }

  // The Year view's Balance row for the FY shown; only the latest answer is kept.
  private async loadBalances(): Promise<void> {
    const fyId = this.data()?.fyId;
    if (!fyId) {
      return;
    }
    const request = this.request;
    let next: BalancesResponse | 'error';
    try {
      next = await this.overviewApi.balances(fyId);
    } catch {
      next = 'error';
    }
    if (request === this.request) {
      this.balances.set(next);
    }
  }

  private navigate(changes: Record<string, string | null>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: changes,
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
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
}
