import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatAmount, formatMoney, toMinor } from '../../shared/money';
import { STATUS_LABELS, balanceRow } from './month-lines';
import { BalancesResponse, OverviewResponse } from './overview-api';

// Owner note 18: Tracker's Totals, the first view. What really happened, and where it is heading
// (owner note 23): Income (with the part from additional income under it) - Spent = Unspent this
// month, added up from the FY's first month = Accumulated savings, and the Balance row of the Year
// view under it. The plan is on Plan, so Planned is not repeated here (owner note 23). Every month
// plus an FY total, read top to bottom; the month in progress is marked. A month yet to come has
// nothing spent yet, so Spent shows "–" and its Unspent and Accumulated are projected: its income
// minus its plan, after the month in progress, which shows its projected Accumulated under the real
// one, spends the rest of its plan. With a category filter only Spent is left: income, savings and
// the balance belong to the whole budget (MON-05). The API makes every sum.
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
        // Owner note 23: marks a month with additional income; "–" without.
        additionalIncome: this.nonZero(month.additionalIncome),
        spent: upcoming && toMinor(month.spent) === 0 ? null : this.amount(month.spent),
        unspent: this.amount(upcoming ? month.projectedUnspent : month.unspent),
        unspentOver: ((upcoming ? month.projectedUnspent : month.unspent) ?? 0) < 0,
        saved: this.amount(upcoming ? month.projectedSaved : month.saved),
        savedOver: ((upcoming ? month.projectedSaved : month.saved) ?? 0) < 0,
        // A month yet to come shows only projected numbers.
        projected: upcoming && month.projectedSaved !== null,
        // The month in progress: its Accumulated once the rest of its plan is spent.
        savedThen: month.status === 'inProgress' ? this.amount(month.projectedSaved) : null,
        savedThenOver: (month.projectedSaved ?? 0) < 0,
      };
    }),
  );

  protected readonly fiscalYearColumn = computed(() => {
    const totals = this.data().totals;
    return {
      income: this.amount(totals.income),
      additionalIncome: this.nonZero(totals.additionalIncome),
      spent: this.amount(totals.spent),
      saved: this.amount(totals.saved),
      savedMoney: totals.saved === null ? null : formatMoney(toMinor(totals.saved), this.currency()),
      over: (totals.saved ?? 0) < 0,
      projectedMoney: totals.projectedSaved === null ? null : formatMoney(toMinor(totals.projectedSaved), this.currency()),
      projectedOver: (totals.projectedSaved ?? 0) < 0,
    };
  });

  protected readonly balanceRow = computed(() => balanceRow(this.data().months, this.balances(), this.currency()));

  // "1,240"; null (shown as "–") when there is nothing to show.
  private amount(value: number | null): string | null {
    return value === null ? null : formatAmount(toMinor(value));
  }

  // The same, with 0 as "–" too.
  private nonZero(value: number | null): string | null {
    return value === null || toMinor(value) === 0 ? null : this.amount(value);
  }
}
