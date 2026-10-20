import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { AccountDialog, AccountDialogData } from './account-dialog';
import { Account } from './banks-api';
import { SplitDialog, SplitDialogData } from './split-dialog';

const main: Account = {
  id: 'main', name: 'Main', archived: false, openingBalance: 5000, openingDate: '2026-01-27', share: 80, monthIncome: 13600,
  balance: { opening: 5000, income: 13600, spent: 14000, transfersIn: 0, transfersOut: 500, balance: 4100 },
};
const savings: Account = { ...main, id: 'savings', name: 'Savings', share: 20, monthIncome: 3400 };

describe('Banks dialogs (BNK-02, BNK-03)', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  function configure(data: unknown): void {
    close.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  }

  afterEach(() => http.verify());

  async function settle<T>(fixture: ComponentFixture<T>): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  }

  function input(root: HTMLElement, id: string): HTMLInputElement {
    return root.querySelector<HTMLInputElement>(`#${id}`)!;
  }

  function type(el: HTMLInputElement, value: string): void {
    el.value = value;
    el.dispatchEvent(new Event('input'));
  }

  function inputLabeled(root: HTMLElement, label: string): HTMLInputElement {
    return Array.from(root.querySelectorAll('input')).find(
      (el) => el.closest('mat-form-field')?.querySelector('mat-label')?.textContent?.trim() === label,
    )!;
  }

  // R-BAL-02: each share shows what it gives in the chosen month (the API's rounding), and Save waits
  // until they add up to 100%.
  describe('the split dialog', () => {
    const data: SplitDialogData = {
      accounts: [main, savings],
      split: { fromPeriodKey: '2026-01', shares: { main: 80, savings: 20 } },
      groups: [{ label: 'FY 2026', months: [{ key: '2026-09', text: 'Sep 2026 · 27 Sep to 26 Oct' }] }],
      defaultKey: '2026-09',
      income: { '2026-09': 17000.01 },
      currency: 'SAR',
    };

    it('previews each amount and saves the shares in percent', async () => {
      configure(data);
      const fixture = TestBed.createComponent(SplitDialog);
      await settle(fixture);
      const root = fixture.nativeElement as HTMLElement;

      expect(root.querySelector('.income')?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Income in Sep 2026: SAR 17,000.01');
      // 1,700,001 × 80% rounds down to 1,360,000; the 1 left goes to the largest share.
      expect(Array.from(root.querySelectorAll('.amount')).map((el) => el.textContent?.trim())).toEqual(['SAR 13,600.01', 'SAR 3,400']);

      type(input(root, 'share-main'), '66.67');
      type(input(root, 'share-savings'), '33.33');
      await settle(fixture);
      (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();

      const put = await vi.waitFor(() => http.expectOne('/api/income-split'));
      expect(put.request.method).toBe('PUT');
      expect(put.request.body).toEqual({ effectiveFromPeriodKey: '2026-09', shares: { main: 66.67, savings: 33.33 } });
      put.flush({ ok: true });
      await settle(fixture);
      expect(close).toHaveBeenCalledWith({ ok: true });
    });

    it('does not save shares that do not add up to 100%', async () => {
      configure(data);
      const fixture = TestBed.createComponent(SplitDialog);
      await settle(fixture);
      const root = fixture.nativeElement as HTMLElement;

      type(input(root, 'share-savings'), '19.99');
      await settle(fixture);
      expect(root.querySelector('.total')?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Total 99.99% · The shares add up to 100%.');
      expect(root.querySelector('.amount')?.textContent?.trim()).toBe('–');
      (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();
      await settle(fixture);

      expect(root.textContent).toContain('Make the shares add up to exactly 100%.');
      http.expectNone('/api/income-split');

      // "All here" puts everything in one account.
      (root.querySelector('[aria-label="All income to Savings"]') as HTMLButtonElement).click();
      await settle(fixture);
      expect([input(root, 'share-main').value, input(root, 'share-savings').value]).toEqual(['0', '100']);
    });

    // Without a split yet, all income goes to the first account until it is changed.
    it('starts with all income in the first account when there is no split', async () => {
      configure({ ...data, split: null });
      const fixture = TestBed.createComponent(SplitDialog);
      await settle(fixture);
      const root = fixture.nativeElement as HTMLElement;

      expect([input(root, 'share-main').value, input(root, 'share-savings').value]).toEqual(['100', '0']);
    });
  });

  // R-BAL-01: a balance can be below 0 (a card); the date is a day of a stored FY.
  describe('the account dialog', () => {
    const data: AccountDialogData = {
      account: null,
      taken: ['Main'],
      currency: 'SAR',
      today: '2026-10-17',
      firstDate: '2026-01-27',
      lastDate: '2027-01-26',
    };

    it('adds an account with its balance today', async () => {
      configure(data);
      const fixture = TestBed.createComponent(AccountDialog);
      await settle(fixture);
      const root = fixture.nativeElement as HTMLElement;

      type(inputLabeled(root, 'Account name'), ' Card ');
      type(inputLabeled(root, 'Balance (SAR)'), '-1200.50');
      await settle(fixture);
      (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();

      const post = await vi.waitFor(() => http.expectOne('/api/bank-accounts'));
      expect(post.request.method).toBe('POST');
      expect(post.request.body).toEqual({ name: 'Card', openingBalance: -1200.5, openingDate: '2026-10-17' });
      post.flush({ ok: true });
      await settle(fixture);
      expect(close).toHaveBeenCalledWith({ ok: true });
    });

    it('refuses a name that is taken', async () => {
      configure(data);
      const fixture = TestBed.createComponent(AccountDialog);
      await settle(fixture);
      const root = fixture.nativeElement as HTMLElement;

      type(inputLabeled(root, 'Account name'), 'main');
      type(inputLabeled(root, 'Balance (SAR)'), '0');
      (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();
      await settle(fixture);

      expect(root.textContent).toContain('A bank account with this name already exists.');
      http.expectNone('/api/bank-accounts');
    });

    // Editing keeps the archived flag and sends the whole account.
    it('edits the name and the opening balance', async () => {
      configure({ ...data, account: main, taken: [] });
      const fixture = TestBed.createComponent(AccountDialog);
      await settle(fixture);
      const root = fixture.nativeElement as HTMLElement;
      expect(inputLabeled(root, 'Balance (SAR)').value).toBe('5000');

      type(inputLabeled(root, 'Balance (SAR)'), '5100');
      (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();

      const put = await vi.waitFor(() => http.expectOne('/api/bank-accounts/main'));
      expect(put.request.body).toEqual({ name: 'Main', openingBalance: 5100, openingDate: '2026-01-27', archived: false });
      put.flush({ ok: true });
      await settle(fixture);
    });
  });
});
