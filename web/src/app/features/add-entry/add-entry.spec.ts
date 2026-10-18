import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatFormFieldHarness } from '@angular/material/form-field/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { provideRouter } from '@angular/router';
import { Me, MeService } from '../../core/me';
import { Category } from '../plan/plan-api';
import { AddEntry } from './add-entry';
import { BankAccount, Indicator } from './entry-api';

// Today is 17 Oct 2026; FY 2026 runs 27 Jan 2026 to 26 Jan 2027 with payday 27 (PRD section 3).
const sep = { key: '2026-09', label: 'Sep 2026', startDate: '2026-09-27', endDate: '2026-10-26' };

function meWith(lastBankAccountId: string | null): Me {
  return {
    setupCompleted: true,
    profile: { displayName: 'Owner', email: 'o@example.com', projectName: 'Home', currency: 'SAR', timezone: 'Asia/Riyadh', fiscalStartMonth: 1, paydayDay: 27, lastBankAccountId },
    currentFyId: '2026-01',
    currentPeriodKey: '2026-09',
    currentPeriod: sep,
    today: '2026-10-17',
    firstDate: '2026-01-27',
    lastDate: '2027-01-26',
  };
}

const categories: Category[] = [
  {
    id: 'food', name: 'Food', sortOrder: 0, archived: false,
    subCategories: [
      { id: 'groceries', categoryId: 'food', name: 'Groceries', sortOrder: 0, archived: false },
      { id: 'old', categoryId: 'food', name: 'Old', sortOrder: 1, archived: true },
    ],
  },
  {
    id: 'transport', name: 'Transport', sortOrder: 1, archived: false,
    subCategories: [{ id: 'fuel', categoryId: 'transport', name: 'Fuel', sortOrder: 0, archived: false }],
  },
  // No sub-category yet, so it cannot take an entry.
  { id: 'health', name: 'Health', sortOrder: 2, archived: false, subCategories: [] },
];

// Sorted by name by the API.
const accounts: BankAccount[] = [
  { id: 'rajhi', name: 'Al Rajhi', archived: false },
  { id: 'closed', name: 'Closed', archived: true },
  { id: 'snb', name: 'SNB', archived: false },
];

const indicator: Indicator = { period: sep, planned: 1500, spent: 1199.99, remaining: 300.01, percent: 79 };

describe('Add entry (PRD 5.4)', () => {
  let http: HttpTestingController;
  let meService: { me: ReturnType<typeof signal<Me>>; load: () => Promise<Me>; setLastBankAccount: ReturnType<typeof vi.fn> };

  function configure(me: Me): void {
    meService = { me: signal(me), load: async () => me, setLastBankAccount: vi.fn() };
    TestBed.configureTestingModule({
      imports: [AddEntry],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MeService, useValue: meService },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  }

  // A flushed response resolves the page's awaits one task later.
  async function settle(fixture: ComponentFixture<AddEntry>): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  }

  // NFR-01: the page load is exactly 2 calls; /api/me is already cached.
  async function open(me = meWith(null)): Promise<ComponentFixture<AddEntry>> {
    configure(me);
    const fixture = TestBed.createComponent(AddEntry);
    (await vi.waitFor(() => http.expectOne('/api/categories'))).flush(categories);
    (await vi.waitFor(() => http.expectOne('/api/bank-accounts'))).flush(accounts);
    await settle(fixture);
    http.verify();
    return fixture;
  }

  async function select(fixture: ComponentFixture<AddEntry>, label: string): Promise<MatSelectHarness> {
    const field = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatFormFieldHarness.with({ floatingLabelText: label }));
    return (await field.getControl(MatSelectHarness))!;
  }

  function inputLabeled(root: HTMLElement, label: string): HTMLInputElement {
    return Array.from(root.querySelectorAll('input')).find(
      (el) => el.closest('mat-form-field')?.querySelector('mat-label')?.textContent?.trim() === label,
    )!;
  }

  function type(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  // Chooses Food → Groceries and answers the indicator call.
  async function chooseGroceries(fixture: ComponentFixture<AddEntry>): Promise<void> {
    await (await select(fixture, 'Category')).clickOptions({ text: 'Food' });
    await (await select(fixture, 'Sub-category')).clickOptions({ text: 'Groceries' });
    (await vi.waitFor(() => http.expectOne('/api/indicator?subCategoryId=groceries&date=2026-10-17'))).flush(indicator);
    await settle(fixture);
  }

  it('defaults to the first account by name before any entry, and to today', async () => {
    const fixture = await open();
    const root = fixture.nativeElement as HTMLElement;

    expect(await (await select(fixture, 'Bank account')).getValueText()).toBe('Al Rajhi');
    expect(inputLabeled(root, 'Date').value).toBe(new Date(2026, 9, 17).toLocaleDateString());
    // ENT-03
    expect(inputLabeled(root, 'Amount (SAR)').getAttribute('inputmode')).toBe('decimal');
  });

  // Decision 5
  it('defaults to the last used account', async () => {
    const fixture = await open(meWith('snb'));

    expect(await (await select(fixture, 'Bank account')).getValueText()).toBe('SNB');
  });

  it('offers only active items, and sub-categories of the chosen category', async () => {
    const fixture = await open();

    const category = await select(fixture, 'Category');
    await category.open();
    expect(await Promise.all((await category.getOptions()).map((o) => o.getText()))).toEqual(['Food', 'Transport']);
    await category.clickOptions({ text: 'Food' });

    const sub = await select(fixture, 'Sub-category');
    await sub.open();
    expect(await Promise.all((await sub.getOptions()).map((o) => o.getText()))).toEqual(['Groceries']);

    const account = await select(fixture, 'Bank account');
    await account.open();
    expect(await Promise.all((await account.getOptions()).map((o) => o.getText()))).toEqual(['Al Rajhi', 'SNB']);
  });

  // ENT-01
  it('shows the indicator for the month of the date as soon as a sub-category is chosen', async () => {
    const fixture = await open();
    const root = fixture.nativeElement as HTMLElement;

    await chooseGroceries(fixture);

    expect(root.querySelector('app-planned-indicator')?.textContent).toContain('Sep 2026 · 27 Sep to 26 Oct');
    expect(root.querySelector('app-planned-indicator')?.textContent).toContain('SAR 300.01');

    type(inputLabeled(root, 'Amount (SAR)'), '100');
    await fixture.whenStable();
    expect(root.querySelector('.after')?.textContent?.trim()).toBe('After this entry: SAR 200.01 left');
  });

  // ENT-02
  it('saves, says "Saved" and keeps category, sub-category and account for a quick repeat', async () => {
    const fixture = await open();
    const root = fixture.nativeElement as HTMLElement;
    await chooseGroceries(fixture);
    await (await select(fixture, 'Bank account')).clickOptions({ text: 'SNB' });
    type(inputLabeled(root, 'Amount (SAR)'), '125.50');
    type(inputLabeled(root, 'Note (optional)'), '  Weekly shop ');
    await fixture.whenStable();

    (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    const post = await vi.waitFor(() => http.expectOne('/api/entries'));
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ subCategoryId: 'groceries', bankAccountId: 'snb', date: '2026-10-17', amount: 125.5, note: 'Weekly shop' });
    post.flush({ ...post.request.body, id: 'e1', periodKey: '2026-09', fyId: '2026-01', categoryId: 'food' });
    await settle(fixture);

    expect(root.querySelector('.saved span')?.textContent).toBe('Saved SAR 125.50 to Groceries');
    // The cleared amount is not shown as an error (it was red right after a save).
    expect(root.querySelector('mat-error')).toBeNull();
    expect(meService.setLastBankAccount).toHaveBeenCalledWith('snb');
    expect(inputLabeled(root, 'Amount (SAR)').value).toBe('');
    expect(inputLabeled(root, 'Note (optional)').value).toBe('');
    expect(await (await select(fixture, 'Category')).getValueText()).toBe('Food');
    expect(await (await select(fixture, 'Sub-category')).getValueText()).toBe('Groceries');
    expect(await (await select(fixture, 'Bank account')).getValueText()).toBe('SNB');
    // The indicator reloads with the new entry.
    (await vi.waitFor(() => http.expectOne('/api/indicator?subCategoryId=groceries&date=2026-10-17'))).flush(indicator);

    // "Saved" stays until the next amount is typed.
    type(inputLabeled(root, 'Amount (SAR)'), '5');
    await fixture.whenStable();
    expect(root.querySelector('.saved')?.textContent?.trim()).toBe('');
  });

  it('does not send an invalid amount', async () => {
    const fixture = await open();
    const root = fixture.nativeElement as HTMLElement;
    await chooseGroceries(fixture);
    type(inputLabeled(root, 'Amount (SAR)'), '1.234');
    await fixture.whenStable();

    (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    http.expectNone('/api/entries');
    expect(root.textContent).toContain('More than 0, at most 10,000,000, with up to 2 decimals.');
  });

  // Owner's request (after M9): tapping an empty field and leaving it shows no error; Save does, and
  // so does text typed wrong once the field is left.
  it('shows errors on empty fields only after Save', async () => {
    const fixture = await open();
    const root = fixture.nativeElement as HTMLElement;
    await chooseGroceries(fixture);
    const amount = inputLabeled(root, 'Amount (SAR)');

    amount.dispatchEvent(new Event('focus'));
    amount.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(root.querySelector('mat-error')).toBeNull();

    (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectNone('/api/entries');
    expect(root.querySelector('mat-error')?.textContent?.trim()).toBe(
      'More than 0, at most 10,000,000, with up to 2 decimals.',
    );
  });

  it('shows an error for text typed wrong once the field is left', async () => {
    const fixture = await open();
    const root = fixture.nativeElement as HTMLElement;
    await chooseGroceries(fixture);
    const amount = inputLabeled(root, 'Amount (SAR)');

    type(amount, '1.234');
    amount.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(root.querySelector('mat-error')?.textContent?.trim()).toBe(
      'More than 0, at most 10,000,000, with up to 2 decimals.',
    );
  });

  // Owner's request (after M9): the indicator panel says what it will show until a sub-category is
  // chosen, then the planned indicator takes its place.
  it('shows the plan panel placeholder until a sub-category is chosen', async () => {
    const fixture = await open();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('app-entry-fields')!.classList).toContain('split');
    expect(root.querySelector('.placeholder-title')?.textContent?.trim()).toBe('Plan for the month');
    expect(root.querySelector('app-planned-indicator')).toBeNull();

    await chooseGroceries(fixture);
    fixture.detectChanges();

    expect(root.querySelector('.placeholder')).toBeNull();
    expect(root.querySelector('.side app-planned-indicator')).not.toBeNull();
  });

  // UX-03: without an active bank account the page says where to add or restore one.
  it('links to the bank accounts in Settings when no account is active', async () => {
    configure(meWith(null));
    const fixture = TestBed.createComponent(AddEntry);
    (await vi.waitFor(() => http.expectOne('/api/categories'))).flush(categories);
    (await vi.waitFor(() => http.expectOne('/api/bank-accounts'))).flush([accounts[1]]);
    await settle(fixture);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('.empty h2')?.textContent?.trim()).toBe('No bank account');
    expect(root.querySelector('.empty a')?.getAttribute('href')).toBe('/settings/bank-accounts');
    expect(root.querySelector('form')).toBeNull();
  });

  // PRD 7.5: 422 when the date is outside every FY.
  it('shows the API answer on the date field', async () => {
    const fixture = await open();
    const root = fixture.nativeElement as HTMLElement;
    await chooseGroceries(fixture);
    type(inputLabeled(root, 'Amount (SAR)'), '10');
    await fixture.whenStable();

    (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    (await vi.waitFor(() => http.expectOne('/api/entries'))).flush(
      { errors: { Date: ['Date is outside every fiscal year.'] } },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await settle(fixture);

    expect(root.textContent).toContain('Date is outside every fiscal year.');
    expect(root.querySelector('.saved')?.textContent?.trim()).toBe('');
  });
});
