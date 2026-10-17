import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Me, MeService } from '../../core/me';
import { Plan } from './plan';
import { Category, PlansResponse } from './plan-api';

// FY 2026 with payday 27 (the PRD section 3 example), months as stored by the API.
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const periods = MONTHS.map((label, i) => {
  const m = i + 1;
  const next = m === 12 ? '2027-01' : `2026-${String(m + 1).padStart(2, '0')}`;
  return { key: `2026-${String(m).padStart(2, '0')}`, label: `${label} 2026`, startDate: `2026-${String(m).padStart(2, '0')}-27`, endDate: `${next}-26` };
});

function plansResponse(amounts: Record<string, number> = {}): PlansResponse {
  return {
    fiscalYear: { id: '2026-01', label: 'FY 2026', startDate: '2026-01-27', endDate: '2027-01-26', isTransition: false, periods },
    plans: Object.keys(amounts).length ? [{ subCategoryId: 'groceries', categoryId: 'food', amounts, total: 0 }] : [],
    categoryTotals: {},
    totals: periods.map((p) => ({ periodKey: p.key, planned: 0, income: 17000, unplanned: 17000 })),
  };
}

describe('Plan (PRD 5.3)', () => {
  const me: Me = {
    setupCompleted: true,
    profile: { displayName: 'Owner', email: 'o@example.com', projectName: 'Home', currency: 'SAR', timezone: 'Asia/Riyadh', fiscalStartMonth: 1, paydayDay: 27, lastBankAccountId: null },
    currentFyId: '2026-01',
    currentPeriodKey: '2026-09',
    currentPeriod: periods[8],
    today: '2026-10-17',
    firstDate: '2026-01-27',
    lastDate: '2027-01-26',
  };
  const meService = { me: signal(me), load: async () => me };
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Plan],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MeService, useValue: meService }],
    });
    http = TestBed.inject(HttpTestingController);
  });

  // A flushed response resolves the page's awaits one task later.
  async function settle(fixture: ComponentFixture<Plan>): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  }

  async function open(categories: Category[], plans: PlansResponse): Promise<ComponentFixture<Plan>> {
    const fixture = TestBed.createComponent(Plan);
    (await vi.waitFor(() => http.expectOne('/api/categories'))).flush(categories);
    (await vi.waitFor(() => http.expectOne('/api/fiscal-years/2026-01/plans'))).flush(plans);
    await settle(fixture);
    return fixture;
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

  function button(root: HTMLElement, text: string): HTMLButtonElement {
    return Array.from(root.querySelectorAll('button')).find((el) => el.textContent?.trim() === text)!;
  }

  const food: Category = { id: 'food', name: 'Food', sortOrder: 0, archived: false, subCategories: [] };
  const withGroceries: Category = {
    ...food,
    subCategories: [{ id: 'groceries', categoryId: 'food', name: 'Groceries', sortOrder: 0, archived: false }],
  };

  // PLN-06, PLN-03
  it('guides you to the first sub-category and shows a totals row per stored month', async () => {
    const fixture = await open([food], plansResponse());
    const root = fixture.nativeElement as HTMLElement;

    expect(root.textContent).toContain('Add your first sub-category');
    const rows = root.querySelectorAll('.totals tbody tr');
    expect(rows.length).toBe(12);
    expect(rows[0].textContent).toContain('Jan 2026');
    expect(rows[0].textContent).toContain('27 Jan to 26 Feb');
    expect(rows[0].textContent).toContain('SAR 17,000');
    // R-PLN-03 "unplanned income" is labeled "Unassigned" (owner's choice, M6), on desktop and phones.
    const headers = Array.from(root.querySelectorAll('.totals thead th')).map((th) => th.textContent?.trim());
    expect(headers).toEqual(['Month', 'Planned', 'Income', 'Unassigned']);
    expect(rows[0].querySelectorAll('td')[2].getAttribute('data-label')).toBe('Unassigned');
  });

  // PLN-02, PLN-04 (Jan 2026 is closed and editable)
  it('fills months with the helpers and saves them, leaving empty months out', async () => {
    const fixture = await open([withGroceries], plansResponse({ '2026-01': 99 }));
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).not.toContain('Add your first sub-category');

    (root.querySelector('[aria-label="Edit amounts of Groceries"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(inputLabeled(root, 'Jan 2026').value).toBe('99');

    // Same amount for all months.
    type(inputLabeled(root, 'Same amount for all months'), '1500.5');
    button(root, 'Apply to all').click();
    await fixture.whenStable();
    expect(periods.map((p) => inputLabeled(root, p.label).value)).toEqual(Array(12).fill('1500.5'));

    // Copy from previous month.
    type(inputLabeled(root, 'Feb 2026'), '200');
    (root.querySelector('[aria-label="Copy from Feb 2026"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(inputLabeled(root, 'Mar 2026').value).toBe('200');

    // Empty and 0 both clear a month (owner's choice, M4).
    type(inputLabeled(root, 'Dec 2026'), '');
    type(inputLabeled(root, 'Nov 2026'), '0');
    button(root, 'Save').click();

    const put = await vi.waitFor(() => http.expectOne('/api/fiscal-years/2026-01/plans/groceries'));
    expect(put.request.method).toBe('PUT');
    expect(put.request.body.amounts).toEqual({
      '2026-01': 1500.5, '2026-02': 200, '2026-03': 200, '2026-04': 1500.5, '2026-05': 1500.5,
      '2026-06': 1500.5, '2026-07': 1500.5, '2026-08': 1500.5, '2026-09': 1500.5, '2026-10': 1500.5,
      '2026-11': 0,
    });
    put.flush(plansResponse({ '2026-01': 1500.5 }));
    await settle(fixture);

    expect(root.textContent).toContain('Saved Groceries.');
    expect(button(root, 'Save')).toBeUndefined();
  });

  it('does not save an invalid month', async () => {
    const fixture = await open([withGroceries], plansResponse());
    const root = fixture.nativeElement as HTMLElement;
    (root.querySelector('[aria-label="Edit amounts of Groceries"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    type(inputLabeled(root, 'Jan 2026'), '1.005');
    button(root, 'Save').click();
    await fixture.whenStable();

    http.expectNone('/api/fiscal-years/2026-01/plans/groceries');
    expect(root.textContent).toContain('at most 2 decimals');
  });

  // PLN-01: a new sub-category opens its month amounts right away.
  it('adds a sub-category and opens its months', async () => {
    const fixture = await open([food], plansResponse());
    const root = fixture.nativeElement as HTMLElement;

    type(inputLabeled(root, 'New sub-category in Food'), ' Groceries ');
    button(root, 'Add').click();
    const post = await vi.waitFor(() => http.expectOne('/api/sub-categories'));
    expect(post.request.body).toEqual({ categoryId: 'food', name: 'Groceries' });
    post.flush({ id: 'groceries', categoryId: 'food', name: 'Groceries', sortOrder: 0, archived: false });
    await settle(fixture);

    expect(root.querySelector('.sub-name')?.textContent).toBe('Groceries');
    expect(inputLabeled(root, 'Jan 2026')).toBeDefined();
    expect(root.textContent).not.toContain('Add your first sub-category');
  });

  // PLN-05, PRD 4.7
  it('refuses a category name that exists, ignoring case, without calling the API', async () => {
    const fixture = await open([food], plansResponse());
    const root = fixture.nativeElement as HTMLElement;

    type(inputLabeled(root, 'Category name'), ' food ');
    const addButtons = Array.from(root.querySelectorAll('button')).filter((el) => el.textContent?.trim() === 'Add');
    addButtons.at(-1)!.click();
    await fixture.whenStable();

    http.expectNone('/api/categories');
    expect(root.textContent).toContain('This category already exists.');
  });

  afterEach(() => http.verify());
});
