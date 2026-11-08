import { BreakpointObserver } from '@angular/cdk/layout';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatFormFieldHarness } from '@angular/material/form-field/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { Me, MeService } from '../../core/me';
import { Category, FiscalYear } from '../plan/plan-api';
import { EntriesResponse, Entry, IncomeRow, TransferRow } from '../add-entry/entry-api';
import { Income } from '../settings/settings-api';
import { Entries } from './entries';

// Today is 17 Oct 2026; FY 2026 runs 27 Jan 2026 to 26 Jan 2027 with payday 27 (PRD section 3).
const aug = { key: '2026-08', label: 'Aug 2026', startDate: '2026-08-27', endDate: '2026-09-26' };
const sep = { key: '2026-09', label: 'Sep 2026', startDate: '2026-09-27', endDate: '2026-10-26' };
const fy2026: FiscalYear = { id: '2026-01', label: 'FY 2026', startDate: '2026-01-27', endDate: '2027-01-26', isTransition: false, periods: [aug, sep] };
const fy2027: FiscalYear = {
  id: '2027-01', label: 'FY 2027', startDate: '2027-01-27', endDate: '2028-01-26', isTransition: false,
  periods: [{ key: '2027-01', label: 'Jan 2027', startDate: '2027-01-27', endDate: '2027-02-26' }],
};

const me: Me = {
  setupCompleted: true,
  isAdmin: false,
  profile: { displayName: 'Owner', email: 'o@example.com', projectName: 'Home', currency: 'SAR', timezone: 'Asia/Riyadh', fiscalStartMonth: 1, paydayDay: 27, monthNaming: 'payday', lastBankAccountId: 'rajhi' },
  currentFyId: '2026-01',
  currentPeriodKey: '2026-09',
  currentPeriod: sep,
  today: '2026-10-17',
  firstDate: '2026-01-27',
  lastDate: '2027-01-26',
  monthKeyOffset: 0,
};

const categories: Category[] = [
  {
    id: 'food', name: 'Food', sortOrder: 0, archived: false,
    subCategories: [{ id: 'groceries', categoryId: 'food', name: 'Groceries', sortOrder: 0, archived: false }],
  },
  {
    id: 'transport', name: 'Transport', sortOrder: 1, archived: false,
    subCategories: [{ id: 'fuel', categoryId: 'transport', name: 'Fuel', sortOrder: 0, archived: false }],
  },
];

const big: Entry = { id: 'e2', date: '2026-10-17', periodKey: '2026-09', fyId: '2026-01', categoryId: 'food', subCategoryId: 'groceries', bankAccountId: 'rajhi', amount: 799.99, note: 'Big shop' };
const fuel: Entry = { id: 'e1', date: '2026-09-10', periodKey: '2026-08', fyId: '2026-01', categoryId: 'transport', subCategoryId: 'fuel', bankAccountId: 'snb', amount: 400, note: '' };

// TBL-07 (owner note 33): a transfer of 250 from SNB to Al Rajhi on 5 Oct; a Gift of 500 into Al Rajhi
// in Sep (Once, Actual); a Bonus of 300 in Aug (Monthly, Planned: no account).
const topUp: TransferRow = { id: 't1', date: '2026-10-05', periodKey: '2026-09', fyId: '2026-01', fromBankAccountId: 'snb', toBankAccountId: 'rajhi', amount: 250, note: 'Top up' };
const gift: IncomeRow = { incomeId: 'gift', date: '2026-09-27', periodKey: '2026-09', label: 'Gift', counts: 'actual', bankAccountId: 'rajhi', amount: 500 };
const bonus: IncomeRow = { incomeId: 'bonus', date: '2026-08-27', periodKey: '2026-08', label: 'Bonus', counts: 'planned', bankAccountId: null, amount: 300 };

function list(entries: Entry[], total: number, fiscalYears = [fy2026], transfers: TransferRow[] = [], income: IncomeRow[] = []): EntriesResponse {
  return {
    fiscalYears,
    fyId: '2026-01',
    bankAccounts: [
      { id: 'rajhi', name: 'Al Rajhi', archived: false },
      { id: 'snb', name: 'SNB', archived: false },
    ],
    entries,
    total,
    transfers,
    transferTotal: transfers.reduce((sum, transfer) => sum + transfer.amount, 0),
    income,
    incomeTotal: income.reduce((sum, row) => sum + row.amount, 0),
  };
}

// GET /api/income, for the income dialog: the Bonus is Monthly from Aug to Sep.
const incomeData: Income = {
  fiscalYears: [fy2026],
  fyId: '2026-01',
  salaries: [],
  additional: [
    { id: 'bonus', kind: 'additional', label: 'Bonus', amount: 300, type: 'monthly', fromPeriodKey: '2026-08', toPeriodKey: '2026-09', counts: 'planned', bankAccountId: null },
  ],
  totals: [],
  currentPeriodKey: '2026-09',
  currentTotal: 17000,
  currentPlanned: 17000,
  bankAccounts: [
    { id: 'rajhi', name: 'Al Rajhi', archived: false },
    { id: 'snb', name: 'SNB', archived: false },
  ],
};

describe('Entries (PRD 5.5)', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'entries', component: Entries }]),
        { provide: MeService, useValue: { me: signal(me), load: async () => me } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((el) => (el.innerHTML = '')));

  async function settle(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  function entriesCall(): Promise<TestRequest> {
    return vi.waitFor(() => http.expectOne((req) => req.url === '/api/entries'));
  }

  function params(request: TestRequest): Record<string, string> {
    return Object.fromEntries(request.request.params.keys().map((key) => [key, request.request.params.get(key)!]));
  }

  // NFR-01: the page load is exactly 2 calls; /api/me is already cached.
  async function open(url = '/entries', response = list([big, fuel], 1199.99)): Promise<TestRequest> {
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    (await vi.waitFor(() => http.expectOne('/api/categories'))).flush(categories);
    const request = await entriesCall();
    request.flush(response);
    await settle();
    http.verify();
    return request;
  }

  function root(): HTMLElement {
    return harness.routeNativeElement!;
  }

  function text(selector: string): string {
    return root().querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  }

  async function select(label: string): Promise<MatSelectHarness> {
    const field = await TestbedHarnessEnvironment.loader(harness.fixture).getHarness(MatFormFieldHarness.with({ floatingLabelText: label }));
    return (await field.getControl(MatSelectHarness))!;
  }

  // By its label (an icon inside the button is not part of it) or its aria-label.
  function button(scope: ParentNode, label: string): HTMLButtonElement {
    return Array.from(scope.querySelectorAll('button')).find(
      (el) => el.querySelector('.mdc-button__label')?.textContent?.trim() === label || el.getAttribute('aria-label') === label,
    )!;
  }

  function dialog(): HTMLElement | null {
    return document.querySelector('mat-dialog-container');
  }

  // Opens the edit dialog of the 799.99 entry and answers its indicator call.
  async function openEdit(): Promise<HTMLElement> {
    button(root(), 'Edit Groceries, SAR 799.99, 17 Oct 2026').click();
    (await vi.waitFor(() => http.expectOne('/api/indicator?subCategoryId=groceries&date=2026-10-17'))).flush({
      period: sep, planned: 1500, spent: 1199.99, remaining: 300.01, percent: 79, overspentLastMonth: 0,
    });
    await settle();
    return dialog()!;
  }

  // TBL-01, TBL-04
  it('lists the entries newest first with names, month labels and the total from the API', async () => {
    const request = await open();

    expect(params(request)).toEqual({});
    const rows = Array.from(root().querySelectorAll('tbody tr')).map((row) =>
      Array.from(row.querySelectorAll('td')).map((cell) => cell.textContent?.trim()).slice(0, 7),
    );
    expect(rows).toEqual([
      ['17 Oct 2026', 'Sep 2026', 'Expense', 'Food · Groceries', 'SAR 799.99', 'Al Rajhi', 'Big shop'],
      ['10 Sep 2026', 'Aug 2026', 'Expense', 'Transport · Fuel', 'SAR 400', 'SNB', ''],
    ]);
    expect(text('.summary p')).toBe('2 entries · Spent SAR 1,199.99');
    // One FY: no selector, the FY as a caption.
    expect(text('.fy')).toBe('FY 2026 · 27 Jan 2026 to 26 Jan 2027');
    expect(root().textContent).not.toContain('Fiscal year');
  });

  // TBL-07 (owner note 33): every type in one list, newest first, each with its colored chip and
  // details; the summary gives each type's total.
  it('lists transfers and additional income with the expenses, each with its type', async () => {
    await open('/entries', list([big, fuel], 1199.99, [fy2026], [topUp], [gift, bonus]));

    const rows = Array.from(root().querySelectorAll('tbody tr')).map((row) =>
      Array.from(row.querySelectorAll('td')).map((cell) => cell.textContent?.trim()).slice(0, 7),
    );
    expect(rows).toEqual([
      ['17 Oct 2026', 'Sep 2026', 'Expense', 'Food · Groceries', 'SAR 799.99', 'Al Rajhi', 'Big shop'],
      ['5 Oct 2026', 'Sep 2026', 'Transfer', '–', 'SAR 250', 'SNB → Al Rajhi', 'Top up'],
      ['27 Sep 2026', 'Sep 2026', 'Income', 'Gift · Actual', 'SAR 500', 'Al Rajhi', ''],
      ['10 Sep 2026', 'Aug 2026', 'Expense', 'Transport · Fuel', 'SAR 400', 'SNB', ''],
      ['27 Aug 2026', 'Aug 2026', 'Income', 'Bonus · Planned', 'SAR 300', '–', ''],
    ]);
    expect(Array.from(root().querySelectorAll('tbody .type-chip')).map((chip) => chip.className)).toEqual([
      'type-chip expense', 'type-chip transfer', 'type-chip income', 'type-chip expense', 'type-chip income',
    ]);
    expect(text('.summary p')).toBe('5 entries · Spent SAR 1,199.99 · Moved SAR 250 · Income SAR 800');
  });

  // TBL-07: the type is in the URL; Transfers or Income clears the category, which is off for them.
  it('filters by type and turns the category off for transfers and income', async () => {
    await open('/entries?categoryId=food');

    await (await select('Type')).clickOptions({ text: 'Transfers' });

    expect(TestBed.inject(Router).url).toBe('/entries?type=transfer');
    const request = await entriesCall();
    expect(params(request)).toEqual({ type: 'transfer' });
    request.flush(list([], 0, [fy2026], [topUp]));
    await settle();
    expect(await (await select('Category')).isDisabled()).toBe(true);
    expect(await (await select('Sub-category')).isDisabled()).toBe(true);
    expect(text('.summary p')).toBe('1 entry · Moved SAR 250');
  });

  // TBL-07: a transfer opens Banks' transfer dialog (BNK-04).
  it('edits a transfer in its own dialog and reloads the list', async () => {
    await open('/entries', list([big], 799.99, [fy2026], [topUp]));

    button(root(), 'Edit SNB → Al Rajhi, SAR 250, 5 Oct 2026').click();
    // The dialog's code loads on demand.
    await vi.waitFor(() => expect(dialog()).not.toBeNull());
    await settle();
    expect(dialog()!.textContent).toContain('Edit transfer');
    button(dialog()!, 'Save').click();
    const put = await vi.waitFor(() => http.expectOne('/api/transfers/t1'));
    expect(put.request.body).toEqual({ fromBankAccountId: 'snb', toBankAccountId: 'rajhi', date: '2026-10-05', amount: 250, note: 'Top up' });
    put.flush(topUp);

    (await entriesCall()).flush(list([big], 799.99, [fy2026], [topUp]));
    await settle();
    await vi.waitFor(() => expect(dialog()).toBeNull());
    expect(text('.status')).toBe('Transfer updated.');
  });

  // TBL-07: income opens Settings' income dialog, which edits the whole item; GET /api/income is
  // loaded only then.
  it('edits additional income in the income dialog, loaded when asked', async () => {
    await open('/entries', list([], 0, [fy2026], [], [bonus]));

    button(root(), 'Edit Bonus, SAR 300, 27 Aug 2026').click();
    (await vi.waitFor(() => http.expectOne('/api/income'))).flush(incomeData);
    await vi.waitFor(() => expect(dialog()).not.toBeNull());
    await settle();

    expect(dialog()!.textContent).toContain('Edit income');
    expect(dialog()!.querySelector('.note')?.textContent?.trim()).toBe('Changes apply to every month of this income.');
    button(dialog()!, 'Cancel').click();
    await settle();
    await vi.waitFor(() => expect(dialog()).toBeNull());
    http.verify();
  });

  it('shows the FY selector when there is more than one FY, and a new FY clears the month', async () => {
    await open('/entries?periodKey=2026-09', list([big], 799.99, [fy2026, fy2027]));

    await (await select('Fiscal year')).clickOptions({ text: /FY 2027/ });

    expect(TestBed.inject(Router).url).toBe('/entries?fyId=2027-01');
    expect(params(await entriesCall())).toEqual({ fyId: '2027-01' });
  });

  // MON-04: Months can open Entries filtered by a sub-category and a month.
  it('sends the filters of the URL and shows the category of a sub-category', async () => {
    const request = await open('/entries?fyId=2026-01&periodKey=2026-09&subCategoryId=groceries', list([big], 799.99));

    expect(params(request)).toEqual({ fyId: '2026-01', periodKey: '2026-09', subCategoryId: 'groceries' });
    expect(await (await select('Month')).getValueText()).toBe('Sep 2026 · 27 Sep to 26 Oct');
    expect(await (await select('Category')).getValueText()).toBe('Food');
    expect(await (await select('Sub-category')).getValueText()).toBe('Groceries');
  });

  it('puts a filter change in the URL and makes one call', async () => {
    await open();

    await (await select('Bank account')).clickOptions({ text: 'Al Rajhi' });

    expect(TestBed.inject(Router).url).toBe('/entries?bankAccountId=rajhi');
    const request = await entriesCall();
    expect(params(request)).toEqual({ bankAccountId: 'rajhi' });
    request.flush(list([big], 799.99));
    await settle();
    // Categories are not loaded again.
    http.verify();
    expect(text('.summary p')).toBe('1 entry · Spent SAR 799.99');
  });

  it('searches notes after a pause in typing', async () => {
    await open();
    const input = root().querySelector('input[type="search"]') as HTMLInputElement;

    input.value = 'big';
    input.dispatchEvent(new Event('input'));

    expect(params(await entriesCall())).toEqual({ q: 'big' });
    expect(TestBed.inject(Router).url).toBe('/entries?q=big');
  });

  // UX-03
  it('says what to do when the FY has no entries', async () => {
    await open('/entries', list([], 0));

    expect(text('.empty h2')).toBe('No entries in FY 2026 yet');
    expect(text('.empty .note')).toBe('Every expense, transfer and additional income you add shows up here. Add an entry.');
    expect(root().querySelector('.empty a')?.getAttribute('href')).toBe('/add');
  });

  it('offers to clear filters that match nothing', async () => {
    await open('/entries?bankAccountId=snb&q=zzz', list([], 0));

    expect(text('.empty h2')).toBe('No entries match these filters');
    button(root(), 'Clear filters').click();

    expect(params(await entriesCall())).toEqual({});
    expect(TestBed.inject(Router).url).toBe('/entries');
  });

  // TBL-06: fetched with the token (a request, not a link), with the filters on screen.
  it('exports the filtered entries as CSV', async () => {
    await open('/entries?periodKey=2026-09');
    const createObjectURL = vi.fn(() => 'blob:entries');
    const revokeObjectURL = vi.fn();
    Object.assign(URL, { createObjectURL, revokeObjectURL });
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      expect(this.download).toBe('nama-entries-2026-01.csv');
      expect(this.href).toBe('blob:entries');
    });

    button(root(), 'Export CSV').click();
    const request = await vi.waitFor(() => http.expectOne((req) => req.url === '/api/entries/export.csv'));
    expect(request.request.responseType).toBe('blob');
    expect(params(request)).toEqual({ fyId: '2026-01', periodKey: '2026-09' });
    request.flush(new Blob(['csv']));
    await settle();

    expect(click).toHaveBeenCalledTimes(1);
    await vi.waitFor(() => expect(revokeObjectURL).toHaveBeenCalledWith('blob:entries'));
    click.mockRestore();
  });

  // TBL-02, ENT-04: the dialog's Save sends the edit, and the list and its total reload.
  it('edits an entry in a dialog and reloads the list', async () => {
    await open();
    const edit = await openEdit();
    expect(edit.textContent).toContain('Edit entry');
    const amount = Array.from(edit.querySelectorAll('input')).find(
      (el) => el.closest('mat-form-field')?.querySelector('mat-label')?.textContent?.trim() === 'Amount (SAR)',
    )!;
    expect(amount.value).toBe('799.99');

    amount.value = '750';
    amount.dispatchEvent(new Event('input'));
    button(edit, 'Save').click();
    const put = await vi.waitFor(() => http.expectOne('/api/entries/e2'));
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ subCategoryId: 'groceries', bankAccountId: 'rajhi', date: '2026-10-17', amount: 750, note: 'Big shop' });
    put.flush({ ...big, amount: 750 });

    const reload = await entriesCall();
    reload.flush(list([{ ...big, amount: 750 }, fuel], 1150));
    await settle();
    await vi.waitFor(() => expect(dialog()).toBeNull());
    expect(text('.summary p')).toBe('2 entries · Spent SAR 1,150');
    expect(text('.status')).toBe('Entry updated.');
  });

  // TBL-03, UX-04: the confirmation replaces the form inside the same dialog.
  it('deletes an entry after a confirmation in the same dialog', async () => {
    await open();
    const edit = await openEdit();

    button(edit, 'Delete').click();
    await settle();

    expect(document.querySelectorAll('mat-dialog-container').length).toBe(1);
    expect(dialog()!.textContent).toContain('Delete this entry?');
    expect(dialog()!.textContent).toContain('SAR 799.99 · Groceries');
    expect(dialog()!.textContent).toContain('17 Oct 2026 · Al Rajhi');
    http.expectNone('/api/entries/e2');

    // The confirmation's Delete is the only one left in the dialog.
    button(dialog()!, 'Delete').click();
    const remove = await vi.waitFor(() => http.expectOne('/api/entries/e2'));
    expect(remove.request.method).toBe('DELETE');
    remove.flush(null, { status: 204, statusText: 'No Content' });

    (await entriesCall()).flush(list([fuel], 400));
    await settle();
    await vi.waitFor(() => expect(dialog()).toBeNull());
    expect(text('.summary p')).toBe('1 entry · Spent SAR 400');
    expect(text('.status')).toBe('Entry deleted.');
  });

  it('leaves the entry alone when the confirmation is cancelled', async () => {
    await open();
    const edit = await openEdit();

    button(edit, 'Delete').click();
    await settle();
    button(dialog()!, 'Cancel').click();
    await settle();
    // Back on the form; its indicator loads again.
    (await vi.waitFor(() => http.expectOne('/api/indicator?subCategoryId=groceries&date=2026-10-17'))).flush({
      period: sep, planned: 1500, spent: 1199.99, remaining: 300.01, percent: 79, overspentLastMonth: 0,
    });
    await settle();

    expect(dialog()!.textContent).toContain('Edit entry');
    http.expectNone('/api/entries/e2');
  });
});

// TBL-05, TBL-07: on phones one card per row, the type chip first and no name said twice.
describe('Entries on phones (PRD 5.5)', () => {
  it('shows one card per row with its type', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'entries', component: Entries }]),
        { provide: MeService, useValue: { me: signal(me), load: async () => me } },
        { provide: BreakpointObserver, useValue: { observe: () => of({ matches: true, breakpoints: {} }), isMatched: () => true } },
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/entries');
    (await vi.waitFor(() => http.expectOne('/api/categories'))).flush(categories);
    (await vi.waitFor(() => http.expectOne((req) => req.url === '/api/entries'))).flush(list([big], 799.99, [fy2026], [topUp], [gift]));
    await new Promise((resolve) => setTimeout(resolve));
    await harness.fixture.whenStable();
    harness.detectChanges();

    const cards = Array.from(harness.routeNativeElement!.querySelectorAll('.entry-card')).map((card) =>
      [card.querySelector('.name'), card.querySelector('.caption')].map((el) => el?.textContent?.replace(/\s+/g, ' ').trim()),
    );
    expect(cards).toEqual([
      ['ExpenseGroceries', 'Food · 17 Oct 2026 · Sep 2026 · Al Rajhi'],
      ['TransferSNB → Al Rajhi', '5 Oct 2026 · Sep 2026'],
      ['IncomeGift', 'Actual · 27 Sep 2026 · Sep 2026 · Al Rajhi'],
    ]);
    http.verify();
  });
});
