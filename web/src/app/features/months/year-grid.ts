import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { Params, RouterLink } from '@angular/router';
import { formatAmount, formatMoney, toMinor } from '../../shared/money';
import { STATUS_LABELS, availableMinor, nameMaps, spendLine } from './month-lines';
import { BalancesResponse, Cell, OverviewResponse } from './overview-api';

// MON-02 Year view: category rows (they expand to sub-categories), the months plus an FY total, and
// a Total row. The header shows each month's status (MON-01). Each cell opens Entries filtered by
// its sub-category or category and month (MON-04). R-PLN-05: a month cell shows spent of available
// (its plan plus what carried in); the FY total is FY spent of FY planned, as the carry starts at 0.
// Owner note 9: each cell says what its numbers are ("Spent", "Plan"), and a Balance row under
// the Total gives each month's end balance, real or projected (R-PLN-07).
@Component({
  selector: 'app-year-grid',
  imports: [NgTemplateOutlet, RouterLink, MatIconModule],
  templateUrl: './year-grid.html',
  styleUrl: './year-grid.scss',
})
export class YearGrid {
  readonly data = input.required<OverviewResponse>();
  readonly currency = input.required<string>();
  // null while it loads; 'error' when it could not be loaded.
  readonly balances = input<BalancesResponse | 'error' | null>(null);

  protected readonly statusLabels = STATUS_LABELS;
  protected readonly openCategories = signal<ReadonlySet<string>>(new Set());

  protected readonly fiscalYearLabel = computed(() => {
    const data = this.data();
    return data.fiscalYears.find((fy) => fy.id === data.fyId)?.label ?? '';
  });

  protected readonly grid = computed(() => {
    const data = this.data();
    const names = nameMaps(data.categories);
    const fyId = data.fyId;
    const months = data.months;
    const cells = (
      name: string,
      cellMonths: Cell[],
      planned: number,
      spent: number,
      query: Params,
    ) => [
      ...cellMonths.map((cell, i) =>
        this.cell(name, months[i].period.label, cell, {
          fyId,
          periodKey: cell.periodKey,
          ...query,
        }),
      ),
      this.cell(name, this.fiscalYearLabel(), { carriedIn: 0, planned, spent }, { fyId, ...query }),
    ];
    return {
      months,
      rows: data.rows.map((row) => {
        const name = names.categories.get(row.categoryId) ?? '';
        return {
          id: row.categoryId,
          name,
          cells: cells(name, row.months, row.planned, row.spent, { categoryId: row.categoryId }),
          subs: row.subCategories.map((sub) => {
            const subName = names.subs.get(sub.subCategoryId) ?? '';
            return {
              id: sub.subCategoryId,
              name: subName,
              cells: cells(subName, sub.months, sub.planned, sub.spent, {
                subCategoryId: sub.subCategoryId,
              }),
            };
          }),
        };
      }),
      total: cells(
        'Total',
        months.map((month) => ({
          periodKey: month.period.key,
          carriedIn: month.carriedIn,
          planned: month.planned,
          spent: month.spent,
          remaining: month.remaining,
        })),
        data.totals.planned,
        data.totals.spent,
        {},
      ),
    };
  });

  // The Balance row: one cell per month, then the FY's end (its last month).
  protected readonly balanceRow = computed(() => {
    const balances = this.balances();
    if (balances === null) {
      return { state: 'loading' as const, months: [], end: null };
    }
    if (balances === 'error' || !balances.complete) {
      return { state: balances === 'error' ? ('error' as const) : ('setup' as const), months: [], end: null };
    }
    const byKey = new Map(balances.months.map((month) => [month.periodKey, month]));
    const months = this.data().months.map((month) => {
      const found = byKey.get(month.period.key);
      const amount = found?.balance ?? null;
      return {
        key: month.period.key,
        amount: amount === null ? null : formatAmount(toMinor(amount)),
        negative: (amount ?? 0) < 0,
        projected: found?.projected ?? false,
        now: month.status === 'inProgress',
        label:
          amount === null
            ? `${month.period.label}: balance not known`
            : `${month.period.label}: ${found?.projected ? 'projected ' : ''}balance at the end ${formatMoney(toMinor(amount), this.currency())}`,
      };
    });
    return { state: 'ready' as const, months, end: months.at(-1) ?? null };
  });

  protected toggleCategory(id: string): void {
    this.openCategories.update((ids) => {
      const next = new Set(ids);
      if (!next.delete(id)) {
        next.add(id);
      }
      return next;
    });
  }

  // A cell with nothing available and nothing spent shows "–" without color (M7).
  private cell(name: string, column: string, amounts: { carriedIn: number; planned: number; spent: number }, query: Params) {
    const available = availableMinor(amounts);
    const carried = toMinor(amounts.carriedIn);
    const spent = toMinor(amounts.spent);
    const line = spendLine(available, spent, this.currency());
    const money = (minor: number) => formatMoney(minor, this.currency());
    const from = carried === 0 ? '' : ` (${money(toMinor(amounts.planned))} planned, ${money(carried)} from last month)`;
    const label = line.status
      ? `${name}, ${column}: ${money(spent)} of ${money(available)} plan${from}, ${line.chip}. Open entries`
      : `${name}, ${column}: nothing planned or spent. Open entries`;
    return { ...line, label, query, spentAmount: formatAmount(spent), availableAmount: formatAmount(available) };
  }
}
