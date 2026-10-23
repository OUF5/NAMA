import { formatAmount, formatMoney, toMinor } from '../../shared/money';
import { PlanStatus, planStatusLabel, spendStatus } from '../../shared/plan-status';
import { Category } from '../plan/plan-api';
import { BalancesResponse, MonthOverview, MonthStatus } from './overview-api';

// MON-01: every status has a text label, never color alone.
export const STATUS_LABELS: Record<MonthStatus, string> = {
  closed: 'Closed',
  inProgress: 'In progress',
  yetToCome: 'Upcoming',
};

// One planned-vs-spent line: a month card's row (docs/DESIGN.md "Category rows on phones") or a
// Year grid cell. status is null when nothing is available and nothing is spent (M7). R-PLN-05:
// "of" is what was available, the month's plan plus what carried in.
export interface SpendLine {
  status: PlanStatus | null;
  chip: string;
  bar: number | null;
  spent: string;
  of: string;
}

// Amounts are minor units: "SAR 1,240" "of 2,000".
export function spendLine(available: number, spent: number, currency: string): SpendLine {
  const status = spendStatus(available, spent);
  return {
    status,
    chip: status ? planStatusLabel(status, available, spent, currency) : '',
    bar: available > 0 ? Math.min(100, (spent * 100) / available) : null,
    spent: formatMoney(spent, currency),
    of: `of ${formatAmount(available)}`,
  };
}

// R-PLN-05: a cell's or a month's plan plus what carried in, in minor units.
export function availableMinor(item: { carriedIn: number; planned: number }): number {
  return toMinor(item.carriedIn) + toMinor(item.planned);
}

// Category and sub-category names by id; archived ones are marked, as in the Entries filters (M6).
export function nameMaps(categories: Category[]): {
  categories: Map<string, string>;
  subs: Map<string, string>;
} {
  const named = (name: string, archived: boolean) => (archived ? `${name} (archived)` : name);
  return {
    categories: new Map(
      categories.map((category) => [category.id, named(category.name, category.archived)]),
    ),
    subs: new Map(
      categories
        .flatMap((category) => category.subCategories)
        .map((sub) => [sub.id, named(sub.name, sub.archived)]),
    ),
  };
}

// Owner note 9: the Balance row of the Year grid and of Totals (owner note 18), one cell per month,
// then the FY's end (its last month). Real for closed months, projected after (R-PLN-07).
export function balanceRow(months: MonthOverview[], balances: BalancesResponse | 'error' | null, currency: string) {
  if (balances === null) {
    return { state: 'loading' as const, months: [], end: null };
  }
  if (balances === 'error' || !balances.complete) {
    return { state: balances === 'error' ? ('error' as const) : ('setup' as const), months: [], end: null };
  }
  const byKey = new Map(balances.months.map((month) => [month.periodKey, month]));
  const cells = months.map((month) => {
    const found = byKey.get(month.period.key);
    const amount = found?.balance ?? null;
    return {
      key: month.period.key,
      amount: amount === null ? null : formatAmount(toMinor(amount)),
      negative: (amount ?? 0) < 0,
      projected: found?.projected ?? false,
      now: month.status === 'inProgress',
      label:
        amount === null
          ? `${month.period.label}: balance not known`
          : `${month.period.label}: ${found?.projected ? 'projected ' : ''}balance at the end ${formatMoney(toMinor(amount), currency)}`,
    };
  });
  return { state: 'ready' as const, months: cells, end: cells.at(-1) ?? null };
}
