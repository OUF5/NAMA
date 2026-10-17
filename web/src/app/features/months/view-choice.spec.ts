import { loadView, saveView } from './view-choice';

describe('Months view choice (MON-02)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('remembers the last choice and starts on Months', () => {
    expect(loadView()).toBe('months');
    saveView('year');
    expect(localStorage.getItem('nama.months.view')).toBe('year');
    expect(loadView()).toBe('year');
    saveView('months');
    expect(loadView()).toBe('months');
  });

  it('ignores an unknown value', () => {
    localStorage.setItem('nama.months.view', 'grid');
    expect(loadView()).toBe('months');
  });

  it('falls back to Months when storage is blocked', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    expect(() => saveView('year')).not.toThrow();
    expect(loadView()).toBe('months');
  });
});
