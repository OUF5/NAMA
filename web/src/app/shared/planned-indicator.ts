import { Component, computed, input } from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { formatMoney } from './money';
import { planStatus, planStatusLabel } from './plan-status';
import { PeriodInfo, periodWithDates } from './month-label';

// ENT-01 planned indicator (PRD 7.3 shared/): planned, what carried in from last month (R-PLN-05),
// spent so far, remaining and a progress bar for one sub-category in one month. The numbers come from
// GET /api/indicator; `pending` is the amount being typed, for the "after this entry" preview. All
// amounts are minor units.
@Component({
  selector: 'app-planned-indicator',
  imports: [MatProgressBarModule],
  templateUrl: './planned-indicator.html',
  styleUrl: './planned-indicator.scss',
})
export class PlannedIndicator {
  readonly period = input.required<PeriodInfo>();
  readonly carriedIn = input(0);
  readonly planned = input.required<number>();
  readonly spent = input.required<number>();
  readonly pending = input(0);
  readonly currency = input.required<string>();

  protected readonly periodText = computed(() => periodWithDates(this.period()));
  protected readonly available = computed(() => this.carriedIn() + this.planned());
  // A plan this month, or money (or an overspend) carried in from last month.
  protected readonly hasPlan = computed(() => this.planned() > 0 || this.carriedIn() !== 0);

  // With an amount typed, the bar and the chip show the month after this entry.
  protected readonly total = computed(() => this.spent() + this.pending());
  protected readonly status = computed(() => planStatus(this.available(), this.total()));
  protected readonly barValue = computed(() =>
    this.available() > 0 ? Math.min(100, (this.total() * 100) / this.available()) : 100,
  );

  protected readonly chip = computed(() => planStatusLabel(this.status(), this.available(), this.total(), this.currency()));

  protected readonly after = computed(() => {
    const left = this.available() - this.total();
    return left >= 0 ? `After this entry: ${this.money(left)} left` : `After this entry: over by ${this.money(-left)}`;
  });

  protected money(minor: number): string {
    return formatMoney(minor, this.currency());
  }
}
