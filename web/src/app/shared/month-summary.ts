import { Component, computed, input } from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { spendLine } from '../features/months/month-lines';
import { MonthOverview } from '../features/months/overview-api';
import { formatMoney, toMinor } from './money';

// docs/DESIGN.md KPI block of one month, Tracker's month card (MON-02). Owner note 32: the month on its
// own first: Remaining this month = Planned - Spent, and the bar and chip compare Spent with Planned.
// Beside it, Remaining − Overspent last month: what is left once the overspend not made up yet is
// covered (R-PLN-09, over the months back picked on Overspent to make up; money left over does not
// carry), with that overspend under it. Income is the month's own: the actual income, with Plan's
// under it when they differ (owner note 17). Desktop: the five in one row, then the bar. Phones ("KPI
// on phones"): Remaining this month large, the bar, then the others two by two. The numbers come from
// the overview (decimals, R-MON-01); this component only formats.
@Component({
  selector: 'app-month-summary',
  imports: [MatProgressBarModule],
  template: `
    @let l = line();
    <!-- No Income while Tracker is filtered to a category (income is null then). -->
    <div class="summary" [class.no-income]="month().income === null">
      <div class="kpi remaining">
        <span class="caption">Remaining this month</span>
        <span class="money value" [class]="l.status">{{ remaining() }}</span>
      </div>
      @let after = afterOverspent();
      <div class="kpi after-last">
        <span class="caption">Remaining − Overspent {{ overspentWords() }}</span>
        <span class="money value" [class.danger]="after !== null && after < 0" [class.muted]="after === null">{{
          after === null ? '–' : format(after)
        }}</span>
        @if (overspentCaption(); as caption) {
          <span class="caption">{{ caption }}</span>
        }
      </div>
      <div class="kpi">
        <span class="caption">Planned</span>
        <span class="money value">{{ money(month().planned) }}</span>
      </div>
      <div class="kpi">
        <span class="caption">Spent</span>
        <span class="money value">{{ money(month().spent) }}</span>
      </div>
      @if (month().income !== null) {
        <div class="kpi">
          <span class="caption">Income</span>
          <span class="money value">{{ money(month().income!) }}</span>
          @if (plannedIncome(); as planned) {
            <span class="caption">Planned {{ planned }}</span>
          }
        </div>
      }
      <div class="bar-line">
        @if (l.bar !== null) {
          <mat-progress-bar
            mode="determinate"
            [class]="l.status"
            [value]="l.bar"
            [attr.aria-label]="'Share of the plan spent this month: ' + l.chip"
          />
        }
        @if (l.status) {
          <span class="chip" [class]="l.status">{{ l.chip }}</span>
        }
      </div>
    </div>
  `,
  styleUrl: './month-summary.scss',
})
export class MonthSummary {
  readonly month = input.required<
    Pick<MonthOverview, 'income' | 'planned' | 'spent'> & { plannedIncome?: number | null }
  >();
  readonly currency = input.required<string>();
  // Owner note 32 (R-PLN-09): the overspend to make up, from the month's detail (null until it loads),
  // and the months it was counted over in words ("last month", "last 2 months", "since Aug 2026").
  readonly overspent = input<number | null>(null);
  readonly overspentWords = input('last month');

  // Shown only when it differs from the actual income.
  protected readonly plannedIncome = computed(() => {
    const { income, plannedIncome } = this.month();
    return plannedIncome === undefined || plannedIncome === null || income === null || plannedIncome === income
      ? null
      : this.money(plannedIncome);
  });

  // Owner note 32: the month on its own, nothing carried in. Minor units.
  private readonly remainingMinor = computed(() => toMinor(this.month().planned) - toMinor(this.month().spent));
  protected readonly remaining = computed(() => this.format(this.remainingMinor()));

  // R-PLN-09: the month's remaining once the overspend not made up yet is covered. Minor units.
  protected readonly afterOverspent = computed(() => {
    const overspent = this.overspent();
    return overspent === null ? null : this.remainingMinor() - toMinor(overspent);
  });

  protected readonly overspentCaption = computed(() => {
    const overspent = this.overspent();
    const words = this.overspentWords();
    return overspent === null
      ? null
      : overspent === 0
        ? `Nothing overspent ${words}`
        : `Overspent ${words} ${this.money(overspent)}`;
  });

  protected readonly line = computed(() =>
    spendLine(toMinor(this.month().planned), toMinor(this.month().spent), this.currency()),
  );

  protected money(amount: number): string {
    return formatMoney(toMinor(amount), this.currency());
  }

  protected format(minor: number): string {
    return formatMoney(minor, this.currency());
  }
}
