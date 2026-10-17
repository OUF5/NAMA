// MON-02: the last view choice is remembered on this device. It is a display preference only, so
// one key serves every account. Storage can be missing or blocked, so every call is guarded and the
// page falls back to the Months view.
export type MonthsView = 'months' | 'year';

const KEY = 'nama.months.view';

export function loadView(): MonthsView {
  try {
    return localStorage.getItem(KEY) === 'year' ? 'year' : 'months';
  } catch {
    return 'months';
  }
}

export function saveView(view: MonthsView): void {
  try {
    localStorage.setItem(KEY, view);
  } catch {
    // Without storage the toggle still works; only the memory is lost.
  }
}
