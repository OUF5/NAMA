import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { countsLabel } from '../../shared/income-months';
import { formatAmount, formatMoney, toMinor } from '../../shared/money';
import { formatDate, formatDay } from '../../shared/month-label';
import { MonthSummary } from '../../shared/month-summary';
import { Entry } from '../add-entry/entry-api';
import { DayBars } from './day-bars';
import { MonthDetail as MonthDetailData } from './month-api';
import { STATUS_LABELS, SpendLine, backOptions, backWords, monthsBack, nameMaps, spendLine } from './month-lines';
import { Cell, OverviewResponse } from './overview-api';

// The selected month's detail call (owner note 9). data stays while a reload runs, so the page does
// not flicker.
export interface MonthDetailState {
  state: 'loading' | 'error' | 'ok';
  data?: MonthDetailData;
}

// Owner note 9: Tracker shows one month at a time, in detail. The month's numbers (MON-02), then:
// spending by day, plan vs spent by category (the month on its own, owner note 32), the overspend
// still to make up by category over the months back picked here (owner note 32, R-PLN-09), each bank
// account's share, the biggest entries and the newest ones (MON-04 "See all" opens Entries). The
// overview gives the plan lines; GET .../months/{periodKey} gives the rest. The API makes every sum;
// this picks and formats.
@Component({
  selector: 'app-month-detail',
  imports: [
    NgTemplateOutlet,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatProgressBarModule,
    MatSelectModule,
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
  // Owner note 32: ?back=, the months back the overspend is counted over ("1" by default).
  readonly back = input('1');

  readonly edit = output<Entry>();
  readonly retry = output<void>();
  readonly backChange = output<string>();

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

  // Plan vs spent: the categories and sub-categories with a plan or spending this month (owner's
  // choice, M7; owner note 32: this month only, a carry or an overspend alone is not listed); with
  // "Over plan only", only what went over this month's plan. Owner note 26: one line each,
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

  // Owner note 32: the picker's months back (only the FY's months before this one), and the one shown,
  // matched by count so "6" in Nov of an FY that started in Sep shows "Since Sep 2026".
  private readonly firstLabel = computed(() => this.overview().months[0]?.period.label ?? '');
  protected readonly backOptions = computed(() => backOptions(Math.max(this.index(), 0), this.firstLabel()));
  protected readonly backValue = computed(() => {
    const count = monthsBack(this.back(), Math.max(this.index(), 0));
    return this.backOptions().find((option) => option.count === count)?.value ?? '1';
  });

  // Owner note 32 (R-PLN-09): what went over plan in the months back and is not made up yet, by
  // category, against what is left of this month's plan. The words and the months come from the
  // answer, so they always match its amounts. Amounts without the currency, which the caption names
  // ("Sep 2026 to Oct 2026 · SAR 700 in all"), so four columns fit a phone.
  protected readonly overspent = computed(() => {
    const data = this.data()?.overspent;
    if (!data) {
      return null;
    }
    const months = this.overview().months;
    const count = data.monthsBack;
    const last = months[this.index() - 1]?.period.label ?? '';
    const span = !data.from ? '' : count === 1 ? last : `${data.from.label} to ${last}`;
    const total = toMinor(data.total);
    const names = this.names().categories;
    return {
      count,
      total: data.total,
      words: backWords(count, this.index(), this.firstLabel()),
      caption: total > 0 ? `${span} · ${formatMoney(total, this.currency())} in all` : span,
      rows: data.categories.map((category) => ({
        id: category.categoryId,
        name: names.get(category.categoryId) ?? '',
        overspent: formatAmount(toMinor(category.overspent)),
        left: formatAmount(toMinor(category.left)),
        after: formatAmount(toMinor(category.after)),
        short: toMinor(category.after) < 0,
      })),
    };
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

  // A plan or spending this month (owner note 32: the month on its own).
  private hasMoney(cell: Cell): boolean {
    return cell.planned > 0 || cell.spent > 0;
  }

  // docs/DESIGN.md category rows: name and chip, progress bar, "SAR 1,240 of 2,000". Owner note 32:
  // "of" is this month's plan, not what carried in (the month card's "Remaining − Overspent last month" has the overspend).
  private line(cell: Cell): SpendLine {
    return spendLine(toMinor(cell.planned), toMinor(cell.spent), this.currency());
  }
}

// Owner note 26: over plan first, then near the limit, then the largest share spent; ties keep
// their order (Array.prototype.sort is stable).
function byAttention<T extends { line: SpendLine }>(items: T[]): T[] {
  const rank = (line: SpendLine) => (line.status === 'danger' ? 0 : line.status === 'warn' ? 1 : 2);
  return [...items].sort((a, b) => rank(a.line) - rank(b.line) || (b.line.bar ?? 0) - (a.line.bar ?? 0));
}
