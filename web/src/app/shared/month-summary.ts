import { Component, computed, input } from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { spendLine } from '../features/months/month-lines';
import { Totals } from '../features/months/overview-api';
import { formatMoney, toMinor } from './money';

// docs/DESIGN.md KPI block of one month, shared by the Months cards (MON-02) and the Dashboard
// current month card (DSH-01). Desktop: Income, Planned, Spent and Remaining in one row, then the
// bar. Phones ("KPI on phones"): Remaining large, the bar, then Income, Planned and Spent.
// The numbers come from the overview (decimals, R-MON-01); this component only formats.
@Component({
  selector: 'app-month-summary',
  imports: [MatProgressBarModule],
  template: `
    @let l = line();
    <!-- No Income while Tracker is filtered to a category (income is null then). -->
    <div class="summary" [class.no-income]="month().income === null">
      <div class="kpi remaining">
        <span class="caption">Remaining</span>
        <span class="money value" [class]="l.status">{{ money(month().remaining) }}</span>
      </div>
      @if (month().income !== null) {
        <div class="kpi">
          <span class="caption">Income</span>
          <span class="money value">{{ money(month().income!) }}</span>
        </div>
      }
      <div class="kpi">
        <span class="caption">Planned</span>
        <span class="money value">{{ money(month().planned) }}</span>
      </div>
      <div class="kpi">
        <span class="caption">Spent</span>
        <span class="money value">{{ money(month().spent) }}</span>
      </div>
      <div class="bar-line">
        @if (l.bar !== null) {
          <mat-progress-bar
            mode="determinate"
            [class]="l.status"
            [value]="l.bar"
            [attr.aria-label]="'Share of the plan spent: ' + l.chip"
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
  readonly month = input.required<Totals>();
  readonly currency = input.required<string>();

  protected readonly line = computed(() =>
    spendLine(toMinor(this.month().planned), toMinor(this.month().spent), this.currency()),
  );

  protected money(amount: number): string {
    return formatMoney(toMinor(amount), this.currency());
  }
}
