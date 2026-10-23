import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatAmount, formatMoney, toMinor } from '../../shared/money';
import { STATUS_LABELS, balanceRow } from './month-lines';
import { BalancesResponse, OverviewResponse } from './overview-api';

// Owner note 18: Tracker's Totals, the first view. Plan's Allocating table (PLN-03) with what really
// happened: Income - Spent = Unspent this month, added up from the FY's first month = Accumulated
// savings, with Planned next to Spent and the Balance row of the Year view under it. Every month plus
// an FY total, read top to bottom; the month in progress is marked. A month yet to come has nothing
// spent yet, so its actual rows show "–". With a category filter only Planned and Spent are left:
// income, savings and the balance belong to the whole budget (MON-05). The API makes every sum.
@Component({
  selector: 'app-tracker-totals',
  imports: [RouterLink],
  templateUrl: './tracker-totals.html',
  styleUrl: './tracker-totals.scss',
})
export class TrackerTotals {
  readonly data = input.required<OverviewResponse>();
  readonly currency = input.required<string>();
  // null while it loads; 'error' when it could not be loaded.
  readonly balances = input<BalancesResponse | 'error' | null>(null);

  // The row labels, then equal month columns (scrolls sideways below that width), as on Allocating.
  protected readonly labelWidth = 168;
  protected readonly columnWidth = 72;

  protected readonly fiscalYearLabel = computed(() => {
    const data = this.data();
    return data.fiscalYears.find((fy) => fy.id === data.fyId)?.label ?? '';
  });
  protected readonly firstMonth = computed(() => this.data().months[0]?.period.label ?? '');
  // A filter has no income (MON-05).
  protected readonly whole = computed(() => this.data().totals.income !== null);
  // The planned income gets its own row only when it differs from what came in (R-INC-04).
  protected readonly showPlannedIncome = computed(() =>
    this.data().months.some((month) => toMinor(month.plannedIncome ?? 0) !== toMinor(month.income ?? 0)),
  );
  // The FY column's savings are the months so far while some are still to come.
  protected readonly soFar = computed(() => this.data().months.some((month) => month.status !== 'closed'));

  protected readonly columns = computed(() =>
    this.data().months.map((month) => {
      const upcoming = month.status === 'yetToCome';
      return {
        key: month.period.key,
        month: month.period.label.slice(0, 3),
        year: month.period.label.slice(4),
        title: `${month.period.label} · ${STATUS_LABELS[month.status]}`,
        now: month.status === 'inProgress',
        income: this.amount(month.income),
        plannedIncome: this.amount(month.plannedIncome),
        planned: this.amount(month.planned),
        spent: upcoming && toMinor(month.spent) === 0 ? null : this.amount(month.spent),
        unspent: this.amount(month.unspent),
        unspentOver: (month.unspent ?? 0) < 0,
        saved: this.amount(month.saved),
        savedOver: (month.saved ?? 0) < 0,
      };
    }),
  );

  protected readonly fiscalYearColumn = computed(() => {
    const totals = this.data().totals;
    return {
      income: this.amount(totals.income),
      plannedIncome: this.amount(totals.plannedIncome),
      planned: this.amount(totals.planned),
      spent: this.amount(totals.spent),
      saved: this.amount(totals.saved),
      savedMoney: totals.saved === null ? null : formatMoney(toMinor(totals.saved), this.currency()),
      over: (totals.saved ?? 0) < 0,
    };
  });

  protected readonly balanceRow = computed(() => balanceRow(this.data().months, this.balances(), this.currency()));

  // "1,240"; null (shown as "–") when there is nothing to show.
  private amount(value: number | null): string | null {
    return value === null ? null : formatAmount(toMinor(value));
  }
}
