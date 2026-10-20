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
