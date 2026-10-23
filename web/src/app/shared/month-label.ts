// Formatting only: the months themselves come from the API (FiscalCalendar). Dates are YYYY-MM-DD.
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

// FY start month pickers (the wizard's step 2 and Settings, STG-02).
export const MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

// R-CAL-11 (owner note 19): a month is named by the month it ends in (the default) or by the
// month the salary arrives. The wizard's step 2 and Settings (STG-02).
export type MonthNaming = 'end' | 'payday';

export const MONTH_NAMINGS: { value: MonthNaming; label: string; hint: string }[] = [
  { value: 'end', label: 'The month it ends in', hint: 'Paid on 25 Oct, the month is November: the salary pays for the month ahead.' },
  { value: 'payday', label: 'The month the salary arrives', hint: 'Paid on 25 Oct, the month is October.' },
];

export interface PeriodInfo {
  key: string;
  label: string;
  startDate: string;
  endDate: string;
}

// "27 Sep"
export function formatDay(date: string): string {
  const [, month, day] = date.split('-').map(Number);
  return `${day} ${MONTHS[month - 1]}`;
}

// "27 Sep 2026"
export function formatDate(date: string): string {
  return `${formatDay(date)} ${date.slice(0, 4)}`;
}

// A YYYY-MM month key (the month it starts in) as its name: "2026-03" → "Mar 2026", or "Apr 2026"
// with offset 1. The offset is /api/me's monthKeyOffset (R-CAL-11, from FiscalCalendar). For income
// months that may lie outside every stored FY.
export function monthKeyLabel(key: string, offset: number): string {
  const [year, month] = key.split('-').map(Number);
  const index = year * 12 + month - 1 + offset;
  return `${MONTHS[index % 12]} ${Math.floor(index / 12)}`;
}

// R-CAL-03: "Sep 2026 · 27 Sep to 26 Oct"
export function periodWithDates(period: PeriodInfo): string {
  return `${period.label} · ${formatDay(period.startDate)} to ${formatDay(period.endDate)}`;
}

// For the date picker: a local Date and a YYYY-MM-DD string, without time zone shifts.
export function toDateString(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

export function fromDateString(date: string): Date {
  const [year, month, day] = date.split('-').map(Number);
  return new Date(year, month - 1, day);
}
