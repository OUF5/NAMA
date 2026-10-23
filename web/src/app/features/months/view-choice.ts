// MON-02: the last view choice is remembered on this device. It is a display preference only, so
// one key serves every account. Storage can be missing or blocked, so every call is guarded and the
// page falls back to the Totals view, the first one (owner note 18; Months before).
export const MONTHS_VIEWS = ['totals', 'year', 'months'] as const;

export type MonthsView = (typeof MONTHS_VIEWS)[number];

const KEY = 'nama.months.view';

export function isMonthsView(value: string | null): value is MonthsView {
  return MONTHS_VIEWS.some((view) => view === value);
}

export function loadView(): MonthsView {
  try {
    const view = localStorage.getItem(KEY);
    return isMonthsView(view) ? view : 'totals';
  } catch {
    return 'totals';
  }
}

export function saveView(view: MonthsView): void {
  try {
    localStorage.setItem(KEY, view);
  } catch {
    // Without storage the toggle still works; only the memory is lost.
  }
}
