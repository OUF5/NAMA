import { AbstractControl, ValidationErrors } from '@angular/forms';
import { monthKeyLabel } from './month-label';

// R-INC-03 helpers for the additional income forms: the wizard's step 3 (PRD 5.2) and the
// Settings income dialog (STG-03).

// A form group with type, from and to. Month keys are YYYY-MM, so string order is calendar order.
export function untilNotBeforeFrom(group: AbstractControl): ValidationErrors | null {
  const { type, from, to } = group.value as { type: string; from: string; to: string };
  return type === 'monthly' && from && to && to < from ? { untilBeforeFrom: true } : null;
}

// "Sep 2026" (once), "From Mar 2026" (open ended), "Jan 2026 to Feb 2026".
export function incomeMonths(item: { fromPeriodKey: string; toPeriodKey: string | null }): string {
  if (item.toPeriodKey === item.fromPeriodKey) {
    return monthKeyLabel(item.fromPeriodKey);
  }
  return item.toPeriodKey
    ? `${monthKeyLabel(item.fromPeriodKey)} to ${monthKeyLabel(item.toPeriodKey)}`
    : `From ${monthKeyLabel(item.fromPeriodKey)}`;
}

// R-INC-04 (owner note 17): what an additional income item counts in.
export type IncomeCounts = 'planned' | 'actual' | 'both';

// The pickers' options, in the PRD's order.
export const INCOME_COUNTS: { value: IncomeCounts; label: string }[] = [
  { value: 'planned', label: 'Planned' },
  { value: 'actual', label: 'Actual' },
  { value: 'both', label: 'Both' },
];

// Under the picker: what the choice does.
export function countsHint(counts: IncomeCounts): string {
  switch (counts) {
    case 'planned':
      return 'Only your plan counts it, not Tracker or your balance. When the money comes, add it as Actual.';
    case 'actual':
      return 'Tracker and your balance count it. Your plan stays the same.';
    default:
      return 'Your plan, Tracker and your balance all count it.';
  }
}

// In lists: "Planned", "Actual", "Planned and actual".
export function countsLabel(counts: IncomeCounts): string {
  return counts === 'both' ? 'Planned and actual' : counts === 'planned' ? 'Planned' : 'Actual';
}
