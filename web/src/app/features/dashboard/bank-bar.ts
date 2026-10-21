import { Component, computed, input } from '@angular/core';
import { SummaryAccount } from '../../core/summary';
import { formatMoney, toMinor } from '../../shared/money';

const SLOTS = 5;

// DSH-04 (owner note 13): the active accounts' balances as one thin bar split by account, then every
// account in a compact list (docs/DESIGN.md "Bank bar (DSH-04)"). A pie read badly with 2 slices and
// could not show more than 5 accounts. Each account keeps its color by its place in the list (by
// name, as the API sends it), never by its size; from the 6th account on, the 5th and later share
// the grey "Other" part of the bar, but each is still listed with its own amount and a grey key.
// Accounts below 0 cannot be a part of a whole, so they are listed under it. Every value is in the
// list (UX-07: never color alone). The widths are display ratios only; every amount comes from the API.
@Component({
  selector: 'app-bank-bar',
  template: `
    <div class="bar" role="img" [attr.aria-label]="ariaLabel()">
      @for (part of parts(); track part.key) {
        <span class="part" [style.flex-grow]="part.minor" [style.background]="part.color" [title]="part.title"></span>
      }
    </div>
    <ul class="list">
      @for (row of rows(); track row.id) {
        <li>
          <span class="key" [style.background]="row.color" aria-hidden="true"></span>
          <span class="name">{{ row.name }}</span>
          <span class="percent caption">{{ row.percent }}</span>
          <span class="money amount">{{ row.amount }}</span>
        </li>
      }
    </ul>
    @if (otherNames(); as names) {
      <p class="caption other-note">The grey part of the bar is {{ names }}.</p>
    }
    @if (outside().length > 0) {
      <p class="caption outside-title">Not in the bar</p>
      <ul class="list outside">
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
  styleUrl: './bank-bar.scss',
})
export class BankBar {
  readonly accounts = input.required<SummaryAccount[]>();
  readonly currency = input.required<string>();

  // Accounts above 0 with their color; from the 6th account on, the 5th slot and later are "Other".
  private readonly above = computed(() => {
    const accounts = this.accounts();
    const fold = accounts.length > SLOTS;
    return accounts
      .map((account, i) => ({
        account,
        minor: account.balance === null ? 0 : toMinor(account.balance),
        other: fold && i >= SLOTS - 1,
        color: fold && i >= SLOTS - 1 ? 'var(--series-other)' : `var(--series-${i + 1})`,
      }))
      .filter((item) => item.minor > 0);
  });

  private readonly sum = computed(() => this.above().reduce((total, item) => total + item.minor, 0));

  private percent(minor: number): string {
    const sum = this.sum();
    return `${sum > 0 ? Math.round((minor * 100) / sum) : 0}%`;
  }

  // The bar: one part per colored account, then one grey "Other" part.
  protected readonly parts = computed(() => {
    const parts: { key: string; name: string; minor: number; color: string }[] = [];
    for (const item of this.above()) {
      const other = item.other ? parts.find((part) => part.key === 'other') : undefined;
      if (other) {
        other.minor += item.minor;
      } else {
        parts.push({
          key: item.other ? 'other' : item.account.id,
          name: item.other ? 'Other' : item.account.name,
          minor: item.minor,
          color: item.color,
        });
      }
    }
    return parts.map((part) => ({
      ...part,
      amount: formatMoney(part.minor, this.currency()),
      percent: this.percent(part.minor),
      title: `${part.name}: ${formatMoney(part.minor, this.currency())} (${this.percent(part.minor)})`,
    }));
  });

  // The list: every account above 0 with its own amount, in the bar's order.
  protected readonly rows = computed(() =>
    this.above().map((item) => ({
      id: item.account.id,
      name: item.account.name,
      color: item.color,
      amount: formatMoney(item.minor, this.currency()),
      percent: this.percent(item.minor),
    })),
  );

  // "Inma, STC and Albilad" when some accounts share the grey part; null otherwise.
  protected readonly otherNames = computed(() => {
    const names = this.above().filter((item) => item.other).map((item) => item.account.name);
    return names.length === 0 ? null : names.length === 1 ? names[0] : `${names.slice(0, -1).join(', ')} and ${names.at(-1)}`;
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
      this.parts()
        .map((part) => `${part.name} ${part.amount}, ${part.percent}`)
        .join('; '),
  );
}
