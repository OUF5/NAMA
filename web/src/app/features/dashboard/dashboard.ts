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

// PRD 5.9 Dashboard (DSH-01 to DSH-04, DSH-06, DSH-07) and the FY-01 banner (owner note 8). It shows
// the FY that contains today, else the latest; there is no FY selector. One call: the overview
// (NFR-01), plus GET /api/fiscal-years only when no FY contains today. Today's day of the month and
// the bank balances come from the top bar's summary (UX-08), so they cost no call. Plan vs actual and
// the saving count from the FY's first month, as Tracker and Plan do (owner note 24). The API makes
// every sum (R-MON-01); this page picks, ranks and formats.
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
  // the same month). Owner note 24: no "From last month"; what carried in is said under Remaining, and
  // Income (actual) says how much of it is additional income.
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
      carried: toMinor(month.carriedIn) === 0 ? null : `incl. ${this.money(month.carriedIn)} from last month`,
      kpis: [
        { label: 'Planned', value: this.money(month.planned), note: null },
        { label: 'Spent', value: this.money(month.spent), note: null },
        ...(month.income === null
          ? []
          : [
              {
                label: 'Income',
                value: this.money(month.income),
                note: toMinor(month.additionalIncome ?? 0) === 0 ? null : `incl. ${this.money(month.additionalIncome!)} additional`,
              },
            ]),
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

  // Owner note 24, DSH-02 Plan vs actual: the started months from the FY's first month (the one in
  // progress in full), planned next to actual for income, spent and the accumulated saving, each with
  // the difference in words and a color (UX-07: color is never the only signal).
  protected readonly versus = computed(() => {
    const data = this.data();
    const vs = data?.planVsActual;
    if (!data || !vs) {
      return null;
    }
    const currency = this.currency();
    const started = data.months.filter((month) => month.status !== 'yetToCome');
    const current = started.find((month) => month.status === 'inProgress');
    const still = toMinor(vs.stillToSpend);
    const stillNote = current && still > 0 ? `${formatMoney(still, currency)} still to spend in ${current.period.label}` : null;
    const income = toMinor(vs.income) - toMinor(vs.plannedIncome);
    const left = toMinor(vs.planned) - toMinor(vs.spent);
    // Owner note 12: the month in progress's unspent plan is still to spend, so it is not ahead yet.
    const ahead = toMinor(vs.saved) - still - toMinor(vs.plannedSaved);
    const additional = toMinor(vs.additionalIncome);
    const spentLine = spendLine(toMinor(vs.planned), toMinor(vs.spent), currency);
    return {
      range:
        started.length === 0
          ? 'No month has started yet'
          : `${started.length === 1 ? started[0].period.label : `${started[0].period.label} to ${started.at(-1)!.period.label}`}` +
            ` · ${started.length} of ${data.months.length} months`,
      rows: [
        {
          key: 'income',
          label: 'Income',
          sub: null,
          planned: this.money(vs.plannedIncome),
          actual: this.money(vs.income),
          ok: income >= 0,
          verdict:
            income === 0
              ? 'As planned'
              : income > 0
                ? `${formatMoney(income, currency)} more than planned`
                : `${formatMoney(-income, currency)} less than planned`,
          note: additional === 0 ? null : `Actual incl. ${formatMoney(additional, currency)} additional income`,
          bar: null,
          status: null,
        },
        {
          key: 'spent',
          label: 'Spent',
          sub: null,
          planned: this.money(vs.planned),
          actual: this.money(vs.spent),
          ok: left >= 0,
          // Owner note 11: while a month is in progress, what is left of its plan is still to spend,
          // so it is "left", not "under plan".
          verdict:
            left === 0
              ? 'On plan'
              : left < 0
                ? `${formatMoney(-left, currency)} over plan`
                : current
                  ? `${formatMoney(left, currency)} of the plan left`
                  : `${formatMoney(left, currency)} under plan`,
          // Said once: the verdict already is what is left when nothing else is.
          note: stillNote && still !== left ? stillNote : null,
          bar: spentLine.bar,
          status: spentLine.status,
        },
        {
          key: 'saved',
          label: 'Saving',
          sub: 'accumulated',
          planned: this.money(vs.plannedSaved),
          actual: this.money(vs.saved),
          ok: ahead >= 0,
          verdict:
            ahead === 0
              ? 'As planned'
              : ahead > 0
                ? `${formatMoney(ahead, currency)} ahead of plan`
                : `${formatMoney(-ahead, currency)} behind plan`,
          // Owner note 12: part of what is unspent is still planned for this month.
          note: stillNote ? `Actual incl. ${stillNote}` : null,
          bar: null,
          status: null,
        },
      ],
    };
  });

  // Owner note 24, DSH-06: Plan's Accumulated Planned Savings at the FY's end next to Tracker's
  // projected Accumulated savings, the difference in words, and the progress so far: saved against
  // the FY's planned saving, with where the plan expects you to be by now.
  protected readonly saving = computed(() => {
    const data = this.data();
    const vs = data?.planVsActual;
    const fiscalYear = this.fiscalYear();
    if (!data || !vs || !fiscalYear) {
      return null;
    }
    const currency = this.currency();
    const planned = toMinor(vs.plannedSavedAtEnd);
    const projected = toMinor(vs.projectedSaved);
    const difference = projected - planned;
    const current = data.months.find((month) => month.status === 'inProgress');
    const share = (amount: number) => Math.max(0, Math.min(100, (amount * 100) / planned));
    return {
      fy: fiscalYear.label,
      first: data.months[0]?.period.label ?? '',
      planned: this.money(vs.plannedSavedAtEnd),
      projected: this.money(vs.projectedSaved),
      plannedNegative: planned < 0,
      projectedNegative: projected < 0,
      ok: difference >= 0,
      verdict:
        difference === 0
          ? 'On track'
          : difference > 0
            ? `${formatMoney(difference, currency)} above plan`
            : `${formatMoney(-difference, currency)} below plan`,
      reason:
        difference === 0
          ? 'Your plan is covered so far.'
          : difference > 0
            ? 'More came in or less went out than planned so far.'
            : 'Less came in or more went out than planned so far.',
      // Progress toward the FY's planned saving; only when there is a positive plan to measure against.
      progress:
        planned > 0
          ? {
              value: share(toMinor(vs.saved)),
              marker: share(toMinor(vs.plannedSaved)),
              text: `Saved so far ${this.money(vs.saved)} of ${this.money(vs.plannedSavedAtEnd)}`,
              byNow: `Plan by now ${this.money(vs.plannedSaved)}`,
            }
          : null,
      lines: [
        { op: '', label: 'Accumulated savings so far', value: this.money(vs.saved) },
        {
          op: '−',
          label: current ? `Still to spend in ${current.period.label}` : 'Still to spend this month',
          value: this.money(vs.stillToSpend),
        },
        { op: '+', label: 'Months to come: income − plan', value: this.money(vs.futureUnspent) },
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
