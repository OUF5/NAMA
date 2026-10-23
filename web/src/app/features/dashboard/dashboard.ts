import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { MeService } from '../../core/me';
import { SummaryService } from '../../core/summary';
import { formatMoney, toMinor } from '../../shared/money';
import { formatDate, formatDay } from '../../shared/month-label';
import { RolloverBanner } from '../../shared/rollover-banner';
import { STATUS_LABELS, availableMinor, nameMaps, spendLine } from '../months/month-lines';
import { MonthOverview, OverviewApi, OverviewResponse } from '../months/overview-api';
import { LoanTotals, LoansApi } from '../loans/loans-api';
import { PlanApi } from '../plan/plan-api';
import { BankBar } from './bank-bar';
import { MonthBars } from './month-bars';
import { Projection } from './projection';

// PRD 5.9 Dashboard (DSH-01 to DSH-06) and the FY-01 banner (owner note 8). It shows the FY that
// contains today, else the latest; there is no FY selector. One call: the overview (NFR-01), plus
// GET /api/fiscal-years only when no FY contains today. The projection, today's day of the month and
// the bank balances come from the top bar's summary (UX-08), so they cost no call. The API makes every
// sum (R-MON-01); this page picks, ranks and formats.
@Component({
  selector: 'app-dashboard',
  imports: [
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatProgressBarModule,
    MatSelectModule,
    BankBar,
    MonthBars,
    Projection,
    RolloverBanner,
  ],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly overviewApi = inject(OverviewApi);
  private readonly planApi = inject(PlanApi);
  private readonly meService = inject(MeService);
  private readonly summaryService = inject(SummaryService);
  private readonly loansApi = inject(LoansApi);

  protected readonly formatDate = formatDate;
  protected readonly inProgressLabel = STATUS_LABELS.inProgress;

  protected readonly loadError = signal('');
  protected readonly data = signal<OverviewResponse | null>(null);
  protected readonly status = signal('');
  // DSH-03: the chart's category, '' for all. Not kept in the URL.
  protected readonly categoryId = signal('');
  protected readonly savingHow = signal(false);

  protected readonly currency = computed(() => this.meService.me()?.profile?.currency ?? 'SAR');
  protected readonly fiscalYear = computed(() => {
    const data = this.data();
    return data?.fiscalYears.find((fiscalYear) => fiscalYear.id === data.fyId) ?? null;
  });

  // DSH-03: only categories with a plan or spending in the FY have a row, so only they are offered.
  protected readonly chartCategories = computed(() => {
    const data = this.data();
    if (!data) {
      return [];
    }
    const names = nameMaps(data.categories).categories;
    return data.rows.map((row) => ({ id: row.categoryId, name: names.get(row.categoryId) ?? '' }));
  });

  // The whole budget, or the picked category's cells (the API's sums) in the months' shape.
  protected readonly chartMonths = computed((): MonthOverview[] => {
    const data = this.data();
    if (!data) {
      return [];
    }
    const row = data.rows.find((item) => item.categoryId === this.categoryId());
    if (!row) {
      return data.months;
    }
    const cells = new Map(row.months.map((cell) => [cell.periodKey, cell]));
    return data.months.map((month) => {
      const cell = cells.get(month.period.key);
      return {
        ...month,
        income: null,
        carriedIn: cell?.carriedIn ?? 0,
        planned: cell?.planned ?? 0,
        spent: cell?.spent ?? 0,
        remaining: cell?.remaining ?? 0,
      };
    });
  });

  // DSH-01: the month in progress, its status line and today's marker (from the summary, when it is
  // the same month).
  protected readonly current = computed(() => {
    const month = this.data()?.months.find((m) => m.status === 'inProgress');
    if (!month) {
      return null;
    }
    const line = spendLine(availableMinor(month), toMinor(month.spent), this.currency());
    const today = this.summaryService.summary()?.month;
    const day = today && today.period.key === month.period.key ? today : null;
    return {
      month,
      line,
      dates: `${formatDay(month.period.startDate)} to ${formatDay(month.period.endDate)}`,
      remaining: this.money(month.remaining),
      kpis: [
        { label: 'From last month', value: this.money(month.carriedIn) },
        { label: 'Planned', value: this.money(month.planned) },
        { label: 'Spent', value: this.money(month.spent) },
        ...(month.income === null ? [] : [{ label: 'Income', value: this.money(month.income) }]),
      ],
      day: day
        ? {
            marker: (day.day * 100) / day.days,
            text: `Day ${day.day} of ${day.days} · ${day.daysLeft} ${day.daysLeft === 1 ? 'day' : 'days'} left`,
          }
        : null,
    };
  });

  // DSH-01: the 3 sub-categories nearest to or over their plan this month (Near limit or Over),
  // over plan first, then by the share of the available amount spent.
  protected readonly watch = computed(() => {
    const data = this.data();
    const key = this.current()?.month.period.key;
    if (!data || !key) {
      return [];
    }
    const names = nameMaps(data.categories).subs;
    return data.rows
      .flatMap((row) =>
        row.subCategories.map((sub) => {
          const cell = sub.months.find((item) => item.periodKey === key);
          const available = cell ? availableMinor(cell) : 0;
          const spent = cell ? toMinor(cell.spent) : 0;
          return { categoryId: row.categoryId, subCategoryId: sub.subCategoryId, available, spent };
        }),
      )
      .map((item) => ({ ...item, line: spendLine(item.available, item.spent, this.currency()) }))
      .filter((item) => item.line.status === 'warn' || item.line.status === 'danger')
      .sort((a, b) => {
        if (a.line.status !== b.line.status) {
          return a.line.status === 'danger' ? -1 : 1;
        }
        const share = (item: typeof a) => (item.available > 0 ? item.spent / item.available : Infinity);
        return share(b) - share(a);
      })
      .slice(0, 3)
      .map((item) => ({
        id: item.subCategoryId,
        name: names.get(item.subCategoryId) ?? '',
        spent: formatMoney(item.spent, this.currency()),
        of: formatMoney(item.available, this.currency()),
        line: item.line,
        query: { fyId: data.fyId, categoryId: item.categoryId, subCategoryId: item.subCategoryId },
      }));
  });

  // When no month is in progress: the FY ended (the FY-01 banner is above), or it has not started.
  protected readonly noMonthText = computed(() => {
    const data = this.data();
    const fiscalYear = this.fiscalYear();
    const today = this.meService.me()?.today;
    if (!data || !fiscalYear) {
      return '';
    }
    if (today && today > fiscalYear.endDate) {
      const start = data.rollover ? ` Start ${data.rollover.nextLabel} above to keep adding entries.` : '';
      return `${fiscalYear.label} ended on ${formatDate(fiscalYear.endDate)}.${start}`;
    }
    return `${fiscalYear.label} starts on ${formatDate(fiscalYear.startDate)}.`;
  });

  // DSH-02 "to date" (owner's choice, M9): the months that started (Closed and In progress), from the
  // first month that carries (R-PLN-06: months before NAMA was set up were never tracked).
  protected readonly toDateRange = computed(() => {
    const data = this.data();
    const months = data?.months ?? [];
    const from = Math.max(0, months.findIndex((m) => m.period.key === data?.carryFromPeriodKey));
    const started = months.slice(from).filter((m) => m.status !== 'yetToCome');
    if (started.length === 0) {
      return 'No month has started yet';
    }
    const range =
      started.length === 1
        ? started[0].period.label
        : `${started[0].period.label} to ${started.at(-1)!.period.label}`;
    // Owner note 11: "1 of 12 months" read as if Oct were the FY's first month.
    return from > 0 ? `${range} · since you started NAMA` : `${range} · ${started.length} of ${months.length} months`;
  });

  protected readonly toDate = computed(() => {
    const toDate = this.data()?.toDate;
    if (!toDate) {
      return null;
    }
    const currency = this.currency();
    const under = toMinor(toDate.planned) - toMinor(toDate.spent);
    // Owner note 11: while a month is in progress, its unspent plan is still to spend (leftovers carry,
    // R-PLN-05), not under plan or saved ahead. It is DSH-06's "Still to spend this month".
    const current = (this.data()?.months ?? []).find((m) => m.status === 'inProgress');
    const left = current ? Math.max(0, under) : 0;
    const ahead = toMinor(toDate.saved) - toMinor(toDate.plannedSaved) - left;
    return {
      income: this.money(toDate.income),
      planned: this.money(toDate.planned),
      spent: this.money(toDate.spent),
      // Owner note 12: income minus spent is only saved once the month is over; before, it is
      // "Unspent income", and part of it is still planned for this month.
      savedLabel: current ? 'Unspent income' : 'Saved',
      saved: this.money(toDate.saved),
      negative: toDate.saved < 0,
      // How spent compares with the plan, in words (UX-07: color is never the only signal).
      spentVersus:
        under === 0
          ? 'On plan'
          : left > 0
            ? `${formatMoney(left, currency)} of the plan left this month`
            : under > 0
              ? `${formatMoney(under, currency)} under plan`
              : `${formatMoney(-under, currency)} over plan`,
      spentOver: under < 0,
      // R-PLN-03: the Unassigned of the latest started month, what the plan meant to keep by now.
      // In progress it splits: what is still to spend in the month, and what is left to keep.
      savedVersus:
        ahead === 0
          ? left > 0
            ? `${formatMoney(left, currency)} still to spend in ${current!.period.label} · ` +
              `${formatMoney(toMinor(toDate.saved) - left, currency)} to keep (as planned)`
            : 'As planned'
          : ahead > 0
            ? `${formatMoney(ahead, currency)} more than planned`
            : `${formatMoney(-ahead, currency)} less than planned`,
    };
  });

  // DSH-05, from the top bar's summary: null until it loads; its projection is null when no FY
  // contains today.
  protected readonly projection = computed(() => {
    const summary = this.summaryService.summary();
    if (!summary) {
      return null;
    }
    const projection = summary.projection;
    return {
      data: projection,
      monthLabel: summary.month?.period.label ?? '',
      end: projection ? `End of ${projection.fiscalYearLabel} · ${formatDate(projection.endDate)}` : '',
    };
  });

  // DSH-06, R-PLN-08: planned saving at the FY's end next to the projected saving. Leftovers are spent
  // (they carry), so the projection is lower only by what was overspent.
  protected readonly saving = computed(() => {
    const projection = this.summaryService.summary()?.projection;
    if (!projection) {
      return null;
    }
    const currency = this.currency();
    const below = toMinor(projection.plannedSaved) - toMinor(projection.projectedSaving);
    return {
      fy: projection.fiscalYearLabel,
      // Owner note 11: say which months Planned saving covers (R-PLN-06), so it is not taken for the
      // whole FY (Plan's Accumulated Planned Savings is).
      covers: projection.countedFrom
        ? `from ${projection.countedFrom.label}, when you started NAMA, to the end of ${projection.fiscalYearLabel}`
        : 'for the whole year',
      planned: this.money(projection.plannedSaved),
      projected: this.money(projection.projectedSaving),
      plannedNegative: projection.plannedSaved < 0,
      projectedNegative: projection.projectedSaving < 0,
      onTrack: below <= 0,
      verdict: below <= 0 ? 'On track' : `${formatMoney(below, currency)} below plan`,
      reason: below <= 0 ? 'Your plan is covered so far.' : 'You have spent more than planned so far.',
      lines: [
        // Owner note 12: the same number as FY to date's "Unspent income" (the month is not over).
        { op: '', label: 'Unspent income', value: this.money(projection.savedToDate) },
        { op: '−', label: 'Still to spend this month', value: this.money(projection.stillToSpend) },
        { op: '+', label: 'Future unallocated', value: this.money(projection.futureUnallocated) },
      ],
    };
  });

  // DSH-04, R-BAL-03, from the top bar's summary; savedClosed is the overview's (closed months).
  protected readonly banks = computed(() => {
    const summary = this.summaryService.summary();
    if (!summary) {
      return null;
    }
    const savedClosed = this.data()?.toDate.savedClosed;
    return {
      total: summary.balance === null ? null : this.money(summary.balance),
      negative: (summary.balance ?? 0) < 0,
      count: `${summary.accounts.length} ${summary.accounts.length === 1 ? 'account' : 'accounts'}`,
      accounts: summary.accounts,
      added: savedClosed === undefined ? null : this.money(savedClosed),
      addedNegative: (savedClosed ?? 0) < 0,
    };
  });

  // DSH-07 (owner note 20): the loan totals, no names. Its own small call, made only here; the card
  // stays hidden when it fails.
  private readonly loanTotals = signal<LoanTotals | null>(null);
  protected readonly loans = computed(() => {
    const totals = this.loanTotals();
    if (!totals) {
      return null;
    }
    const count = (n: number) => `${n} open`;
    return {
      empty: totals.lent.openCount + totals.borrowed.openCount + totals.lent.settledCount + totals.borrowed.settledCount === 0,
      lent: this.money(totals.lent.open),
      lentCount: count(totals.lent.openCount),
      borrowed: this.money(totals.borrowed.open),
      borrowedCount: count(totals.borrowed.openCount),
      net: this.money(totals.net),
      netNegative: totals.net < 0,
    };
  });

  constructor() {
    void this.load();
    void this.loadLoans();
  }

  // FY-01, FY-02: the banner already set the new /api/me. The page shows the FY that now contains
  // today, else the one just started, and the banner goes (rollover is null after Start).
  protected rolledOver(label: string, fyId: string): void {
    this.status.set(`${label} started.`);
    void this.load(this.meService.me()?.currentFyId ?? fyId);
  }

  private async load(fyIdOverride?: string): Promise<void> {
    try {
      const me = await this.meService.load();
      const fyId = fyIdOverride || me.currentFyId || (await this.planApi.fiscalYears()).at(-1)?.id;
      if (!fyId) {
        this.loadError.set('There is no fiscal year yet.');
        return;
      }
      this.data.set(await this.overviewApi.overview(fyId));
      this.categoryId.set('');
      this.loadError.set('');
    } catch {
      this.loadError.set('The dashboard could not be loaded. Please reload the page.');
    }
  }

  private async loadLoans(): Promise<void> {
    try {
      this.loanTotals.set(await this.loansApi.totals());
    } catch {
      this.loanTotals.set(null);
    }
  }

  private money(amount: number): string {
    return formatMoney(toMinor(amount), this.currency());
  }
}
