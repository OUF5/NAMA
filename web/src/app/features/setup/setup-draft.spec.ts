import { SetupDraft, clearAllDrafts, clearDraft, loadDraft, saveDraft } from './setup-draft';

describe('setup draft (SET-03)', () => {
  const draft: SetupDraft = {
    projectName: 'Home budget',
    displayName: 'Owner',
    currency: 'SAR',
    fiscalStartMonth: 1,
    paydayDay: 27,
    salary: '15000',
    additional: [{ label: 'Rent', amount: '2000', type: 'monthly', from: '2026-01', to: '' }],
    bankAccounts: ['SNB'],
    categories: ['Food'],
  };

  beforeEach(() => localStorage.clear());
  afterEach(() => vi.restoreAllMocks());

  it('survives a reload, per Google account', () => {
    saveDraft('user-1', draft);
    expect(loadDraft('user-1')).toEqual(draft);
    expect(loadDraft('user-2')).toBeNull();
  });

  it('is removed on Finish', () => {
    saveDraft('user-1', draft);
    clearDraft('user-1');
    expect(loadDraft('user-1')).toBeNull();
  });

  it('is removed for every account on sign-out, other keys stay', () => {
    saveDraft('user-1', draft);
    saveDraft('user-2', draft);
    localStorage.setItem('other', 'kept');
    clearAllDrafts();
    expect(loadDraft('user-1')).toBeNull();
    expect(loadDraft('user-2')).toBeNull();
    expect(localStorage.getItem('other')).toBe('kept');
  });

  it('works without storage', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    expect(() => saveDraft('user-1', draft)).not.toThrow();
    expect(loadDraft('user-1')).toBeNull();
  });
});
