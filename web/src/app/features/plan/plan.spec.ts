import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
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

const fy2026 = { id: '2026-01', label: 'FY 2026', startDate: '2026-01-27', endDate: '2027-01-26', isTransition: false, periods };
// Example A's transition FY (R-CAL-08): Jan to Jun 2027.
const transition = {
  id: '2027-01',
  label: 'FY 2027',
  startDate: '2027-01-27',
  endDate: '2027-07-26',
  isTransition: true,
  periods: [{ key: '2027-01', label: 'Jan 2027', startDate: '2027-01-27', endDate: '2027-02-26' }],
};

// Groceries (Food) with the given amounts; income 17,000 every month.
function plansResponse(amounts: Record<string, number> = {}, fiscalYear = fy2026, fiscalYears = [fy2026]): PlansResponse {
  const planned = (key: string) => amounts[key] ?? 0;
  const total = Object.values(amounts).reduce((sum, amount) => sum + amount, 0);
  return {
    fiscalYears,
    fiscalYear,
    plans: Object.keys(amounts).length ? [{ subCategoryId: 'groceries', categoryId: 'food', amounts, total }] : [],
    categoryTotals: Object.keys(amounts).length ? { food: total } : {},
    categoryMonthTotals: Object.keys(amounts).length
      ? { food: Object.fromEntries(fiscalYear.periods.map((p) => [p.key, planned(p.key)])) }
      : {},
    totals: fiscalYear.periods.map((p) => ({
      periodKey: p.key,
      planned: planned(p.key),
      income: 17000,
      unplanned: 17000 - planned(p.key),
    })),
  };
}

describe('Plan (PRD 5.3)', () => {
  const me: Me = {
    setupCompleted: true,
    isAdmin: false,
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
  let harness: RouterTestingHarness;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'plan', component: Plan }]),
        { provide: MeService, useValue: meService },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    document.querySelectorAll('.cdk-overlay-container').forEach((el) => (el.innerHTML = ''));
  });

  async function settle(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await harness.fixture.whenStable();
    harness.detectChanges();
    TestBed.inject(ApplicationRef).tick();
  }

  async function open(categories: Category[], plans: PlansResponse, url = '/plan'): Promise<HTMLElement> {
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    (await vi.waitFor(() => http.expectOne('/api/categories'))).flush(categories);
    (await vi.waitFor(() => http.expectOne(`/api/fiscal-years/${plans.fiscalYear.id}/plans`))).flush(plans);
    await settle();
    return harness.routeNativeElement!;
  }

  // Every text node, joined with spaces (Angular drops the whitespace between sibling elements).
  function text(el: Element | null | undefined): string {
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

  function inputLabeled(root: ParentNode, label: string): HTMLInputElement {
    return Array.from(root.querySelectorAll('input')).find(
      (el) =>
        el.closest('mat-form-field')?.querySelector('mat-label')?.textContent?.trim() === label ||
        el.getAttribute('aria-label') === label,
    )!;
  }

  function type(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function button(root: ParentNode, label: string): HTMLButtonElement {
    return Array.from(root.querySelectorAll('button')).find(
      (el) => text(el) === label || text(el).endsWith(` ${label}`) || el.getAttribute('aria-label') === label,
    )!;
  }

  function dialog(): HTMLElement {
    return document.querySelector('mat-dialog-container')!;
  }

  const food: Category = { id: 'food', name: 'Food', sortOrder: 0, archived: false, subCategories: [] };
  const withGroceries: Category = {
    ...food,
    subCategories: [{ id: 'groceries', categoryId: 'food', name: 'Groceries', sortOrder: 0, archived: false }],
  };

  // PLN-06; PLN-03 turned sideways (owner's choice, M8): every month's numbers at once.
  it('guides you to the first sub-category and shows every month in the totals', async () => {
    const root = await open([food], plansResponse());

    expect(text(root)).toContain('Add your first sub-category');
    const table = root.querySelector('table.totals')!;
    expect(Array.from(table.querySelectorAll('tbody th')).map((th) => text(th))).toEqual(['Planned', 'Income', 'Unassigned']);
    const months = Array.from(table.querySelectorAll<HTMLButtonElement>('thead button.month'));
    expect(months.length).toBe(12);
    expect(text(months[0])).toBe('Jan 2026');
    expect(months[0].title).toBe('Jan 2026 · 27 Jan to 26 Feb');
    expect(text(table.querySelectorAll('tbody tr')[1].querySelectorAll('td')[0])).toBe('17,000');
    // The current month (Sep 2026) is selected.
    expect(months[8].getAttribute('aria-pressed')).toBe('true');
    expect(text(root.querySelector('#list-title'))).toBe('Planned in Sep 2026 27 Sep to 26 Oct');
  });

  // Categories start closed; the selected month drives the amounts.
  it('shows each category in the selected month and opens its sub-categories', async () => {
    const amounts = Object.fromEntries(periods.map((p) => [p.key, 1500]));
    amounts['2026-01'] = 900;
    const root = await open([withGroceries], plansResponse(amounts));

    const category = root.querySelector<HTMLButtonElement>('button.expand')!;
    expect(text(category)).toBe('chevron_right Food FY total SAR 17,400 SAR 1,500');
    expect(category.getAttribute('aria-expanded')).toBe('false');
    expect(root.querySelector('[aria-label="Edit amounts of Groceries"]')).toBeNull();

    root.querySelectorAll<HTMLButtonElement>('thead button.month')[0].click();
    category.click();
    await settle();

    expect(text(root.querySelector('#list-title'))).toContain('Planned in Jan 2026');
    expect(text(category)).toContain('SAR 900');
    expect(text(root.querySelector('.row.sub'))).toBe('Groceries FY total SAR 17,400 SAR 900 edit');
  });

  // PLN-01, PLN-02, PLN-04 in the editor dialog, with "Unassigned after" while typing (M8).
  it('edits the months in a dialog, previews Unassigned, and saves', async () => {
    const root = await open([withGroceries], plansResponse({ '2026-01': 99 }));
    root.querySelector<HTMLButtonElement>('button.expand')!.click();
    await settle();
    root.querySelector<HTMLButtonElement>('[aria-label="Edit amounts of Groceries"]')!.click();
    await settle();
    expect(inputLabeled(dialog(), 'Jan 2026').value).toBe('99');

    // Same amount for all months.
    type(inputLabeled(dialog(), 'Same amount for all months'), '1500.5');
    button(dialog(), 'Apply to all').click();
    await settle();
    expect(periods.map((p) => inputLabeled(dialog(), p.label).value)).toEqual(Array(12).fill('1500.5'));

    // Copy from previous month.
    type(inputLabeled(dialog(), 'Feb 2026'), '20000');
    button(dialog(), 'Copy from Feb 2026').click();
    await settle();
    expect(inputLabeled(dialog(), 'Mar 2026').value).toBe('20000');

    // Unassigned after: Jan 16,901 - (1,500.50 - 99) = 15,499.50; Feb 17,000 - 20,000 = -3,000 (red).
    const after = Array.from(dialog().querySelectorAll('.after'));
    expect(text(after[0])).toBe('Unassigned after SAR 15,499.50');
    expect(text(after[1])).toBe('Unassigned after SAR -3,000');
    expect(after[1].querySelector('.money')!.classList).toContain('danger');

    // Empty and 0 both clear a month (owner's choice, M4).
    type(inputLabeled(dialog(), 'Dec 2026'), '');
    type(inputLabeled(dialog(), 'Nov 2026'), '0');
    button(dialog(), 'Save').click();

    const put = await vi.waitFor(() => http.expectOne('/api/fiscal-years/2026-01/plans/groceries'));
    expect(put.request.method).toBe('PUT');
    expect(put.request.body.amounts).toEqual({
      '2026-01': 1500.5, '2026-02': 20000, '2026-03': 20000, '2026-04': 1500.5, '2026-05': 1500.5,
      '2026-06': 1500.5, '2026-07': 1500.5, '2026-08': 1500.5, '2026-09': 1500.5, '2026-10': 1500.5,
      '2026-11': 0,
    });
    put.flush(plansResponse({ '2026-09': 1500.5 }));
    await vi.waitFor(() => expect(document.querySelector('mat-dialog-container')).toBeNull());
    await settle();

    expect(text(root.querySelector('.status'))).toBe('Saved Groceries.');
    expect(root.querySelector('.row.sub')!.classList).toContain('flash');
    expect(text(root.querySelector('.row.sub'))).toContain('SAR 1,500.50');
  });

  it('does not save an invalid month', async () => {
    const root = await open([withGroceries], plansResponse());
    root.querySelector<HTMLButtonElement>('button.expand')!.click();
    await settle();
    root.querySelector<HTMLButtonElement>('[aria-label="Edit amounts of Groceries"]')!.click();
    await settle();

    type(inputLabeled(dialog(), 'Jan 2026'), '1.005');
    button(dialog(), 'Save').click();
    await settle();

    http.expectNone('/api/fiscal-years/2026-01/plans/groceries');
    expect(text(dialog())).toContain('at most 2 decimals');
  });

  // PLN-01: a new sub-category opens its month amounts right away.
  it('adds a sub-category and opens its months', async () => {
    const root = await open([food], plansResponse());
    root.querySelector<HTMLButtonElement>('button.expand')!.click();
    await settle();

    button(root, 'Add sub-category').click();
    await settle();
    type(inputLabeled(dialog(), 'Sub-category name'), ' Groceries ');
    button(dialog(), 'Save').click();
    const post = await vi.waitFor(() => http.expectOne('/api/sub-categories'));
    expect(post.request.body).toEqual({ categoryId: 'food', name: 'Groceries' });
    post.flush({ id: 'groceries', categoryId: 'food', name: 'Groceries', sortOrder: 0, archived: false });

    await vi.waitFor(() => expect(document.querySelector('app-plan-dialog')).not.toBeNull());
    await settle();
    expect(inputLabeled(dialog(), 'Jan 2026')).toBeDefined();
    expect(text(root.querySelector('.row.sub'))).toContain('Groceries');
  });

  // PLN-05, PRD 4.7
  it('refuses a category name that exists, ignoring case, without calling the API', async () => {
    const root = await open([food], plansResponse());

    button(root, 'Add category').click();
    await settle();
    type(inputLabeled(dialog(), 'Category name'), ' food ');
    button(dialog(), 'Save').click();
    await settle();

    http.expectNone('/api/categories');
    expect(text(dialog())).toContain('This category already exists.');
  });

  // Owner's choice (M8): an FY selector with more than one FY; the FY is in the URL.
  it('shows the FY from the URL and switches FY with the selector', async () => {
    const root = await open([withGroceries], plansResponse({}, transition, [fy2026, transition]), '/plan?fyId=2027-01');

    expect(root.querySelector('mat-select')?.textContent).toContain('FY 2027 · 27 Jan 2027 to 26 Jul 2027');
    expect(root.querySelectorAll('thead button.month').length).toBe(1);

    await harness.navigateByUrl('/plan?fyId=2026-01');
    (await vi.waitFor(() => http.expectOne('/api/categories'))).flush([withGroceries]);
    (await vi.waitFor(() => http.expectOne('/api/fiscal-years/2026-01/plans'))).flush(
      plansResponse({}, fy2026, [fy2026, transition]),
    );
    await settle();
    expect(root.querySelectorAll('thead button.month').length).toBe(12);
  });

  // Archived items stay hidden, except a sub-category with an amount in this FY: it still counts in
  // the totals, so it is shown marked "(archived)" and can be cleared (owner's choice, M8).
  it('shows an archived sub-category only when it has a plan in the FY', async () => {
    const archived: Category = {
      ...food,
      subCategories: [
        { id: 'groceries', categoryId: 'food', name: 'Groceries', sortOrder: 0, archived: true },
        { id: 'gym', categoryId: 'food', name: 'Gym', sortOrder: 1, archived: true },
      ],
    };
    const oldCategory: Category = { id: 'old', name: 'Old', sortOrder: 1, archived: true, subCategories: [] };
    const root = await open([archived, oldCategory], plansResponse({ '2026-01': 99 }));
    root.querySelector<HTMLButtonElement>('button.expand')!.click();
    await settle();

    const names = Array.from(root.querySelectorAll('.row.sub .name')).map((el) => text(el));
    expect(names).toEqual(['Groceries (archived) FY total SAR 99']);
    expect(root.querySelector('[aria-label="Edit amounts of Groceries"]')).not.toBeNull();
    expect(text(root)).not.toContain('Old');
    expect(text(root)).toContain('Add your first sub-category');
  });
});
