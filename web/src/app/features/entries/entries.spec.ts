import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatFormFieldHarness } from '@angular/material/form-field/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Me, MeService } from '../../core/me';
import { Category, FiscalYear } from '../plan/plan-api';
import { EntriesResponse, Entry } from '../add-entry/entry-api';
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
  profile: { displayName: 'Owner', email: 'o@example.com', projectName: 'Home', currency: 'SAR', timezone: 'Asia/Riyadh', fiscalStartMonth: 1, paydayDay: 27, lastBankAccountId: 'rajhi' },
  currentFyId: '2026-01',
  currentPeriodKey: '2026-09',
  currentPeriod: sep,
  today: '2026-10-17',
  firstDate: '2026-01-27',
  lastDate: '2027-01-26',
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

function list(entries: Entry[], total: number, fiscalYears = [fy2026]): EntriesResponse {
  return {
    fiscalYears,
    fyId: '2026-01',
    bankAccounts: [
      { id: 'rajhi', name: 'Al Rajhi', archived: false },
      { id: 'snb', name: 'SNB', archived: false },
    ],
    entries,
    total,
  };
}

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
      period: sep, planned: 1500, spent: 1199.99, remaining: 300.01, percent: 79,
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
      ['17 Oct 2026', 'Sep 2026', 'Food', 'Groceries', 'SAR 799.99', 'Al Rajhi', 'Big shop'],
      ['10 Sep 2026', 'Aug 2026', 'Transport', 'Fuel', 'SAR 400', 'SNB', ''],
    ]);
    expect(text('.summary p')).toBe('2 entries · Total SAR 1,199.99');
    // One FY: no selector, the FY as a caption.
    expect(text('.fy')).toBe('FY 2026 · 27 Jan 2026 to 26 Jan 2027');
    expect(root().textContent).not.toContain('Fiscal year');
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
    expect(text('.summary p')).toBe('1 entry · Total SAR 799.99');
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
    expect(text('.summary p')).toBe('2 entries · Total SAR 1,150');
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
    expect(text('.summary p')).toBe('1 entry · Total SAR 400');
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
      period: sep, planned: 1500, spent: 1199.99, remaining: 300.01, percent: 79,
    });
    await settle();

    expect(dialog()!.textContent).toContain('Edit entry');
    http.expectNone('/api/entries/e2');
  });
});
