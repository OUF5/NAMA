import { loadView, saveView } from './view-choice';

describe('Months view choice (MON-02)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  // Owner note 18: Totals is the first view and the default.
  it('remembers the last choice and starts on Totals', () => {
    expect(loadView()).toBe('totals');
    saveView('year');
    expect(localStorage.getItem('nama.months.view')).toBe('year');
    expect(loadView()).toBe('year');
    saveView('months');
    expect(loadView()).toBe('months');
    saveView('totals');
    expect(loadView()).toBe('totals');
  });

  it('ignores an unknown value', () => {
    localStorage.setItem('nama.months.view', 'grid');
    expect(loadView()).toBe('totals');
  });

  it('falls back to Totals when storage is blocked', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    expect(() => saveView('year')).not.toThrow();
    expect(loadView()).toBe('totals');
  });
});
