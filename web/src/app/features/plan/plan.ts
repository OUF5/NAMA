import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentType } from '@angular/cdk/portal';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { MeService } from '../../core/me';
import { dialogSize } from '../../shared/dialog';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { formatAmount, formatMoney, toMinor } from '../../shared/money';
import { PeriodInfo, formatDate, formatDay } from '../../shared/month-label';
import { MonthOption, MonthPicker } from '../../shared/month-picker';
import { Category, PlanApi, PlansResponse, SubCategory } from './plan-api';
import { PlanCategories, CategoriesChange } from './plan-categories';
import { PlanDialog, PlanDialogData } from './plan-dialog';

const PHONE = '(max-width: 767.98px)';

// Owner note 9: three tabs, in the URL as ?tab= so a link can open one. Owner note 25: Allocating
// first, and /plan with no tab opens it; links about editing amounts name ?tab=planning.
export const PLAN_TABS = [
  { id: 'allocating', label: 'Allocating' },
  { id: 'planning', label: 'Planning' },
  { id: 'categories', label: 'Categories' },
] as const;

export type PlanTab = (typeof PLAN_TABS)[number]['id'];

// PRD 5.3 Plan (PLN-01 to PLN-06), in three tabs (owner note 9): Allocating (the PLN-03 totals, the
// default, owner note 25), Planning (each sub-category's amount in one month, a month at a time) and Categories (STG-05, moved from
// Settings). Owner's choice (M8), so the page stays short as it grows:
// - PLN-03 totals are one table turned sideways that reads top to bottom: Income - Planned = Unassigned
//   this month (the month on its own), added up from the FY's first month = Accumulated Planned
//   Savings (R-PLN-03, owner note 10), for every month and the FY, whose last row is Planned saved
//   (R-PLN-04). A month can be selected.
// - Categories are compact rows (closed at first) with their amount in the selected month and their
//   FY total; a category opens its sub-categories. Allocating and Planning share the selected month.
// - The 12 month amounts are edited in a dialog (PlanDialog).
// All sums come from the API (R-PLN-02, R-PLN-03); the page only formats them.
@Component({
  selector: 'app-plan',
  imports: [
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatSelectModule,
    MonthPicker,
    PlanCategories,
  ],
  providers: [OUTLINE_FORM_FIELDS],
  templateUrl: './plan.html',
  styleUrl: './plan.scss',
})
export class Plan {
  private readonly api = inject(PlanApi);
  private readonly meService = inject(MeService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(BreakpointObserver);

  protected readonly formatDate = formatDate;
  protected readonly tabs = PLAN_TABS;
  protected readonly tab = signal<PlanTab>('allocating');
  protected readonly formatDay = formatDay;

  protected readonly loadError = signal('');
  protected readonly data = signal<PlansResponse | null>(null);
  protected readonly categories = signal<Category[]>([]);
  protected readonly status = signal('');

  protected readonly currency = computed(() => this.meService.me()?.profile?.currency ?? 'SAR');
  protected readonly periods = computed<PeriodInfo[]>(() => this.data()?.fiscalYear.periods ?? []);

  // The selected month column: the current month by default, else the FY's first month.
  private readonly selectedKey = signal<string | null>(null);
  protected readonly selected = computed(
    () => this.periods().find((period) => period.key === this.selectedKey()) ?? this.periods()[0] ?? null,
  );
  protected readonly monthOptions = computed<MonthOption[]>(() => {
    const current = this.meService.me()?.currentPeriodKey;
    return this.periods().map((period) => ({
      key: period.key,
      label: period.label,
      hint: period.key === current ? 'This month' : undefined,
    }));
  });

  // PLN-03, column by column. Unassigned is green at 0 or more and red below 0 (R-PLN-03).
  protected readonly columns = computed(() => {
    const totals = new Map((this.data()?.totals ?? []).map((row) => [row.periodKey, row]));
    return this.periods().map((period) => {
      const row = totals.get(period.key);
      return {
        period,
        month: period.label.slice(0, 3),
        year: period.label.slice(4),
        planned: formatAmount(toMinor(row?.planned ?? 0)),
        income: formatAmount(toMinor(row?.income ?? 0)),
        // Marks a month with additional income, as Tracker's Totals does; "–" without.
        additionalIncome: this.nonZero(row?.additionalIncome ?? 0),
        thisMonth: formatAmount(toMinor(row?.thisMonth ?? 0)),
        thisMonthOver: (row?.thisMonth ?? 0) < 0,
        unassigned: formatAmount(toMinor(row?.unassigned ?? 0)),
        over: (row?.unassigned ?? 0) < 0,
      };
    });
  });

  // Owner note 10: Accumulated Planned Savings adds up from the FY's first month ("Aug 2026").
  protected readonly firstMonth = computed(() => this.periods()[0]?.label ?? '');

  // R-PLN-04: what the plan leaves at the end of the FY, in the FY column and the card head.
  protected readonly fiscalYearColumn = computed(() => {
    const fy = this.data()?.fiscalYearTotals;
    return fy
      ? {
          income: formatAmount(toMinor(fy.income)),
          additionalIncome: this.nonZero(fy.additionalIncome),
          planned: formatAmount(toMinor(fy.planned)),
          plannedSaved: formatAmount(toMinor(fy.plannedSaved)),
          plannedSavedMoney: formatMoney(toMinor(fy.plannedSaved), this.currency()),
          over: fy.plannedSaved < 0,
        }
      : null;
  });

  // The totals table: the row labels, then equal month columns (scrolls sideways below that width).
  protected readonly labelWidth = 168;
  protected readonly columnWidth = 72;

  private readonly opened = signal<ReadonlySet<string>>(new Set());
  // The sub-category saved last, highlighted for a moment.
  protected readonly flashId = signal<string | null>(null);

  // Archived items are hidden (PRD 4.6), except a sub-category with an amount in this FY: it still
  // counts in the totals, so it is shown marked "(archived)" and can be cleared (owner's choice, M8).
  // Archived categories get no "Add sub-category".
  protected readonly rows = computed(() => {
    const data = this.data();
    const key = this.selected()?.key ?? '';
    const plans = new Map((data?.plans ?? []).map((plan) => [plan.subCategoryId, plan]));
    const planned = new Set(
      (data?.plans ?? [])
        .filter((plan) => Object.values(plan.amounts).some((amount) => amount > 0))
        .map((plan) => plan.subCategoryId),
    );
    return this.categories()
      .map((category) => ({
        ...category,
        month: data?.categoryMonthTotals[category.id]?.[key] ?? 0,
        total: data?.categoryTotals[category.id] ?? 0,
        subs: category.subCategories
          .filter((sub) => (!sub.archived && !category.archived) || planned.has(sub.id))
          .map((sub) => ({
            ...sub,
            hidden: sub.archived || category.archived,
            month: plans.get(sub.id)?.amounts[key] ?? 0,
            total: plans.get(sub.id)?.total ?? 0,
          })),
      }))
      .filter((category) => !category.archived || category.subs.length > 0);
  });

  // PLN-06
  protected readonly hasSubCategories = computed(() =>
    this.rows().some((category) => category.subs.some((sub) => !sub.hidden)),
  );

  private urlFyId: string | null = null;
  private request = 0;

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const tab = params.get('tab');
      this.tab.set(PLAN_TABS.some((item) => item.id === tab) ? (tab as PlanTab) : 'allocating');
      // Switching tabs keeps the FY and needs no call.
      if (this.data() && params.get('fyId') === this.urlFyId) {
        return;
      }
      this.urlFyId = params.get('fyId');
      void this.load();
    });
  }

  // "SAR 1,240"; nothing planned shows "–".
  protected money(amount: number): string {
    return amount ? formatMoney(toMinor(amount), this.currency()) : '–';
  }

  // "2,000", or null for the template's "–".
  private nonZero(amount: number): string | null {
    return toMinor(amount) === 0 ? null : formatAmount(toMinor(amount));
  }

  // The selected FY lives in the URL, as on Months and Entries (owner's choice, M8); the tab stays.
  protected setFiscalYear(fyId: string): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { fyId },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  protected selectMonth(key: string): void {
    this.selectedKey.set(key);
  }

  protected isOpen(categoryId: string): boolean {
    return this.opened().has(categoryId);
  }

  protected toggle(categoryId: string): void {
    this.opened.update((ids) => {
      const next = new Set(ids);
      if (!next.delete(categoryId)) {
        next.add(categoryId);
      }
      return next;
    });
  }

  // PLN-01, PLN-04: the month amounts of one sub-category, in a dialog.
  protected async edit(sub: SubCategory, categoryName: string): Promise<void> {
    const plans = this.data();
    if (!plans) {
      return;
    }
    const saved = await this.open<PlanDialog, PlanDialogData, PlansResponse>(PlanDialog, {
      sub,
      categoryName,
      plans,
      currency: this.currency(),
    });
    if (saved) {
      this.data.set(saved);
      this.status.set(`Saved ${sub.name}.`);
      this.flash(sub.id);
    }
  }

  // The Categories tab changed the list (STG-05, PLN-05).
  protected categoriesChanged(change: CategoriesChange): void {
    this.categories.set(change.categories);
    this.status.set(change.message);
  }

  // PLN-01: a new sub-category goes straight to its month amounts.
  protected async subAdded(sub: SubCategory, categoryName: string): Promise<void> {
    this.opened.update((ids) => new Set(ids).add(sub.categoryId));
    await this.edit(sub, categoryName);
  }

  // The FY in the URL, else the one that contains today, else the latest one.
  private async load(): Promise<void> {
    const request = ++this.request;
    try {
      const me = await this.meService.load();
      const fyId = this.urlFyId || me.currentFyId || (await this.api.fiscalYears()).at(-1)?.id;
      if (!fyId) {
        this.loadError.set('There is no fiscal year yet.');
        return;
      }
      const [categories, data] = await Promise.all([this.api.categories(), this.api.plans(fyId)]);
      if (request !== this.request) {
        return;
      }
      this.categories.set(categories);
      this.data.set(data);
      this.selectedKey.set(me.currentPeriodKey);
      this.loadError.set('');
    } catch {
      if (request === this.request) {
        this.loadError.set('Plan could not be loaded. Please reload the page.');
      }
    }
  }

  private flash(subId: string): void {
    this.flashId.set(subId);
    setTimeout(() => this.flashId.update((id) => (id === subId ? null : id)), 2000);
  }

  // One dialog at a time (UX-04); full screen on phones.
  private open<C, D, R>(component: ComponentType<C>, data: D, width?: string): Promise<R | undefined> {
    const ref = this.dialog.open<C, D, R>(component, { ...dialogSize(this.breakpoints.isMatched(PHONE), width), data });
    return firstValueFrom(ref.afterClosed());
  }
}
