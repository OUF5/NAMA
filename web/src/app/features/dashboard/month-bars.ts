import { Component, computed, input, signal } from '@angular/core';
import { formatAmount, formatMoney, toMinor } from '../../shared/money';
import { STATUS_LABELS, availableMinor, spendLine } from '../months/month-lines';
import { MonthOverview } from '../months/overview-api';

// DSH-03: planned vs spent per month as a column chart in plain CSS (no chart library in Phase 1).
// Each month has a Planned and a Spent column on one scale: the largest planned or spent value of
// any month is the full height. The heights are display ratios only; every number comes from the
// API. Color is never the only signal (UX-07): a month's numbers and status are written out under
// the chart (the month in progress first, any other one on tap), and over-plan months are listed.
@Component({
  selector: 'app-month-bars',
  template: `
    <div class="legend" aria-hidden="true">
      <span><span class="key planned"></span>Planned</span>
      <span><span class="key spent"></span>Spent, in the plan status color</span>
    </div>

    <!-- Phones: 44px per month, so the chart scrolls sideways inside its card; the page never does. -->
    <div class="scroll">
      <div class="chart">
        <div class="axis" aria-hidden="true">
          <span class="money">{{ top() }}</span>
          <span class="money">0</span>
        </div>
        <div class="plot" role="group" aria-label="Planned and spent per month">
          <div class="lines" aria-hidden="true">
            <span></span>
            <span></span>
            <span></span>
          </div>
          @for (row of rows(); track row.key) {
            <button
              type="button"
              class="col"
              [class.now]="row.inProgress"
              [class.upcoming]="row.upcoming"
              [class.selected]="row.key === selected()?.key"
              [attr.aria-pressed]="row.key === selected()?.key"
              [attr.aria-label]="row.ariaLabel"
              (click)="selectedKey.set(row.key)"
            >
              <span class="bars" aria-hidden="true">
                <span class="bar planned" [style.height.%]="row.plannedHeight"></span>
                <span class="bar spent" [class]="row.line.status" [style.height.%]="row.spentHeight"></span>
              </span>
              <span class="month" aria-hidden="true">{{ row.short }}</span>
            </button>
          }
        </div>
      </div>
    </div>

    @if (selected(); as s) {
      <div class="detail" aria-live="polite">
        <div class="detail-head">
          <span class="label">{{ s.label }}</span>
          @if (s.statusText) {
            <span class="caption">{{ s.statusText }}</span>
          }
          @if (s.line.status) {
            <span class="chip" [class]="s.line.status">{{ s.line.chip }}</span>
          }
        </div>
        <p class="caption money">
          Planned {{ s.planned }} · Spent {{ s.spent }}@if (s.carried) { · From last month {{ s.carried }}}
        </p>
      </div>
    }
    @if (over().length > 0) {
      <p class="caption over">Over plan: {{ over().join(', ') }}</p>
    }
  `,
  styleUrl: './month-bars.scss',
})
export class MonthBars {
  readonly months = input.required<MonthOverview[]>();
  readonly currency = input.required<string>();

  protected readonly selectedKey = signal<string | null>(null);

  private readonly scale = computed(() =>
    Math.max(
      0,
      ...this.months().map((month) => Math.max(toMinor(month.planned), toMinor(month.spent))),
    ),
  );

  // The top of the axis is the largest value itself, so the axis shows API numbers only.
  protected readonly top = computed(() => formatAmount(this.scale()));

  protected readonly rows = computed(() => {
    const scale = this.scale();
    const height = (minor: number) => (scale > 0 ? (minor * 100) / scale : 0);
    return this.months().map((month) => {
      const planned = toMinor(month.planned);
      const spent = toMinor(month.spent);
      // R-PLN-05: the status counts what carried in, as on Tracker; the columns are the month's own.
      const line = spendLine(availableMinor(month), spent, this.currency());
      const carried = toMinor(month.carriedIn);
      // Closed months need no label; In progress and Upcoming match the Months cards (MON-01).
      const statusText = month.status === 'closed' ? '' : STATUS_LABELS[month.status];
      const plannedText = formatMoney(planned, this.currency());
      const spentText = formatMoney(spent, this.currency());
      return {
        key: month.period.key,
        label: month.period.label,
        short: month.period.label.slice(0, 3),
        inProgress: month.status === 'inProgress',
        upcoming: month.status === 'yetToCome',
        statusText,
        line,
        planned: plannedText,
        spent: spentText,
        carried: carried === 0 ? '' : formatMoney(carried, this.currency()),
        plannedHeight: height(planned),
        spentHeight: height(spent),
        ariaLabel: [
          month.period.label + (statusText ? `, ${statusText}` : ''),
          `planned ${plannedText}`,
          `spent ${spentText}`,
          ...(line.status ? [line.chip] : []),
        ].join(', '),
      };
    });
  });

  // The tapped month, else the month in progress, else the first one.
  protected readonly selected = computed(() => {
    const rows = this.rows();
    return (
      rows.find((row) => row.key === this.selectedKey()) ??
      rows.find((row) => row.inProgress) ??
      rows[0] ??
      null
    );
  });

  protected readonly over = computed(() =>
    this.rows()
      .filter((row) => row.line.status === 'danger')
      .map((row) => `${row.label} (${row.line.chip})`),
  );
}
