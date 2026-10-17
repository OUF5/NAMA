import { Component, computed, input } from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { formatMoney } from './money';
import { PeriodInfo, periodWithDates } from './month-label';

export type PlanStatus = 'ok' | 'warn' | 'danger';

// ENT-01 colors, in exact minor-unit math: green below 80%, amber from 80% to 100%, red above 100%.
export function planStatus(planned: number, spent: number): PlanStatus {
  if (spent * 100 < planned * 80) {
    return 'ok';
  }
  return spent <= planned ? 'warn' : 'danger';
}

// Months (M7): null when nothing is planned and nothing is spent. Spending without a plan is
// over plan, because a missing plan counts as 0 (R-PLN-01, owner's choice).
export function spendStatus(planned: number, spent: number): PlanStatus | null {
  return planned === 0 && spent === 0 ? null : planStatus(planned, spent);
}

// docs/DESIGN.md chips: always a text label, never color alone.
export function planStatusLabel(status: PlanStatus, planned: number, spent: number, currency: string): string {
  switch (status) {
    case 'ok':
      return 'Within plan';
    case 'warn':
      return 'Near limit';
    default:
      return `Over by ${formatMoney(spent - planned, currency)}`;
  }
}

// ENT-01 planned indicator (PRD 7.3 shared/): planned, spent so far, remaining and a progress bar
// for one sub-category in one month. The numbers come from GET /api/indicator; `pending` is the
// amount being typed, for the "after this entry" preview. All amounts are minor units.
@Component({
  selector: 'app-planned-indicator',
  imports: [MatProgressBarModule],
  templateUrl: './planned-indicator.html',
  styleUrl: './planned-indicator.scss',
})
export class PlannedIndicator {
  readonly period = input.required<PeriodInfo>();
  readonly planned = input.required<number>();
  readonly spent = input.required<number>();
  readonly pending = input(0);
  readonly currency = input.required<string>();

  protected readonly periodText = computed(() => periodWithDates(this.period()));
  protected readonly hasPlan = computed(() => this.planned() > 0);

  // With an amount typed, the bar and the chip show the month after this entry.
  protected readonly total = computed(() => this.spent() + this.pending());
  protected readonly status = computed(() => planStatus(this.planned(), this.total()));
  protected readonly barValue = computed(() => Math.min(100, (this.total() * 100) / this.planned()));

  protected readonly chip = computed(() => planStatusLabel(this.status(), this.planned(), this.total(), this.currency()));

  protected readonly after = computed(() => {
    const left = this.planned() - this.total();
    return left >= 0 ? `After this entry: ${this.money(left)} left` : `After this entry: over by ${this.money(-left)}`;
  });

  protected money(minor: number): string {
    return formatMoney(minor, this.currency());
  }
}
