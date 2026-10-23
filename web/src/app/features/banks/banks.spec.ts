import { BreakpointObserver } from '@angular/cdk/layout';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { Me, MeService } from '../../core/me';
import { FiscalYear } from '../plan/plan-api';
import { AccountDialog } from './account-dialog';
import { Banks } from './banks';
import { BanksResponse } from './banks-api';
import { SplitDialog } from './split-dialog';
import { TransferDialog } from './transfer-dialog';

// PRD 5.11 Banks. Today is 17 Oct 2026; FY 2026 runs 27 Jan 2026 to 26 Jan 2027 with payday 27.
const periods = ['01', '02', '03', '04', '05', '06', '07', '08', '09', '10', '11', '12'].map((m, i) => ({
  key: `2026-${m}`,
  label: `${['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'][i]} 2026`,
  startDate: `2026-${m}-27`,
  endDate: i === 11 ? '2027-01-26' : `2026-${String(i + 2).padStart(2, '0')}-26`,
}));
const fy2026: FiscalYear = { id: '2026-01', label: 'FY 2026', startDate: '2026-01-27', endDate: '2027-01-26', isTransition: false, periods };

const me: Me = {
  setupCompleted: true,
  isAdmin: false,
  profile: { displayName: 'Owner', email: 'o@example.com', projectName: 'Home', currency: 'SAR', timezone: 'Asia/Riyadh', fiscalStartMonth: 1, paydayDay: 27, monthNaming: 'payday', lastBankAccountId: null },
  currentFyId: '2026-01',
  currentPeriodKey: '2026-09',
  currentPeriod: periods[8],
  today: '2026-10-17',
  firstDate: '2026-01-27',
  lastDate: '2027-01-26',
  monthKeyOffset: 0,
};

// The owner's example (BanksTests): Main 4,100 and Savings 23,900, income 17,000 split 80 / 20.
function banks(changes: Partial<BanksResponse> = {}): BanksResponse {
  return {
    today: '2026-10-17',
    currentPeriodKey: '2026-09',
    accounts: [
      {
        id: 'main', name: 'Main', archived: false, openingBalance: 5000, openingDate: '2026-01-27', share: 80, monthIncome: 13600,
        balance: { opening: 5000, income: 13600, spent: 14000, transfersIn: 0, transfersOut: 500, balance: 4100 },
      },
      { id: 'old', name: 'Old card', archived: true, openingBalance: null, openingDate: null, share: 0, monthIncome: 0, balance: null },
      {
        id: 'savings', name: 'Savings', archived: false, openingBalance: 20000, openingDate: '2026-01-27', share: 20, monthIncome: 3400,
        balance: { opening: 20000, income: 3400, spent: 0, transfersIn: 500, transfersOut: 0, balance: 23900 },
      },
    ],
    total: 28000,
    complete: true,
    split: { fromPeriodKey: '2026-01', shares: { main: 80, savings: 20 } },
    splitHistory: [{ fromPeriodKey: '2026-01', shares: { main: 80, savings: 20 } }],
    fiscalYears: [fy2026],
    income: Object.fromEntries(periods.map((p) => [p.key, 17000])),
    transfers: [
      { id: 't1', fromBankAccountId: 'main', toBankAccountId: 'savings', date: '2026-10-18', amount: 500, note: 'Savings', periodKey: '2026-09', fyId: '2026-01' },
    ],
    ...changes,
  };
}

describe('Banks (PRD 5.11)', () => {
  let http: HttpTestingController;
  let dialogResult: unknown;
  let dialogOpen: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    dialogResult = undefined;
    dialogOpen = vi.fn(() => ({ afterClosed: () => of(dialogResult) }));
    TestBed.configureTestingModule({
      imports: [Banks],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MeService, useValue: { me: signal(me), load: async () => me } },
        { provide: MatDialog, useValue: { open: dialogOpen } },
        { provide: BreakpointObserver, useValue: { isMatched: () => false } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function settle(fixture: ComponentFixture<Banks>): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  }

  // NFR-01: one call.
  async function open(data = banks()): Promise<{ fixture: ComponentFixture<Banks>; root: HTMLElement }> {
    const fixture = TestBed.createComponent(Banks);
    (await vi.waitFor(() => http.expectOne('/api/banks'))).flush(data);
    await settle(fixture);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  // Every text node, joined with spaces (Angular drops the whitespace between sibling elements).
  function text(el: Element | null): string {
    if (!el) {
      return '';
    }
    const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    const parts: string[] = [];
    while (walker.nextNode()) {
      parts.push(walker.currentNode.textContent ?? '');
    }
    return parts.join(' ').replace(/\s+/g, ' ').trim();
  }

  function button(root: HTMLElement, label: string): HTMLButtonElement {
    return Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find(
      (b) => b.getAttribute('aria-label') === label || text(b).endsWith(label),
    )!;
  }

  // BNK-02, R-BAL-03: every account is a card, its balance first and how it adds up under it.
  it('shows the total, each account with its breakdown, the split and the transfers', async () => {
    const { fixture, root } = await open();

    expect(text(root.querySelector('.total'))).toContain('Total balance SAR 28,000 Today, 17 Oct 2026 · 2 accounts');
    expect(root.querySelector('.setup')).toBeNull();

    const cards = Array.from(root.querySelectorAll('.accounts .account'));
    expect(cards.map((card) => text(card.querySelector('h2')))).toEqual(['Main', 'Savings']);
    expect(text(cards[0].querySelector('.kpi'))).toBe('SAR 4,100');
    expect(text(cards[0].querySelector('.kpi + .caption'))).toBe('Gets 80% of income · SAR 13,600 in Sep 2026');
    expect(Array.from(cards[0].querySelectorAll('.breakdown > div')).map(text)).toEqual([
      'Opening on 27 Jan 2026 SAR 5,000',
      '+ Income since SAR 13,600',
      '+ Transfers in SAR 0',
      '− Transfers out SAR 500',
      '− Expenses paid SAR 14,000',
      '= Balance today SAR 4,100',
    ]);
    expect(cards[0].querySelector('a.expenses')?.getAttribute('href')).toBe('/entries?bankAccountId=main');

    // BNK-03
    expect(Array.from(root.querySelectorAll('.split li')).map(text)).toEqual(['Main 80% SAR 13,600', 'Savings 20% SAR 3,400']);
    expect(text(root.querySelector('#split-title'))).toBe('Income split from Jan 2026');

    // BNK-04
    expect(Array.from(root.querySelectorAll('.transfer')).map(text)).toEqual([
      '18 Oct 2026 Main arrow_forward Savings Savings SAR 500 edit',
    ]);

    // Archived accounts fold away, with their balance kept out of the total.
    expect(text(root)).not.toContain('Old card');
    button(root, 'Archived (1)').click();
    await settle(fixture);
    expect(text(root.querySelector('[aria-label="Archived accounts"]'))).toBe('Old card No balance unarchive');
  });

  // BNK-05: what is missing, with the action that fixes it.
  it('says what is missing until the balances are complete', async () => {
    const data = banks({ complete: false, total: 4100, split: null, splitHistory: [] });
    data.accounts[2] = { ...data.accounts[2], openingBalance: null, openingDate: null, balance: null, share: null, monthIncome: null };
    const { root } = await open(data);

    const setup = root.querySelector('.setup')!;
    expect(text(setup.querySelector('h2'))).toBe('Finish setting up your balances');
    expect(Array.from(setup.querySelectorAll('li')).map(text)).toEqual(['Savings Set balance']);
    expect(text(setup)).toContain('Choose which accounts your income goes into');
    expect(text(root.querySelector('.total'))).toContain('· not complete yet');
    const savings = root.querySelectorAll('.accounts .account')[1];
    expect(text(savings)).toContain("No opening balance yet, so NAMA can't show this account's balance.");
  });

  // The API refuses to archive an account that gets a share of income (R-BAL-02); the page says why.
  it('archives with one tap and shows why the API refuses', async () => {
    const { fixture, root } = await open();

    button(root, 'Archive Main').click();
    const put = http.expectOne('/api/bank-accounts/main');
    expect(put.request.body).toEqual({ name: 'Main', archived: true, openingBalance: 5000, openingDate: '2026-01-27' });
    put.flush(
      { errors: { Archived: ['This account gets a share of income. Give its share to another account first.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle(fixture);

    expect(text(root.querySelector('.status'))).toBe('This account gets a share of income. Give its share to another account first.');
  });

  it('adds an account and changes the split in one dialog each, and shows the answer', async () => {
    const { fixture, root } = await open();
    const added = banks();
    added.accounts = [...added.accounts, { ...added.accounts[0], id: 'cash', name: 'Cash', share: 0, monthIncome: 0 }];
    dialogResult = added;

    button(root, 'Add account').click();
    await settle(fixture);

    expect(dialogOpen.mock.calls[0][0]).toBe(AccountDialog);
    expect(dialogOpen.mock.calls[0][1].data).toMatchObject({ account: null, taken: ['Main', 'Old card', 'Savings'], today: '2026-10-17' });
    expect(text(root.querySelector('.status'))).toBe('Added Cash.');
    expect(root.querySelectorAll('.accounts .account').length).toBe(3);

    dialogResult = banks();
    button(root, 'Change split').click();
    await settle(fixture);
    expect(dialogOpen.mock.calls[1][0]).toBe(SplitDialog);
    // The active accounts only, and a change starts in the current month.
    expect(dialogOpen.mock.calls[1][1].data.accounts.map((account: { id: string }) => account.id)).toEqual(['main', 'savings', 'cash']);
    expect(dialogOpen.mock.calls[1][1].data.defaultKey).toBe('2026-09');
    expect(text(root.querySelector('.status'))).toBe('Income split saved.');
  });

  // BNK-04: edit or delete in one dialog; the balances load again.
  it('edits a transfer and loads the balances again', async () => {
    const { fixture, root } = await open();
    dialogResult = 'deleted';

    button(root, 'Edit transfer SAR 500 from Main to Savings, 18 Oct 2026').click();
    (await vi.waitFor(() => http.expectOne('/api/banks'))).flush(banks({ transfers: [] }));
    await settle(fixture);

    expect(dialogOpen.mock.calls[0][0]).toBe(TransferDialog);
    expect(text(root.querySelector('.status'))).toBe('Transfer deleted.');
    expect(text(root)).toContain('No transfers yet.');
  });

  // docs/DESIGN.md: no filled button on the page; each dialog's Save is the one.
  it('has no filled button', async () => {
    const { root } = await open();

    expect(root.querySelectorAll('.mat-mdc-unelevated-button').length).toBe(0);
  });
});
