import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MeService } from '../../core/me';
import { formatMoney, toMinor } from '../../shared/money';
import { formatDate, formatDay } from '../../shared/month-label';
import { MonthSummary } from '../../shared/month-summary';
import { RolloverBanner } from '../../shared/rollover-banner';
import { STATUS_LABELS } from '../months/month-lines';
import { OverviewApi, OverviewResponse } from '../months/overview-api';
import { PlanApi } from '../plan/plan-api';
import { MonthBars } from './month-bars';

// PRD 5.9 Dashboard preview (DSH-01 to DSH-03) and the FY-01 banner. It shows the FY that contains
// today, else the latest; there is no FY selector. One call: the overview (NFR-01), plus
// GET /api/fiscal-years only when no FY contains today. The API makes every sum (R-MON-01).
@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, MonthBars, MonthSummary, RolloverBanner],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly overviewApi = inject(OverviewApi);
  private readonly planApi = inject(PlanApi);
  private readonly meService = inject(MeService);

  protected readonly formatDate = formatDate;
  protected readonly inProgressLabel = STATUS_LABELS.inProgress;

  protected readonly loadError = signal('');
  protected readonly data = signal<OverviewResponse | null>(null);
  protected readonly status = signal('');

  protected readonly currency = computed(() => this.meService.me()?.profile?.currency ?? 'SAR');
  protected readonly fiscalYear = computed(() => {
    const data = this.data();
    return data?.fiscalYears.find((fiscalYear) => fiscalYear.id === data.fyId) ?? null;
  });

  // DSH-01: the month in progress, with its dates.
  protected readonly current = computed(() => {
    const month = this.data()?.months.find((m) => m.status === 'inProgress');
    return month
      ? { month, dates: `${formatDay(month.period.startDate)} to ${formatDay(month.period.endDate)}` }
      : null;
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

  // DSH-02 "to date" (owner's choice, M9): the months that started (Closed and In progress).
  protected readonly toDateRange = computed(() => {
    const months = this.data()?.months ?? [];
    const started = months.filter((m) => m.status !== 'yetToCome');
    if (started.length === 0) {
      return 'No month has started yet';
    }
    const range =
      started.length === 1
        ? started[0].period.label
        : `${started[0].period.label} to ${started.at(-1)!.period.label}`;
    return `${range} · ${started.length} of ${months.length} months`;
  });

  protected readonly toDate = computed(() => {
    const toDate = this.data()?.toDate;
    return toDate
      ? {
          income: this.money(toDate.income),
          spent: this.money(toDate.spent),
          saved: this.money(toDate.saved),
          negative: toDate.saved < 0,
        }
      : null;
  });

  constructor() {
    void this.load();
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
      this.loadError.set('');
    } catch {
      this.loadError.set('The dashboard could not be loaded. Please reload the page.');
    }
  }

  private money(amount: number): string {
    return formatMoney(toMinor(amount), this.currency());
  }
}
