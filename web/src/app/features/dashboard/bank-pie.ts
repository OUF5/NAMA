import { Component, computed, input } from '@angular/core';
import { SummaryAccount } from '../../core/summary';
import { formatMoney, toMinor } from '../../shared/money';

const RADIUS = 96;
const SLOTS = 5;

// The point on the circle at a share of the way round, clockwise from 12 o'clock (2 decimals).
function point(share: number): string {
  const angle = 2 * Math.PI * share - Math.PI / 2;
  const round = (n: number) => Math.round(n * 100) / 100;
  return `${round(100 + RADIUS * Math.cos(angle))} ${round(100 + RADIUS * Math.sin(angle))}`;
}

// DSH-04: the active accounts' balances as a pie, in plain SVG (no chart library). Slices are
// wedges with a 2px --surface edge, so neighbours are always apart (docs/DESIGN.md "Pie (DSH-04)").
// Each account keeps its color by its place in the list (by name, as the API sends it), never by its
// size; from the 6th account on, the 5th slot and later fold into "Other". Accounts below 0 cannot be
// slices, so they are listed under it. Every value is also in the legend (UX-07: never color alone).
// The angles are display ratios only; every amount comes from the API.
@Component({
  selector: 'app-bank-pie',
  template: `
    <div class="pie-row">
      <svg viewBox="0 0 200 200" class="pie" role="img" [attr.aria-label]="ariaLabel()">
        @for (slice of slices(); track slice.key) {
          @if (slice.path) {
            <path class="slice" [attr.d]="slice.path" [style.fill]="slice.color">
              <title>{{ slice.name }}: {{ slice.amount }} ({{ slice.percent }})</title>
            </path>
          } @else {
            <circle class="slice" cx="100" cy="100" [attr.r]="radius" [style.fill]="slice.color">
              <title>{{ slice.name }}: {{ slice.amount }} ({{ slice.percent }})</title>
            </circle>
          }
        }
      </svg>
      <ul class="legend">
        @for (slice of slices(); track slice.key) {
          <li>
            <span class="key" [style.background]="slice.color" aria-hidden="true"></span>
            <span class="name">{{ slice.name }}</span>
            <span class="percent caption">{{ slice.percent }}</span>
            <span class="money amount">{{ slice.amount }}</span>
          </li>
        }
      </ul>
    </div>
    @if (outside().length > 0) {
      <p class="caption outside-title">Not in the chart</p>
      <ul class="legend outside">
        @for (account of outside(); track account.id) {
          <li>
            <span class="name">{{ account.name }}</span>
            <span class="money amount" [class.danger]="account.negative" [class.caption]="!account.negative">
              {{ account.text }}
            </span>
          </li>
        }
      </ul>
    }
  `,
  styleUrl: './bank-pie.scss',
})
export class BankPie {
  readonly accounts = input.required<SummaryAccount[]>();
  readonly currency = input.required<string>();

  protected readonly radius = RADIUS;

  // Accounts above 0, each with its slot; "Other" holds the 5th and later when there are more than 5.
  private readonly parts = computed(() => {
    const accounts = this.accounts();
    const fold = accounts.length > SLOTS;
    const parts: { key: string; name: string; minor: number; color: string }[] = [];
    accounts.forEach((account, i) => {
      const minor = account.balance === null ? 0 : toMinor(account.balance);
      if (minor <= 0) {
        return;
      }
      if (fold && i >= SLOTS - 1) {
        const other = parts.find((part) => part.key === 'other');
        if (other) {
          other.minor += minor;
        } else {
          parts.push({ key: 'other', name: 'Other', minor, color: 'var(--series-other)' });
        }
        return;
      }
      parts.push({ key: account.id, name: account.name, minor, color: `var(--series-${i + 1})` });
    });
    return parts;
  });

  protected readonly slices = computed(() => {
    const parts = this.parts();
    const sum = parts.reduce((total, part) => total + part.minor, 0);
    let start = 0;
    return parts.map((part) => {
      const share = sum > 0 ? part.minor / sum : 0;
      const end = start + share;
      // One slice is the whole circle (an arc cannot end where it starts).
      const path =
        parts.length === 1
          ? null
          : `M 100 100 L ${point(start)} A ${RADIUS} ${RADIUS} 0 ${share > 0.5 ? 1 : 0} 1 ${point(end)} Z`;
      start = end;
      return {
        key: part.key,
        name: part.name,
        color: part.color,
        amount: formatMoney(part.minor, this.currency()),
        percent: `${Math.round(share * 100)}%`,
        path,
      };
    });
  });

  protected readonly outside = computed(() =>
    this.accounts()
      .filter((account) => account.balance === null || toMinor(account.balance) <= 0)
      .map((account) => ({
        id: account.id,
        name: account.name,
        negative: account.balance !== null && account.balance < 0,
        text: account.balance === null ? 'No balance yet' : formatMoney(toMinor(account.balance), this.currency()),
      })),
  );

  protected readonly ariaLabel = computed(
    () =>
      'Balances by account: ' +
      this.slices()
        .map((slice) => `${slice.name} ${slice.amount}, ${slice.percent}`)
        .join('; '),
  );
}
