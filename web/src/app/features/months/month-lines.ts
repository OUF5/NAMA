import { formatAmount, formatMoney, toMinor } from '../../shared/money';
import { PlanStatus, planStatusLabel, spendStatus } from '../../shared/plan-status';
import { Category } from '../plan/plan-api';
import { MonthStatus } from './overview-api';

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
