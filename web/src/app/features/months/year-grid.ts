import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { Params, RouterLink } from '@angular/router';
import { formatMoney, toMinor } from '../../shared/money';
import { STATUS_LABELS, nameMaps, spendLine } from './month-lines';
import { Cell, OverviewResponse } from './overview-api';

// MON-02 Year view: category rows (they expand to sub-categories), the months plus an FY total, and
// a Total row. The header shows each month's status (MON-01). Each cell opens Entries filtered by
// its sub-category or category and month (MON-04).
@Component({
  selector: 'app-year-grid',
  imports: [NgTemplateOutlet, RouterLink, MatIconModule],
  templateUrl: './year-grid.html',
  styleUrl: './year-grid.scss',
})
export class YearGrid {
  readonly data = input.required<OverviewResponse>();
  readonly currency = input.required<string>();

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
        this.cell(name, months[i].period.label, cell.planned, cell.spent, {
          fyId,
          periodKey: cell.periodKey,
          ...query,
        }),
      ),
      this.cell(name, this.fiscalYearLabel(), planned, spent, { fyId, ...query }),
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
          planned: month.planned,
          spent: month.spent,
        })),
        data.totals.planned,
        data.totals.spent,
        {},
      ),
    };
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

  // A cell with nothing planned and nothing spent shows "–" without color (M7).
  private cell(
    name: string,
    column: string,
    plannedAmount: number,
    spentAmount: number,
    query: Params,
  ) {
    const planned = toMinor(plannedAmount);
    const spent = toMinor(spentAmount);
    const line = spendLine(planned, spent, this.currency());
    const money = (minor: number) => formatMoney(minor, this.currency());
    const label = line.status
      ? `${name}, ${column}: ${money(spent)} of ${money(planned)}, ${line.chip}. Open entries`
      : `${name}, ${column}: nothing planned or spent. Open entries`;
    return { ...line, label, query };
  }
}
