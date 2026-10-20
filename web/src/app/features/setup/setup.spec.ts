import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormArray, FormControl, FormGroup } from '@angular/forms';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth';
import { Setup } from './setup';

// The PRD section 3 example: FY start January, payday 27, today 17 Oct 2026 (Sep 2026).
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const periods = MONTHS.map((name, i) => ({
  key: `2026-${String(i + 1).padStart(2, '0')}`,
  label: `${name} 2026`,
  startDate: `2026-${String(i + 1).padStart(2, '0')}-27`,
  endDate: i < 11 ? `2026-${String(i + 2).padStart(2, '0')}-26` : '2027-01-26',
}));
const preview = {
  fiscalYear: { id: '2026-01', label: 'FY 2026', startDate: '2026-01-27', endDate: '2027-01-26', isTransition: false, periods },
  currentPeriod: periods[8],
};

// The protected members the income month specs drive, instead of opening 12 mat-select panels.
interface Wizard {
  project: FormGroup;
  calendar: FormGroup;
  income: FormGroup;
  bankAccounts: FormArray;
  categories: FormArray;
  additional: FormArray<FormGroup>;
  months(): unknown[];
  currentIncomeMinor(): number;
  incomeLines(): string[];
  addIncomeItem(): void;
  finish(): Promise<void>;
}

describe('Setup wizard (PRD 5.2)', () => {
  const auth = { user: signal({ uid: 'user-1', displayName: 'Owner' }), signOut: vi.fn() };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [Setup],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: auth }],
    });
  });

  // The Add buttons once sat in a plain <form>, which the browser submitted: the page reloaded
  // and the account was not added.
  it('adds a bank account with the Add button and with Enter, without submitting a form', async () => {
    const fixture = TestBed.createComponent(Setup);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const input = Array.from(root.querySelectorAll('input')).find(
      (el) => el.closest('mat-form-field')?.textContent?.includes('Account name'),
    )!;
    const addButton = input.closest('.add-row')!.querySelector('button')!;

    expect(input.closest('form')).toBeNull();
    expect(addButton.type).toBe('button');

    input.value = ' SNB ';
    input.dispatchEvent(new Event('input'));
    addButton.click();
    input.value = 'Al Rajhi';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await fixture.whenStable();

    const names = Array.from(root.querySelectorAll('.names li > span')).map((el) => el.textContent);
    expect(names).toEqual(['SNB', 'Al Rajhi']);
  });

  it('prefills your name from Google', async () => {
    const fixture = TestBed.createComponent(Setup);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const input = Array.from(root.querySelectorAll('input')).find(
      (el) => el.closest('mat-form-field')?.textContent?.includes('Your name'),
    )!;
    expect(input.value).toBe('Owner');
  });
});

// R-INC-03 in the wizard (PRD 5.2 step 3): additional income goes in the months you pick.
describe('Setup wizard income months', () => {
  const auth = { user: signal({ uid: 'user-1', displayName: 'Owner' }), signOut: vi.fn() };
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [Setup],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: auth }],
    });
    http = TestBed.inject(HttpTestingController);
  });

  // Step 2 filled in and the preview answered (it waits 250 ms after typing).
  async function wizardWithPreview(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<Setup>>; wizard: Wizard }> {
    const fixture = TestBed.createComponent(Setup);
    const wizard = fixture.componentInstance as unknown as Wizard;
    await fixture.whenStable();
    wizard.calendar.setValue({ fiscalStartMonth: 1, paydayDay: 27 });
    await new Promise((resolve) => setTimeout(resolve, 300));
    http.expectOne((request) => request.url === '/api/setup/preview').flush(preview);
    await fixture.whenStable();
    return { fixture, wizard };
  }

  function item(wizard: Wizard, index: number): FormGroup {
    return wizard.additional.at(index);
  }

  it('offers the 12 months of the FY, Monthly from the first month and Once in the current month', async () => {
    const { fixture, wizard } = await wizardWithPreview();
    expect(wizard.months().length).toBe(12);

    wizard.addIncomeItem();
    await fixture.whenStable();
    expect(item(wizard, 0).getRawValue()).toEqual({ label: '', amount: '', type: 'monthly', from: '2026-01', to: '' });
    const labels = () =>
      Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.income-months mat-label')).map((el) => el.textContent?.trim());
    expect(labels()).toEqual(['From', 'Until (optional)']);

    // A month still at its default follows the type.
    item(wizard, 0).controls['type'].setValue('once');
    await fixture.whenStable();
    expect(item(wizard, 0).controls['from'].value).toBe('2026-09');
    expect(labels()).toEqual(['Month']);

    // A picked month stays when the type changes.
    item(wizard, 0).controls['from'].setValue('2026-03');
    item(wizard, 0).controls['type'].setValue('monthly');
    expect(item(wizard, 0).controls['from'].value).toBe('2026-03');
  });

  it('blocks Until before From', async () => {
    const { fixture, wizard } = await wizardWithPreview();
    wizard.income.controls['salary'].setValue('15000');
    wizard.addIncomeItem();
    item(wizard, 0).patchValue({ label: 'Freelance', amount: '1500', from: '2026-06', to: '2026-05' });
    await fixture.whenStable();

    expect(wizard.income.invalid).toBe(true);
    expect((fixture.nativeElement as HTMLElement).querySelector('.income-error')?.textContent).toContain('Until is the same month as From or later.');
    item(wizard, 0).controls['to'].setValue('2026-06');
    expect(wizard.income.valid).toBe(true);
  });

  it('totals the current month and lists every item with its months', async () => {
    const { wizard } = await wizardWithPreview();
    wizard.income.controls['salary'].setValue('15000');
    for (const value of [
      { label: 'Annual bonus', amount: '5000', type: 'once', from: '2026-03', to: '' },
      { label: 'Eid bonus', amount: '1000', type: 'once', from: '2026-09', to: '' },
      { label: 'Freelance', amount: '1500', type: 'monthly', from: '2026-06', to: '2026-08' },
      { label: 'Rent', amount: '2000', type: 'monthly', from: '2026-01', to: '' },
    ]) {
      wizard.addIncomeItem();
      wizard.additional.at(-1).setValue(value);
    }

    // Sep 2026: salary 15,000 + Eid bonus 1,000 + Rent 2,000. The March bonus and the
    // freelance work that ended in Aug do not count.
    expect(wizard.currentIncomeMinor()).toBe(1_800_000);
    expect(wizard.incomeLines()).toEqual([
      'Annual bonus · SAR 5,000 · Mar 2026',
      'Eid bonus · SAR 1,000 · Sep 2026',
      'Freelance · SAR 1,500 · Jun 2026 to Aug 2026',
      'Rent · SAR 2,000 · From Jan 2026',
    ]);
  });

  it('sends the chosen months on Finish', async () => {
    const { wizard } = await wizardWithPreview();
    wizard.project.patchValue({ projectName: 'Home budget' });
    wizard.income.controls['salary'].setValue('15000');
    wizard.addIncomeItem();
    wizard.additional.at(0).setValue({ label: 'Annual bonus', amount: '5000', type: 'once', from: '2026-03', to: '2026-05' });
    wizard.addIncomeItem();
    wizard.additional.at(1).setValue({ label: 'Rent', amount: '2000', type: 'monthly', from: '2026-10', to: '' });
    wizard.bankAccounts.push(new FormControl('SNB'));
    wizard.categories.push(new FormControl('Food'));

    void wizard.finish();
    const request = http.expectOne('/api/setup');
    expect(request.request.body.additionalIncome).toEqual([
      { label: 'Annual bonus', amount: 5000, type: 'once', fromPeriodKey: '2026-03', toPeriodKey: null },
      { label: 'Rent', amount: 2000, type: 'monthly', fromPeriodKey: '2026-10', toPeriodKey: null },
    ]);
  });

  it('gives a restored item without months its default, and keeps months the FY still has', async () => {
    localStorage.setItem(
      'nama.setup.user-1',
      JSON.stringify({
        fiscalStartMonth: 1,
        paydayDay: 27,
        additional: [
          { label: 'Bonus', amount: '500', type: 'once' },
          { label: 'Rent', amount: '2000', type: 'monthly', from: '2026-04', to: '2027-03' },
        ],
      }),
    );
    const fixture = TestBed.createComponent(Setup);
    const wizard = fixture.componentInstance as unknown as Wizard;
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 300));
    http.expectOne((request) => request.url === '/api/setup/preview').flush(preview);
    await fixture.whenStable();

    // An older draft has no months; 2027-03 is not a month of FY 2026.
    expect(item(wizard, 0).getRawValue()).toMatchObject({ from: '2026-09', to: '' });
    expect(item(wizard, 1).getRawValue()).toMatchObject({ from: '2026-04', to: '' });
  });
});
