import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Me, MeService } from '../../core/me';
import { Loans, loanStatus } from './loans';
import { Loan, LoansResponse } from './loans-api';
import { SettleDialog } from './settle-dialog';

const me: Me = {
  setupCompleted: true,
  isAdmin: false,
  profile: {
    displayName: 'Owner',
    email: 'o@example.com',
    projectName: 'Home',
    currency: 'SAR',
    timezone: 'Asia/Riyadh',
    fiscalStartMonth: 1,
    paydayDay: 25,
    monthNaming: 'end',
    lastBankAccountId: 'rajhi',
  },
  currentFyId: '2026-01',
  currentPeriodKey: '2026-09',
  currentPeriod: { key: '2026-09', label: 'Oct 2026', startDate: '2026-09-25', endDate: '2026-10-24' },
  today: '2026-10-23',
  firstDate: '2026-01-25',
  lastDate: '2027-01-24',
  monthKeyOffset: 1,
};

const ahmed: Loan = { id: 'a', name: 'Ahmed', type: 'lent', amount: 1000, date: '2026-10-10', bankAccountId: 'snb', note: 'Rent help', settledOn: null };
const sara: Loan = { id: 's', name: 'Sara', type: 'borrowed', amount: 3000, date: '2025-06-01', bankAccountId: 'rajhi', note: '', settledOn: null };
const omar: Loan = { id: 'o', name: 'Omar', type: 'lent', amount: 500, date: '2026-08-01', bankAccountId: 'snb', note: '', settledOn: '2026-09-15' };

// Two months of FY 2026 named by the month they end in (R-CAL-11): Oct 2026 is in progress, Nov 2026
// is yet to come.
function response(loans: Loan[] = [ahmed, omar, sara]): LoansResponse {
  return {
    today: '2026-10-23',
    loans,
    totals: {
      lent: { open: 1000, openCount: 1, settled: 500, settledCount: 1 },
      borrowed: { open: 3000, openCount: 1, settled: 0, settledCount: 0 },
      net: -2000,
    },
    people: [
      { name: 'Ahmed', lentOpen: 1000, borrowedOpen: 0, openCount: 1 },
      { name: 'Sara', lentOpen: 0, borrowedOpen: 3000, openCount: 1 },
    ],
    accounts: [
      { id: 'rajhi', name: 'Al Rajhi', archived: false, balance: 20000 },
      { id: 'old', name: 'Old', archived: true, balance: 0 },
      { id: 'snb', name: 'SNB', archived: false, balance: 4100 },
    ],
    lastBankAccountId: 'snb',
    complete: true,
    balance: 24100,
    months: [
      { period: { key: '2026-09', label: 'Oct 2026', startDate: '2026-09-25', endDate: '2026-10-24' }, balance: 23900, projected: true },
      { period: { key: '2026-10', label: 'Nov 2026', startDate: '2026-10-25', endDate: '2026-11-24' }, balance: 25400, projected: true },
    ],
  };
}

interface Page {
  form: { patchValue(value: object): void };
  save(): Promise<void>;
}

describe('Loans (PRD 5.12)', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'loans', component: Loans }]),
        { provide: MeService, useValue: { me: signal(me), load: async () => me } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function settle(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  async function open(url = '/loans', data = response()): Promise<Page> {
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    (await vi.waitFor(() => http.expectOne('/api/loans'))).flush(data);
    await settle();
    return harness.routeDebugElement!.componentInstance as Page;
  }

  function root(): HTMLElement {
    return harness.routeNativeElement!;
  }

  // Every text node of the first match, joined with spaces (Angular drops the whitespace between
  // sibling elements).
  function text(selector: string): string {
    const el = root().querySelector(selector);
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

  // LON-02: today and the last used account; the chosen type's totals and how the loan changes them.
  it('shows the totals and the account balance today while typing', async () => {
    const page = await open();

    expect(text('.info')).toBe('Information only: your balances, plan and Tracker stay the same.');
    expect(text('.totals')).toBe('Lent · open (1) SAR 1,000 Borrowed SAR 3,000 Lent − Borrowed SAR -2,000');
    expect(text('.outlook')).toContain('SNB today SAR 4,100');

    page.form.patchValue({ name: 'Ahmed', amount: '1025' });
    await settle();
    // arrow_forward is the arrow icon between the total now and after.
    expect(text('.totals')).toBe(
      'Lent · open (1) SAR 1,000 arrow_forward SAR 2,025 Borrowed SAR 3,000 Lent − Borrowed SAR -2,000 arrow_forward SAR -975',
    );
    expect(text('.outlook')).toContain('SAR 1,025 is 25% of it.');
    expect(text('.outlook')).toContain("NAMA's balances do not change.");

    page.form.patchValue({ type: 'borrowed' });
    await settle();
    expect(text('.totals')).toBe(
      'Borrowed · open (1) SAR 3,000 arrow_forward SAR 4,025 Lent SAR 1,000 Lent − Borrowed SAR -2,000 arrow_forward SAR -3,025',
    );
  });

  // LON-02: a date in a month yet to come shows the projected total at that month's end; later, none.
  it('shows the projected balance for a month yet to come', async () => {
    const page = await open();

    page.form.patchValue({ date: new Date(2026, 10, 2) });
    await settle();
    expect(text('.outlook')).toContain('Total balance projected at the end of Nov 2026 (24 Nov 2026) SAR 25,400');

    page.form.patchValue({ date: new Date(2027, 5, 1) });
    await settle();
    expect(text('.outlook')).toContain('No projection for that month yet.');

    // Before the current FY: what NAMA knows is today's balance.
    page.form.patchValue({ date: new Date(2024, 0, 1) });
    await settle();
    expect(text('.outlook')).toContain('SNB today SAR 4,100');
  });

  it('saves a loan and clears the name and amount', async () => {
    const page = await open();
    page.form.patchValue({ name: '  Ahmed ', amount: '250.5', note: ' Taxi ' });

    void page.save();
    const post = await vi.waitFor(() => http.expectOne('/api/loans'));
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ name: 'Ahmed', type: 'lent', amount: 250.5, date: '2026-10-23', bankAccountId: 'snb', note: 'Taxi' });
    post.flush(response());
    await settle();

    expect(text('.status')).toBe('Added Lent · Ahmed · SAR 250.50 · from SNB');
    expect((root().querySelector('input[formcontrolname="name"]') as HTMLInputElement).value).toBe('');
  });

  // LON-03: open loans by default, By person, the table and the totals of the shown rows.
  it('lists the loans with their status and totals', async () => {
    await open('/loans?tab=all');

    expect(text('.summary')).toBe('2 loans Lent SAR 1,000 Borrowed SAR 3,000 Lent − Borrowed SAR -2,000');
    expect(root().querySelector('.summary .danger')?.textContent).toBe('SAR -2,000');
    expect(text('table.people tbody')).toBe('Ahmed SAR 1,000 – 1 Sara – SAR 3,000 1');
    const rows = Array.from(root().querySelectorAll('table.loans tbody tr')).map((tr) =>
      Array.from(tr.querySelectorAll('td')).slice(0, 7).map((td) => td.textContent?.trim()),
    );
    expect(rows).toEqual([
      ['10 Oct 2026', 'Ahmed', 'Lent', 'SAR 1,000', 'SNB', 'Open · 13 days', 'Rent help'],
      ['1 Jun 2025', 'Sara', 'Borrowed', 'SAR 3,000', 'Al Rajhi', 'Open · 509 days', ''],
    ]);
    expect(root().querySelector('table.loans .chip.ok')).toBeNull();
    expect(Array.from(root().querySelectorAll('table.loans .actions-wrap button.mat-mdc-button')).map((b) => b.textContent?.trim())).toEqual([
      'Mark returned',
      'Mark paid',
    ]);
  });

  // R-LON-03: a settled loan can be opened again, without a dialog.
  it('opens a settled loan again', async () => {
    await open('/loans?tab=all');
    const status = root().querySelector('mat-select') as HTMLElement;
    expect(status).not.toBeNull();
    (harness.routeDebugElement!.componentInstance as { statusFilter: { set(value: string): void } }).statusFilter.set('settled');
    await settle();
    expect(text('table.loans .chip.ok')).toBe('Returned 15 Sep 2026');
    // No loan in the list has a note now, so there is no Note column.
    expect(text('table.loans thead')).toBe('Date Name Type Amount Bank account Status');

    (root().querySelector('table.loans .actions-wrap button.mat-mdc-button') as HTMLButtonElement).click();
    const put = await vi.waitFor(() => http.expectOne('/api/loans/o/settled'));
    expect(put.request.body).toEqual({ settledOn: null });
    put.flush(response([ahmed, { ...omar, settledOn: null }, sara]));
    await settle();
    expect(text('.status')).toBe('Open again: Omar · SAR 500.');
  });

  it('says how to start when there is no loan', async () => {
    await open('/loans?tab=all', { ...response([]), people: [] });

    expect(text('.empty')).toBe('No loans yet Add one on New loan .');
  });
});

// LON-03 status words.
describe('loanStatus', () => {
  it('counts the days of an open loan, and says when it starts or was settled', () => {
    expect(loanStatus(ahmed, '2026-10-23')).toBe('Open · 13 days');
    expect(loanStatus({ ...ahmed, date: '2026-10-22' }, '2026-10-23')).toBe('Open · 1 day');
    expect(loanStatus({ ...ahmed, date: '2026-10-23' }, '2026-10-23')).toBe('Open · since today');
    expect(loanStatus({ ...ahmed, date: '2026-12-05' }, '2026-10-23')).toBe('Starts 5 Dec 2026');
    expect(loanStatus(omar, '2026-10-23')).toBe('Returned 15 Sep 2026');
    expect(loanStatus({ ...sara, settledOn: '2026-10-01' }, '2026-10-23')).toBe('Paid 1 Oct 2026');
  });
});

// R-LON-03: in full, on a date on or after the loan's (default today).
describe('SettleDialog', () => {
  it('marks a lent loan returned on the chosen date', async () => {
    const close = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { loan: ahmed, today: '2026-10-23', currency: 'SAR' } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(SettleDialog);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('h2')?.textContent?.trim()).toBe('Mark returned');
    expect(el.textContent).toContain('Ahmed returned SAR 1,000 on');
    (el.querySelector('button[type="submit"]') as HTMLButtonElement).click();

    const put = http.expectOne('/api/loans/a/settled');
    expect(put.request.body).toEqual({ settledOn: '2026-10-23' });
    put.flush(response());
    await vi.waitFor(() => expect(close).toHaveBeenCalled());
    http.verify();
  });
});
