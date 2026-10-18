// Formatting only: the months themselves come from the API (FiscalCalendar). Dates are YYYY-MM-DD.
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

// FY start month pickers (the wizard's step 2 and Settings, STG-02).
export const MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
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

// A YYYY-MM month key as its label: "2026-03" → "Mar 2026" (R-CAL-03: a month is labeled by the
// month it starts in, which is its key). For income months that may lie outside every stored FY.
export function monthKeyLabel(key: string): string {
  const [year, month] = key.split('-').map(Number);
  return `${MONTHS[month - 1]} ${year}`;
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
