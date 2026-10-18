import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldHarness } from '@angular/material/form-field/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { Category } from '../plan/plan-api';
import { BankAccount, Entry } from '../add-entry/entry-api';
import { EntryDialog, EntryDialogData } from './entry-dialog';

const sep = { key: '2026-09', label: 'Sep 2026', startDate: '2026-09-27', endDate: '2026-10-26' };

// "Old" and "Closed" were archived after the entry was made (archive comes with M8).
const categories: Category[] = [
  {
    id: 'food', name: 'Food', sortOrder: 0, archived: false,
    subCategories: [
      { id: 'groceries', categoryId: 'food', name: 'Groceries', sortOrder: 0, archived: false },
      { id: 'old', categoryId: 'food', name: 'Old', sortOrder: 1, archived: true },
      { id: 'older', categoryId: 'food', name: 'Older', sortOrder: 2, archived: true },
    ],
  },
];

const accounts: BankAccount[] = [
  { id: 'rajhi', name: 'Al Rajhi', archived: false },
  { id: 'closed', name: 'Closed', archived: true },
  { id: 'gone', name: 'Gone', archived: true },
];

const entry: Entry = {
  id: 'e1', date: '2026-10-17', periodKey: '2026-09', fyId: '2026-01', categoryId: 'food',
  subCategoryId: 'old', bankAccountId: 'closed', amount: 1500.5, note: 'Weekly shop',
};

describe('Entry dialog (TBL-02, TBL-03)', () => {
  let http: HttpTestingController;
  let close: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<EntryDialog>;

  beforeEach(async () => {
    close = vi.fn();
    const data: EntryDialogData = { entry, categories, accounts, currency: 'SAR', firstDate: '2026-01-27', lastDate: '2027-01-26' };
    TestBed.configureTestingModule({
      imports: [EntryDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EntryDialog);
    // The indicator shows the month as saved.
    (await vi.waitFor(() => http.expectOne('/api/indicator?subCategoryId=old&date=2026-10-17'))).flush({
      period: sep, planned: 0, spent: 1500.5, remaining: -1500.5, percent: null,
    });
    await settle();
  });

  async function settle(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  async function select(label: string): Promise<MatSelectHarness> {
    const field = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatFormFieldHarness.with({ floatingLabelText: label }));
    return (await field.getControl(MatSelectHarness))!;
  }

  async function options(label: string): Promise<string[]> {
    const control = await select(label);
    await control.open();
    const texts = await Promise.all((await control.getOptions()).map((option) => option.getText()));
    await control.close();
    return texts;
  }

  function button(label: string): HTMLButtonElement {
    return Array.from(root().querySelectorAll('button')).find((el) => el.textContent?.trim() === label)!;
  }

  function inputLabeled(label: string): HTMLInputElement {
    return Array.from(root().querySelectorAll('input')).find(
      (el) => el.closest('mat-form-field')?.querySelector('mat-label')?.textContent?.trim() === label,
    )!;
  }

  it('is filled from the entry and keeps its archived sub-category and account', async () => {
    expect(await (await select('Sub-category')).getValueText()).toBe('Old (archived)');
    expect(await (await select('Bank account')).getValueText()).toBe('Closed (archived)');
    expect(inputLabeled('Amount (SAR)').value).toBe('1500.50');
    expect(inputLabeled('Note (optional)').value).toBe('Weekly shop');
    // Other archived items are still hidden (PRD 4.6).
    expect(await options('Sub-category')).toEqual(['Groceries', 'Old (archived)']);
    expect(await options('Bank account')).toEqual(['Al Rajhi', 'Closed (archived)']);
    // No "after this entry" preview: "spent so far" already includes this entry.
    expect(root().querySelector('.after')).toBeNull();
    // The Add page layout (split) is not used in the dialog: no placeholder panel.
    expect(root().querySelector('app-entry-fields')!.classList).not.toContain('split');
    expect(root().querySelector('.placeholder')).toBeNull();
  });

  // PRD 7.5: 422 when the new date is outside every FY.
  it('shows the API answer on the date field', async () => {
    button('Save').click();
    (await vi.waitFor(() => http.expectOne('/api/entries/e1'))).flush(
      { errors: { Date: ['Date is outside every fiscal year.'] } },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await settle();

    expect(root().textContent).toContain('Date is outside every fiscal year.');
    expect(close).not.toHaveBeenCalled();
  });

  it('does not send an invalid amount', async () => {
    const amount = inputLabeled('Amount (SAR)');
    amount.value = '0';
    amount.dispatchEvent(new Event('input'));

    button('Save').click();
    await settle();

    http.expectNone('/api/entries/e1');
    expect(root().textContent).toContain('More than 0, at most 10,000,000, with up to 2 decimals.');
  });

  it('closes as saved after a save', async () => {
    button('Save').click();
    const put = await vi.waitFor(() => http.expectOne('/api/entries/e1'));
    expect(put.request.body).toEqual({ subCategoryId: 'old', bankAccountId: 'closed', date: '2026-10-17', amount: 1500.5, note: 'Weekly shop' });
    put.flush(entry);
    await settle();

    expect(close).toHaveBeenCalledWith('saved');
  });

  // Deleted elsewhere (another tab): the list reloads without it.
  it('closes as deleted when the entry is already gone', async () => {
    button('Delete').click();
    await settle();
    button('Delete').click();
    (await vi.waitFor(() => http.expectOne('/api/entries/e1'))).flush(null, { status: 404, statusText: 'Not Found' });
    await settle();

    expect(close).toHaveBeenCalledWith('deleted');
  });
});
