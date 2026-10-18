import { FiscalYear } from '../plan/plan-api';
import { formatDate, monthKeyLabel, periodWithDates } from '../../shared/month-label';
import { IncomeItem } from './settings-api';

// The month pickers of the income dialogs: the months of the stored FYs, grouped by FY (R-CAL-09).
export interface MonthGroup {
  label: string;
  months: { key: string; text: string }[];
}

export function monthGroups(fiscalYears: FiscalYear[]): MonthGroup[] {
  return fiscalYears.map((fy) => ({
    label: `${fy.label} · ${formatDate(fy.startDate)} to ${formatDate(fy.endDate)}`,
    months: fy.periods.map((period) => ({ key: period.key, text: periodWithDates(period) })),
  }));
}

// R-INC-03: "Sep 2026" (once), "From Mar 2026" (open ended), "Jan 2026 to Feb 2026".
export function incomeMonths(item: IncomeItem): string {
  if (item.toPeriodKey === item.fromPeriodKey) {
    return monthKeyLabel(item.fromPeriodKey);
  }
  return item.toPeriodKey
    ? `${monthKeyLabel(item.fromPeriodKey)} to ${monthKeyLabel(item.toPeriodKey)}`
    : `From ${monthKeyLabel(item.fromPeriodKey)}`;
}

// STG-01: the browser's IANA time zones (Baseline since March 2022). The list holds primary ids
// only, so the saved zone is added when it is an alias. The API checks the zone again.
export function timeZones(current: string): string[] {
  let zones: string[];
  try {
    zones = Intl.supportedValuesOf('timeZone');
  } catch {
    zones = [];
  }
  return zones.includes(current) ? zones : [...zones, current].sort();
}
