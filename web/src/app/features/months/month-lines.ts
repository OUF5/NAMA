import { formatAmount, formatMoney } from '../../shared/money';
import { PlanStatus, planStatusLabel, spendStatus } from '../../shared/planned-indicator';
import { Category } from '../plan/plan-api';
import { MonthStatus } from './overview-api';

// MON-01: every status has a text label, never color alone.
export const STATUS_LABELS: Record<MonthStatus, string> = {
  closed: 'Closed',
  inProgress: 'In progress',
  yetToCome: 'Upcoming',
};

// One planned-vs-spent line: a month card's row (docs/DESIGN.md "Category rows on phones") or a
// Year grid cell. status is null when nothing is planned and nothing is spent (M7).
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
