import { Component, computed, input } from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { formatMoney } from './money';
import { planStatus, planStatusLabel } from './plan-status';
import { PeriodInfo, periodWithDates } from './month-label';

// ENT-01 planned indicator (PRD 7.3 shared/): planned, spent so far, remaining and a progress bar for
// one sub-category in one month, the month on its own (owner note 32, as Tracker's month). Under it,
// only when last month went over plan, Remaining − Overspent last month (R-PLN-09). The numbers come
// from GET /api/indicator; `pending` is the amount being typed, for the "after this entry" preview. All
// amounts are minor units.
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
  // R-PLN-09: what went over plan last month and is not made up yet.
  readonly overspent = input(0);

  protected readonly periodText = computed(() => periodWithDates(this.period()));
  protected readonly hasPlan = computed(() => this.planned() > 0);
  protected readonly remaining = computed(() => this.planned() - this.spent());
  // As the amount Remaining this month shows, before the amount being typed.
  protected readonly afterOverspent = computed(() => this.remaining() - this.overspent());

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
