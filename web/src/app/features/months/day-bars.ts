import { Component, computed, input } from '@angular/core';
import { formatMoney, toMinor } from '../../shared/money';
import { formatDay } from '../../shared/month-label';
import { DaySpent } from './month-api';

// Owner note 9: a month's spending by day as columns in plain CSS (no chart library, PRD 5.9). One
// series, so no legend: neutral columns on one scale (the biggest day is the full height), today in
// --primary-text, days still to come as an empty track. The heights are display ratios only; every
// amount comes from the API. Each column's title shows its day and amount; the chart's label lists
// every day with spending, so nothing is color or hover only (UX-07).
@Component({
  selector: 'app-day-bars',
  template: `
    <div class="chart" role="img" [attr.aria-label]="ariaLabel()">
      <div class="plot" aria-hidden="true">
        @for (day of columns(); track day.date) {
          <span
            class="col"
            [class.today]="day.today"
            [class.future]="day.future"
            [title]="day.title"
          >
            <span class="bar" [style.height.%]="day.height"></span>
          </span>
        }
      </div>
      <div class="axis" aria-hidden="true">
        <span>{{ first() }}</span>
        @if (todayAt() !== null) {
          <span class="today-label" [style.left.%]="todayAt()">Today</span>
        }
        <span>{{ last() }}</span>
      </div>
    </div>
  `,
  styleUrl: './day-bars.scss',
})
export class DayBars {
  readonly days = input.required<DaySpent[]>();
  readonly today = input<string | null>(null);
  readonly currency = input.required<string>();

  private readonly max = computed(() =>
    Math.max(0, ...this.days().map((day) => toMinor(day.spent))),
  );

  protected readonly columns = computed(() => {
    const max = this.max();
    const today = this.today();
    return this.days().map((day) => {
      const minor = toMinor(day.spent);
      return {
        date: day.date,
        height: max > 0 ? (minor * 100) / max : 0,
        today: day.date === today,
        future: !!today && day.date > today,
        title: `${formatDay(day.date)}: ${formatMoney(minor, this.currency())}`,
      };
    });
  });

  protected readonly first = computed(() =>
    this.days().length ? formatDay(this.days()[0].date) : '',
  );
  protected readonly last = computed(() =>
    this.days().length ? formatDay(this.days().at(-1)!.date) : '',
  );
  // Where today's column is, as a share of the width, for its label.
  protected readonly todayAt = computed(() => {
    const columns = this.columns();
    const i = columns.findIndex((day) => day.today);
    return i < 0 ? null : ((i + 0.5) * 100) / columns.length;
  });

  protected readonly ariaLabel = computed(() => {
    const spent = this.days().filter((day) => day.spent > 0);
    return spent.length === 0
      ? 'Spending by day: nothing spent'
      : 'Spending by day: ' +
          spent
            .map(
              (day) => `${formatDay(day.date)} ${formatMoney(toMinor(day.spent), this.currency())}`,
            )
            .join(', ');
  });
}
