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
// Year grid cell. status is null when nothing is planned and nothing is spent (M7). Owner note 32:
// "of" is the month's own plan (what was available, R-PLN-05, before).
export interface SpendLine {
  status: PlanStatus | null;
  chip: string;
  bar: number | null;
  spent: string;
  of: string;
}

// Amounts are minor units: "SAR 1,240" "of 2,000".
export function spendLine(planned: number, spent: number, currency: string): SpendLine {
  const status = spendStatus(planned, spent);
  return {
    status,
    chip: status ? planStatusLabel(status, planned, spent, currency) : '',
    bar: planned > 0 ? Math.min(100, (spent * 100) / planned) : null,
    spent: formatMoney(spent, currency),
    of: `of ${formatAmount(planned)}`,
  };
}

// Owner note 32 (R-PLN-09): how many months back the overspend to make up is counted over, kept in
// the URL as ?back=: 1 (the default), 2, 3, 6, or "all" (since the FY's first month). Never more than
// the FY's months before the month shown (before), so 0 in the FY's first month.
const BACK_CHOICES = [1, 2, 3, 6];

export function monthsBack(back: string, before: number): number {
  const picked = back === 'all' ? before : Number(back);
  return Math.min(Number.isInteger(picked) && picked > 0 ? picked : 1, before);
}

// "last month", "last 3 months" or "since Aug 2026" (all the FY's months before this one).
export function backWords(count: number, before: number, firstLabel: string): string {
  return count <= 1 ? 'last month' : count === before ? `since ${firstLabel}` : `last ${count} months`;
}

// The picker's choices for a month with `before` months before it in its FY.
export function backOptions(before: number, firstLabel: string): { value: string; count: number; label: string }[] {
  const counts = [...BACK_CHOICES.filter((count) => count < before), ...(before > 0 ? [before] : [])];
  return counts.map((count) => {
    const words = backWords(count, before, firstLabel);
    return {
      value: count === before && count > 1 ? 'all' : String(count),
      count,
      label: words[0].toUpperCase() + words.slice(1),
    };
  });
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
