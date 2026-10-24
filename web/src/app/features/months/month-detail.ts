import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { countsLabel } from '../../shared/income-months';
import { formatMoney, toMinor } from '../../shared/money';
import { formatDate, formatDay } from '../../shared/month-label';
import { MonthSummary } from '../../shared/month-summary';
import { Entry } from '../add-entry/entry-api';
import { DayBars } from './day-bars';
import { MonthDetail as MonthDetailData } from './month-api';
import { STATUS_LABELS, SpendLine, availableMinor, nameMaps, spendLine } from './month-lines';
import { Cell, OverviewResponse } from './overview-api';

// The selected month's detail call (owner note 9). data stays while a reload runs, so the page does
// not flicker.
export interface MonthDetailState {
  state: 'loading' | 'error' | 'ok';
  data?: MonthDetailData;
}

// Owner note 9: Tracker shows one month at a time, in detail. The month's numbers (MON-02), then:
// spending by day, plan vs spent by category (R-PLN-05), each category against the month before, each
// bank account's share, the biggest entries and the newest ones (MON-04 "See all" opens Entries). The
// overview gives the plan lines; GET .../months/{periodKey} gives the rest. The API makes every sum;
// this picks and formats.
@Component({
  selector: 'app-month-detail',
  imports: [
    NgTemplateOutlet,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    DayBars,
    MonthSummary,
  ],
  templateUrl: './month-detail.html',
  styleUrl: './month-detail.scss',
})
export class MonthDetail {
  readonly overview = input.required<OverviewResponse>();
  readonly periodKey = input.required<string>();
  readonly overOnly = input(false);
  readonly detail = input<MonthDetailState | null>(null);
  readonly currency = input.required<string>();
  readonly today = input<string | null>(null);
  // Entries' filters for "See all": the FY, the month and Tracker's filter.
  readonly entriesQuery = input.required<Record<string, string>>();

  readonly edit = output<Entry>();
  readonly retry = output<void>();

  protected readonly statusLabels = STATUS_LABELS;
  protected readonly formatDay = formatDay;

  private readonly names = computed(() => nameMaps(this.overview().categories));
  private readonly index = computed(() =>
    this.overview().months.findIndex((m) => m.period.key === this.periodKey()),
  );

  protected readonly month = computed(() => this.overview().months[this.index()] ?? null);
  protected readonly dates = computed(() => {
    const month = this.month();
    return month
      ? `${formatDay(month.period.startDate)} to ${formatDay(month.period.endDate)}`
      : '';
  });

  // Plan vs spent: the categories and sub-categories with a plan, spending or a carry this month
  // (owner's choice, M7); with "Over plan only", only what went over. Owner note 26: one line each,
  // over plan first, then near the limit, then the largest share spent (Plan's order for ties). A
  // category opens to its sub-categories; one with a single sub-category does not repeat it, and
  // says its name beside the category's when it differs.
  protected readonly categories = computed(() => {
    const i = this.index();
    const names = this.names();
    if (i < 0) {
      return [];
    }
    return byAttention(
      this.overview()
        .rows.map((row) => {
          const name = names.categories.get(row.categoryId) ?? '';
          const subs = byAttention(
            row.subCategories
              .filter((sub) =>
                this.overOnly()
                  ? this.line(sub.months[i]).status === 'danger'
                  : this.hasMoney(sub.months[i]),
              )
              .map((sub) => ({
                id: sub.subCategoryId,
                name: names.subs.get(sub.subCategoryId) ?? '',
                line: this.line(sub.months[i]),
              })),
          );
          return {
            id: row.categoryId,
            name,
            line: this.line(row.months[i]),
            subs,
            single: subs.length === 1 ? { detail: subs[0].name === name ? null : subs[0].name } : null,
          };
        })
        .filter((category) => category.subs.length > 0),
    );
  });

  // Owner note 26: the opened categories; with "Over plan only" every category is open.
  private readonly opened = signal<ReadonlySet<string>>(new Set());
  protected isOpen(id: string): boolean {
    return this.overOnly() || this.opened().has(id);
  }
  protected toggle(id: string): void {
    const next = new Set(this.opened());
    if (!next.delete(id)) {
      next.add(id);
    }
    this.opened.set(next);
  }

  protected readonly data = computed(() => this.detail()?.data ?? null);

  // The day with the most spending (a pick, not a sum).
  protected readonly biggestDay = computed(() => {
    const days = this.data()?.days ?? [];
    const top = days.reduce<(typeof days)[number] | null>(
      (best, day) => (day.spent > (best?.spent ?? 0) ? day : best),
      null,
    );
    return top ? `${formatDay(top.date)} · ${this.money(top.spent)}` : null;
  });

  // Each category against the month before, the change in words (UX-07: never color alone).
  protected readonly changes = computed(() => {
    const data = this.data();
    if (!data) {
      return [];
    }
    const names = this.names().categories;
    const last = data.lastMonth?.label ?? '';
    return data.categories.map((change) => {
      const diff = toMinor(change.spent) - toMinor(change.lastSpent);
      return {
        id: change.categoryId,
        name: names.get(change.categoryId) ?? '',
        spent: this.money(change.spent),
        last: this.money(change.lastSpent),
        more: diff > 0,
        words:
          diff === 0
            ? `Same as ${last}`
            : `${formatMoney(Math.abs(diff), this.currency())} ${diff > 0 ? 'more' : 'less'} than ${last}`,
      };
    });
  });

  // Owner note 17 (R-INC-04, R-INC-05): the month's additional income by name, what it counts in and
  // the account it went into, so a gift or a bonus shows: "Gift · Actual · into SNB · SAR 500".
  protected readonly extraIncome = computed(() => {
    const data = this.data();
    return (data?.additionalIncome ?? []).map((item) => {
      const account = data!.bankAccounts.find((option) => option.id === item.bankAccountId)?.name;
      return {
        id: item.id,
        label: item.label,
        caption: [countsLabel(item.counts), account ? `into ${account}` : ''].filter(Boolean).join(' · '),
        amount: this.money(item.amount),
      };
    });
  });

  // Each account's share of the month's spending (a display ratio).
  protected readonly accounts = computed(() => {
    const data = this.data();
    if (!data) {
      return [];
    }
    const names = new Map(data.bankAccounts.map((account) => [account.id, account.name]));
    const total = toMinor(data.total);
    return data.accounts.map((account) => {
      const share = total > 0 ? (toMinor(account.spent) * 100) / total : 0;
      return {
        id: account.bankAccountId,
        name: names.get(account.bankAccountId) ?? '',
        amount: this.money(account.spent),
        share,
        percent: `${Math.round(share)}%`,
      };
    });
  });

  protected readonly latest = computed(() =>
    (this.data()?.latest ?? []).map((entry) => this.entryRow(entry)),
  );
  protected readonly biggest = computed(() =>
    (this.data()?.biggest ?? []).map((entry) => this.entryRow(entry)),
  );

  protected money(amount: number): string {
    return formatMoney(toMinor(amount), this.currency());
  }

  private entryRow(entry: Entry) {
    const sub = this.names().subs.get(entry.subCategoryId) ?? '';
    const amount = this.money(entry.amount);
    return {
      entry,
      day: formatDay(entry.date),
      sub,
      amount,
      editLabel: `Edit ${sub}, ${amount}, ${formatDate(entry.date)}`,
    };
  }

  // A plan, spending, or money (or an overspend) carried in from last month.
  private hasMoney(cell: Cell): boolean {
    return cell.planned > 0 || cell.spent > 0 || cell.carriedIn !== 0;
  }

  // docs/DESIGN.md category rows: name and chip, progress bar, "SAR 1,240 of 2,000" (available).
  private line(cell: Cell): SpendLine {
    return spendLine(availableMinor(cell), toMinor(cell.spent), this.currency());
  }
}

// Owner note 26: over plan first, then near the limit, then the largest share spent; ties keep
// their order (Array.prototype.sort is stable).
function byAttention<T extends { line: SpendLine }>(items: T[]): T[] {
  const rank = (line: SpendLine) => (line.status === 'danger' ? 0 : line.status === 'warn' ? 1 : 2);
  return [...items].sort((a, b) => rank(a.line) - rank(b.line) || (b.line.bar ?? 0) - (a.line.bar ?? 0));
}
