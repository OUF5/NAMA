// Formatting only: the months themselves come from the API (FiscalCalendar). Dates are YYYY-MM-DD.
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

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
