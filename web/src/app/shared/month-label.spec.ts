import { formatDate, fromDateString, periodWithDates, toDateString } from './month-label';

describe('month label and dates', () => {
  it('shows a month with its dates (R-CAL-03)', () => {
    expect(periodWithDates({ key: '2026-09', label: 'Sep 2026', startDate: '2026-09-27', endDate: '2026-10-26' })).toBe(
      'Sep 2026 · 27 Sep to 26 Oct',
    );
    expect(formatDate('2027-01-26')).toBe('26 Jan 2027');
  });

  // The date picker works with local dates; the API takes YYYY-MM-DD.
  it('turns a picked date into YYYY-MM-DD and back without shifting the day', () => {
    expect(toDateString(new Date(2026, 9, 17, 23, 59))).toBe('2026-10-17');
    expect(toDateString(new Date(2026, 0, 1, 0, 0))).toBe('2026-01-01');
    expect(fromDateString('2027-01-26')).toEqual(new Date(2027, 0, 26));
    expect(toDateString(fromDateString('2026-02-28'))).toBe('2026-02-28');
  });
});
