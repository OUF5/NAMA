import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { Params, RouterLink } from '@angular/router';
import { formatAmount, formatMoney, toMinor } from '../../shared/money';
import { STATUS_LABELS, balanceRow, nameMaps, spendLine } from './month-lines';
import { BalancesResponse, Cell, OverviewResponse } from './overview-api';

// MON-02 Year view: category rows (they expand to sub-categories), the months plus an FY total, and
// a Total row. The header shows each month's status (MON-01). Each cell opens Entries filtered by
// its sub-category or category and month (MON-04). Owner note 32: a month cell shows Spent (its color
// against the month's own plan), Plan (the month's own) and Available (R-PLN-05: the plan plus what
// carried in from the FY's first month, what this grid called Plan before); the FY total is FY spent
// of FY planned.
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
    // TBL-07: a cell opens Entries on its expenses only, the spending it shows.
    const type = 'expense';
    const months = data.months;
    const cells = (
      name: string,
      cellMonths: Pick<Cell, 'periodKey' | 'carriedIn' | 'planned' | 'spent'>[],
      planned: number,
      spent: number,
      query: Params,
    ) => [
      ...cellMonths.map((cell, i) =>
        this.cell(name, months[i].period.label, cell, { fyId, type, periodKey: cell.periodKey, ...query }),
      ),
      this.cell(name, this.fiscalYearLabel(), { carriedIn: null, planned, spent }, { fyId, type, ...query }),
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
        months.map((month) => ({ ...month, periodKey: month.period.key })),
        data.totals.planned,
        data.totals.spent,
        {},
      ),
    };
  });

  // The Balance row: one cell per month, then the FY's end (its last month).
  protected readonly balanceRow = computed(() => balanceRow(this.data().months, this.balances(), this.currency()));

  protected toggleCategory(id: string): void {
    this.openCategories.update((ids) => {
      const next = new Set(ids);
      if (!next.delete(id)) {
        next.add(id);
      }
      return next;
    });
  }

  // A cell with nothing planned, nothing spent and nothing carried in shows "–" without color (M7).
  // Owner note 32: Available (R-PLN-05, --danger below 0) in the month cells; the FY column has none
  // (carriedIn null), as nothing carries into the FY.
  private cell(name: string, column: string, amounts: { carriedIn: number | null; planned: number; spent: number }, query: Params) {
    const planned = toMinor(amounts.planned);
    const spent = toMinor(amounts.spent);
    const available = amounts.carriedIn === null ? null : toMinor(amounts.carriedIn) + planned;
    const line = spendLine(planned, spent, this.currency());
    const money = (minor: number) => formatMoney(minor, this.currency());
    const shown = line.status !== null || (available ?? 0) !== 0;
    const availableWords = available === null ? '' : `, ${money(available)} available`;
    const label = shown
      ? `${name}, ${column}: ${money(spent)} of ${money(planned)} plan${availableWords}${line.chip ? `, ${line.chip}` : ''}. Open entries`
      : `${name}, ${column}: nothing planned or spent. Open entries`;
    return {
      ...line,
      shown,
      label,
      query,
      spentAmount: formatAmount(spent),
      plannedAmount: formatAmount(planned),
      availableAmount: available === null ? null : formatAmount(available),
      short: (available ?? 0) < 0,
    };
  }
}
