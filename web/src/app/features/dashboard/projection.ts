import { Component, computed, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { Projection as ProjectionData } from '../../core/summary';
import { formatMoney, toMinor } from '../../shared/money';

// DSH-05, R-PLN-07: the projected balance at the end of the FY, then the lines that make it, read top
// to bottom like a Banks card (BNK-02). The API makes every sum (R-MON-01); this only formats. What
// each line means and the months yet to come fold away under "How?". It is the inside of a card.
@Component({
  selector: 'app-projection',
  imports: [RouterLink, MatButtonModule, MatIconModule],
  template: `
    @if (view(); as v) {
      @if (v.projected) {
        <span class="money hero" [class.danger]="v.negative">{{ v.projected }}</span>
        <p class="caption sub">
          @if (v.negative) {
            <span class="danger">Below 0</span> ·
          }
          if you spend exactly what is planned from now on
        </p>
        <dl class="breakdown">
          <div>
            <dt><span class="op" aria-hidden="true"></span>Balance today</dt>
            <dd class="money">{{ v.balanceToday }}</dd>
          </div>
          <div>
            <dt><span class="op" aria-hidden="true">−</span>Still to spend in {{ monthLabel() }}</dt>
            <dd class="money">{{ v.stillToSpend }}</dd>
          </div>
          <div>
            <dt><span class="op" aria-hidden="true">+</span>Future unallocated</dt>
            <dd class="money" [class.danger]="v.futureNegative">{{ v.future }}</dd>
          </div>
          <div class="result">
            <dt><span class="op" aria-hidden="true">=</span>Projected balance</dt>
            <dd class="money" [class.danger]="v.negative">{{ v.projected }}</dd>
          </div>
        </dl>
        <button mat-button type="button" class="fold" [attr.aria-expanded]="open()" (click)="open.set(!open())">
          <mat-icon class="chevron" [class.open]="open()" aria-hidden="true">chevron_right</mat-icon>
          How?
        </button>
        @if (open()) {
          <p class="caption how">
            Balance today is every active account, including the money you had before NAMA. Still to spend is
            what is left of this month's plan. Future unallocated is income minus planned in each month yet to
            come:
          </p>
          @if (v.months.length > 0) {
            <dl class="breakdown months">
              @for (month of v.months; track month.key) {
                <div>
                  <dt>{{ month.label }}</dt>
                  <dd class="money" [class.danger]="month.negative">{{ month.amount }}</dd>
                </div>
              }
            </dl>
          } @else {
            <p class="caption how">This is the last month of {{ v.fy }}.</p>
          }
        }
      } @else {
        <p class="note">
          Set the opening balance of every account and the income split on
          <a routerLink="/banks">Banks</a> to see the projection.
        </p>
      }
    }
  `,
  styleUrl: './projection.scss',
})
export class Projection {
  readonly projection = input.required<ProjectionData>();
  readonly monthLabel = input.required<string>();
  readonly currency = input.required<string>();

  protected readonly open = signal(false);

  protected readonly view = computed(() => {
    const p = this.projection();
    const money = (amount: number) => formatMoney(toMinor(amount), this.currency());
    return {
      fy: p.fiscalYearLabel,
      projected: p.projectedBalance === null ? null : money(p.projectedBalance),
      negative: (p.projectedBalance ?? 0) < 0,
      balanceToday: p.balanceToday === null ? null : money(p.balanceToday),
      stillToSpend: money(p.stillToSpend),
      future: money(p.futureUnallocated),
      futureNegative: p.futureUnallocated < 0,
      months: p.futureMonths.map((month) => ({
        key: month.period.key,
        label: month.period.label,
        amount: money(month.unallocated),
        negative: month.unallocated < 0,
      })),
    };
  });
}
