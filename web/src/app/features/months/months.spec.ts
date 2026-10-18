import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatFormFieldHarness } from '@angular/material/form-field/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Me, MeService } from '../../core/me';
import { EntriesResponse, Entry } from '../add-entry/entry-api';
import { Category, FiscalYear } from '../plan/plan-api';
import { Months } from './months';
import { MonthOverview, OverviewResponse } from './overview-api';

// Today is 17 Oct 2026; FY 2026 has payday 27, so Sep 2026 (27 Sep to 26 Oct) is in progress.
const aug = { key: '2026-08', label: 'Aug 2026', startDate: '2026-08-27', endDate: '2026-09-26' };
const sep = { key: '2026-09', label: 'Sep 2026', startDate: '2026-09-27', endDate: '2026-10-26' };
const oct = { key: '2026-10', label: 'Oct 2026', startDate: '2026-10-27', endDate: '2026-11-26' };
const fy2026: FiscalYear = {
  id: '2026-01',
  label: 'FY 2026',
  startDate: '2026-01-27',
  endDate: '2027-01-26',
  isTransition: false,
  periods: [aug, sep, oct],
};
const fy2027: FiscalYear = {
  id: '2027-01',
  label: 'FY 2027',
  startDate: '2027-01-27',
  endDate: '2028-01-26',
  isTransition: false,
  periods: [{ key: '2027-01', label: 'Jan 2027', startDate: '2027-01-27', endDate: '2027-02-26' }],
};

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
    paydayDay: 27,
    lastBankAccountId: 'rajhi',
  },
  currentFyId: '2026-01',
  currentPeriodKey: '2026-09',
  currentPeriod: sep,
  today: '2026-10-17',
  firstDate: '2026-01-27',
  lastDate: '2027-01-26',
};

const categories: Category[] = [
  {
    id: 'food',
    name: 'Food',
    sortOrder: 0,
    archived: false,
    subCategories: [
      { id: 'groceries', categoryId: 'food', name: 'Groceries', sortOrder: 0, archived: false },
    ],
  },
  {
    id: 'transport',
    name: 'Transport',
    sortOrder: 1,
    archived: false,
    subCategories: [
      { id: 'fuel', categoryId: 'transport', name: 'Fuel', sortOrder: 0, archived: false },
      { id: 'taxi', categoryId: 'transport', name: 'Taxi', sortOrder: 1, archived: true },
    ],
  },
];

function month(
  period: typeof sep,
  status: MonthOverview['status'],
  income: number,
  planned: number,
  spent: number,
): MonthOverview {
  return { period, status, income, planned, spent, remaining: planned - spent };
}

const cells = (...values: [number, number][]) =>
  values.map(([planned, spent], i) => ({ periodKey: [aug, sep, oct][i].key, planned, spent }));

// Taxi (archived) has 30 spent in Aug with no plan; Fuel has nothing in Aug.
function overview(
  fyId = '2026-01',
  statuses: MonthOverview['status'][] = ['closed', 'inProgress', 'yetToCome'],
  fiscalYears = [fy2026],
): OverviewResponse {
  return {
    fiscalYears,
    fyId,
    months: [
      month(aug, statuses[0], 17000, 1500, 80),
      month(sep, statuses[1], 17500.25, 1900, 1299.99),
      month(oct, statuses[2], 17000, 1900, 0),
    ],
    categories,
    rows: [
      {
        categoryId: 'food',
        months: cells([1500, 50], [1500, 1199.99], [1500, 0]),
        planned: 4500,
        spent: 1249.99,
        subCategories: [
          {
            subCategoryId: 'groceries',
            months: cells([1500, 50], [1500, 1199.99], [1500, 0]),
            planned: 4500,
            spent: 1249.99,
          },
        ],
      },
      {
        categoryId: 'transport',
        months: cells([0, 30], [400, 100], [400, 0]),
        planned: 800,
        spent: 130,
        subCategories: [
          {
            subCategoryId: 'fuel',
            months: cells([0, 0], [400, 100], [400, 0]),
            planned: 800,
            spent: 100,
          },
          { subCategoryId: 'taxi', months: cells([0, 30], [0, 0], [0, 0]), planned: 0, spent: 30 },
        ],
      },
    ],
    totals: { income: 51500.25, planned: 5300, spent: 1379.99, remaining: 3920.01 },
    toDate: { income: 34500.25, spent: 1379.99, saved: 33120.26 },
    rollover: null,
  };
}

const big: Entry = {
  id: 'e2',
  date: '2026-10-17',
  periodKey: '2026-09',
  fyId: '2026-01',
  categoryId: 'food',
  subCategoryId: 'groceries',
  bankAccountId: 'rajhi',
  amount: 799.99,
  note: 'Big shop',
};

function entries(list: Entry[]): EntriesResponse {
  return {
    fiscalYears: [fy2026],
    fyId: '2026-01',
    bankAccounts: [{ id: 'rajhi', name: 'Al Rajhi', archived: false }],
    entries: list,
    total: list.reduce((sum, entry) => sum + entry.amount, 0),
  };
}

describe('Months (PRD 5.6)', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let meValue: Me;

  beforeEach(() => {
    meValue = me;
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'months', component: Months }]),
        {
          provide: MeService,
          useValue: { me: signal(me), load: async () => meValue, set: (value: Me) => (meValue = value) },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    document.querySelectorAll('.cdk-overlay-container').forEach((el) => (el.innerHTML = ''));
  });

  async function settle(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  function overviewCall(fyId = '2026-01'): Promise<TestRequest> {
    return vi.waitFor(() => http.expectOne(`/api/fiscal-years/${fyId}/overview`));
  }

  function entriesCall(periodKey: string): Promise<TestRequest> {
    return vi.waitFor(() =>
      http.expectOne(
        (req) =>
          req.url === '/api/entries' &&
          req.params.get('periodKey') === periodKey &&
          req.params.get('fyId') === '2026-01',
      ),
    );
  }

  // NFR-01: the overview, plus the entries of the in-progress month, which opens expanded.
  async function open(
    url = '/months',
    data = overview(),
    current: string | null = data.months.find((m) => m.status === 'inProgress')?.period.key ?? null,
  ): Promise<void> {
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    (await overviewCall(data.fyId)).flush(data);
    if (current) {
      (await entriesCall(current)).flush(entries([big]));
    }
    await settle();
    http.verify();
  }

  function root(): HTMLElement {
    return harness.routeNativeElement!;
  }

  function text(el: Element | null | undefined): string {
    return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  }

  // Every text node, joined with spaces (Angular drops the whitespace between sibling elements).
  function words(el: Element | null | undefined): string {
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

  function card(key: string): HTMLElement {
    return root().querySelector(`#month-${key}`)!;
  }

  function button(scope: ParentNode, label: string): HTMLButtonElement {
    return Array.from(scope.querySelectorAll('button')).find(
      (el) =>
        el.querySelector('.mdc-button__label')?.textContent?.trim() === label ||
        el.getAttribute('aria-label') === label ||
        el.textContent?.trim() === label,
    )!;
  }

  function cell(label: string): HTMLAnchorElement {
    return Array.from(root().querySelectorAll<HTMLAnchorElement>('a.cell')).find((a) =>
      a.getAttribute('aria-label')?.startsWith(label),
    )!;
  }

  // MON-01, MON-02
  it('shows one card per month with its status, numbers and the in-progress month expanded', async () => {
    await open();

    expect(root().querySelectorAll('section.month').length).toBe(3);
    expect(card('2026-08').classList).toContain('closed');
    expect(words(card('2026-08').querySelector('.month-head'))).toBe(
      'Aug 2026 27 Aug to 26 Sep Closed',
    );
    expect(card('2026-09').classList).toContain('inProgress');
    expect(text(card('2026-09').querySelector('.month-head .now-chip'))).toBe('In progress');
    expect(card('2026-10').classList).toContain('yetToCome');
    expect(text(card('2026-10').querySelector('.status-text'))).toBe('Upcoming');

    const kpis = Array.from(card('2026-09').querySelectorAll('.kpi')).map((kpi) => words(kpi));
    expect(kpis).toEqual([
      'Remaining SAR 600.01',
      'Income SAR 17,500.25',
      'Planned SAR 1,900',
      'Spent SAR 1,299.99',
    ]);
    expect(text(card('2026-09').querySelector('.bar-line .chip'))).toBe('Within plan');
    // Remaining carries the status color; the other numbers stay neutral.
    expect(card('2026-09').querySelector('.remaining .value')!.classList).toContain('ok');
    expect(
      card('2026-09').querySelectorAll('.kpi .value.ok, .kpi .value.warn, .kpi .value.danger')
        .length,
    ).toBe(1);

    // Only the in-progress month is open, with its categories and entries.
    expect(card('2026-09').querySelector('mat-expansion-panel')!.classList).toContain(
      'mat-expanded',
    );
    expect(card('2026-08').querySelector('mat-expansion-panel')!.classList).not.toContain(
      'mat-expanded',
    );
    const lines = Array.from(card('2026-09').querySelectorAll('.spend-row')).map((row) =>
      words(row),
    );
    expect(lines).toEqual([
      'Food Within plan SAR 1,199.99 of 1,500',
      'Groceries Within plan SAR 1,199.99 of 1,500',
      'Transport Within plan SAR 100 of 400',
      'Fuel Within plan SAR 100 of 400',
    ]);
    const entry = card('2026-09').querySelector('.entry')!;
    expect(
      ['.day', '.name', '.money'].map((selector) => text(entry.querySelector(selector))),
    ).toEqual(['17 Oct', 'Groceries', 'SAR 799.99']);

    // One FY: no selector, the FY as a caption (MON-03).
    expect(text(root().querySelector('.fy'))).toBe('FY 2026 · 27 Jan 2026 to 26 Jan 2027');
    expect(root().textContent).not.toContain('Fiscal year');
  });

  it("loads a month's entries once when its card is expanded", async () => {
    await open();
    const header = card('2026-08').querySelector('mat-expansion-panel-header') as HTMLElement;

    header.click();
    (await entriesCall('2026-08')).flush(entries([]));
    await settle();

    // Spending without a plan is over plan (R-PLN-01): Taxi (archived) is listed in Aug.
    expect(words(card('2026-08').querySelectorAll('.spend-row')[3])).toBe(
      'Taxi (archived) Over by SAR 30 SAR 30 of 0',
    );
    expect(text(card('2026-08').querySelector('.note'))).toBe(
      'No entries in Aug 2026. Add an entry.',
    );

    header.click();
    await settle();
    header.click();
    await settle();
    http.verify();
  });

  // Owner's choice (M7): only the newest 5, and a link to the month in Entries.
  it('lists the newest 5 entries of a month and links to the rest in Entries', async () => {
    await open();
    expect(card('2026-09').querySelector('.see-all')).toBeNull();

    (card('2026-08').querySelector('mat-expansion-panel-header') as HTMLElement).click();
    const augEntries = Array.from({ length: 7 }, (_, i) => ({
      ...big,
      id: `a${i}`,
      date: `2026-09-${String(20 - i).padStart(2, '0')}`,
      periodKey: '2026-08',
      amount: 10 + i,
    }));
    (await entriesCall('2026-08')).flush(entries(augEntries));
    await settle();

    const shown = Array.from(card('2026-08').querySelectorAll('.entry .money')).map((el) =>
      text(el),
    );
    expect(shown).toEqual(['SAR 10', 'SAR 11', 'SAR 12', 'SAR 13', 'SAR 14']);
    const link = card('2026-08').querySelector('a.see-all')!;
    expect(text(link)).toBe('See all 7 entries in Aug 2026');
    expect(link.getAttribute('href')).toBe('/entries?fyId=2026-01&periodKey=2026-08');
  });

  // UX-03: the Months view says what to do when nothing is planned or spent in the FY.
  it('shows an empty state above the month cards when nothing is planned or spent', async () => {
    await open('/months', { ...overview(), rows: [] });

    const empty = root().querySelector('.empty')!;
    expect(text(empty.querySelector('h2'))).toBe('Nothing planned or spent in FY 2026 yet');
    expect(Array.from(empty.querySelectorAll('a')).map((a) => a.getAttribute('href'))).toEqual([
      '/plan',
      '/add',
    ]);
    expect(root().querySelectorAll('section.month').length).toBe(3);
  });

  it('shows no empty state when something is planned or spent', async () => {
    await open();

    expect(root().querySelector('.empty')).toBeNull();
  });

  it('opens no card when no month is in progress', async () => {
    const past = overview('2026-01', ['closed', 'closed', 'closed']);
    await open('/months?fyId=2026-01', past);

    expect(root().querySelectorAll('mat-expansion-panel.mat-expanded').length).toBe(0);
    http.verify();
  });

  // MON-03: same default as Entries; with no current FY, the latest.
  it('uses the latest FY when no FY contains today', async () => {
    meValue = { ...me, currentFyId: null, currentPeriodKey: null, currentPeriod: null };
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/months');

    (await vi.waitFor(() => http.expectOne('/api/fiscal-years'))).flush([fy2026, fy2027]);
    (await overviewCall('2027-01')).flush({
      ...overview('2027-01', ['yetToCome', 'yetToCome', 'yetToCome'], [fy2026, fy2027]),
    });
    await settle();
    http.verify();
  });

  it('shows the FY selector with more than one FY and keeps the choice in the URL', async () => {
    await open('/months', overview('2026-01', undefined, [fy2026, fy2027]));
    const field = await TestbedHarnessEnvironment.loader(harness.fixture).getHarness(
      MatFormFieldHarness.with({ floatingLabelText: 'Fiscal year' }),
    );

    await (await field.getControl(MatSelectHarness))!.clickOptions({ text: /FY 2027/ });

    expect(TestBed.inject(Router).url).toBe('/months?fyId=2027-01');
    (await overviewCall('2027-01')).flush(
      overview('2027-01', ['yetToCome', 'yetToCome', 'yetToCome'], [fy2026, fy2027]),
    );
    await settle();
    http.verify();
  });

  // FY-01, FY-02: the banner during the last month; Start, then the overview of the FY that now
  // contains today (the new /api/me), without the banner.
  it('shows the rollover banner and starts the next FY', async () => {
    const lastMonth = overview('2026-01', ['closed', 'closed', 'inProgress']);
    lastMonth.rollover = {
      fyId: '2026-01',
      label: 'FY 2026',
      endDate: '2027-01-26',
      nextLabel: 'FY 2027',
      nextStartDate: '2027-01-27',
    };
    await open('/months', lastMonth);
    const banner = root().querySelector('app-rollover-banner')!;
    expect(text(banner)).toContain('FY 2026 ends on 26 Jan 2027.');

    button(banner, 'Start FY 2027').click();
    const post = await vi.waitFor(() => http.expectOne('/api/fiscal-years/next'));
    expect(post.request.body).toEqual({ copyPlans: true });
    post.flush({ fiscalYear: fy2027, copiedPlans: 1, me: { ...me, currentFyId: '2027-01', lastDate: '2028-01-26' } });

    (await overviewCall('2027-01')).flush(
      overview('2027-01', ['yetToCome', 'yetToCome', 'yetToCome'], [fy2026, fy2027]),
    );
    await settle();
    http.verify();

    expect(root().querySelector('app-rollover-banner')).toBeNull();
    expect(text(root().querySelector('.status'))).toBe('FY 2027 started.');
  });

  // Plan's "See the year grid" link (M8).
  it('opens the Year view from the URL', async () => {
    await open('/months?fyId=2026-01&view=year', overview(), null);

    expect(root().querySelector('app-year-grid')).not.toBeNull();
    expect(localStorage.getItem('nama.months.view')).toBe('year');
  });

  // MON-02: the last choice is remembered.
  it('remembers the view choice', async () => {
    await open();

    button(root(), 'Year').click();
    await settle();
    expect(localStorage.getItem('nama.months.view')).toBe('year');
    expect(root().querySelector('table.grid')).not.toBeNull();

    button(root(), 'Months').click();
    await settle();
    expect(localStorage.getItem('nama.months.view')).toBe('months');
    expect(root().querySelectorAll('section.month').length).toBe(3);
  });

  it('opens on the remembered Year view, and the open card loads its entries when Months is shown', async () => {
    localStorage.setItem('nama.months.view', 'year');
    await open('/months', overview(), null);
    expect(root().querySelector('table.grid')).not.toBeNull();

    button(root(), 'Months').click();
    await settle();
    (await entriesCall('2026-09')).flush(entries([big]));
    await settle();
    http.verify();
    expect(card('2026-09').querySelector('mat-expansion-panel')!.classList).toContain(
      'mat-expanded',
    );
    expect(card('2026-09').querySelector('.entry')).not.toBeNull();
  });

  it('works without storage', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    await open();
    expect(root().querySelectorAll('section.month').length).toBe(3);

    button(root(), 'Year').click();
    await settle();
    expect(root().querySelector('table.grid')).not.toBeNull();
  });

  // MON-02 Year view and MON-04 links.
  it('shows the year grid with month statuses, colors and links to Entries', async () => {
    await open();
    button(root(), 'Year').click();
    await settle();

    const headers = Array.from(root().querySelectorAll('thead th')).map((th) => words(th));
    expect(headers).toEqual([
      'Category',
      'Aug 2026 Closed',
      'Sep 2026 In progress',
      'Oct 2026 Upcoming',
      'FY total',
    ]);
    // The in-progress column is marked in every row (Food, Transport, Total).
    expect(root().querySelectorAll('td.now').length).toBe(3);

    const food = cell('Food, Sep 2026');
    expect(words(food)).toBe('SAR 1,199.99 of 1,500');
    expect(food.classList).toContain('ok');
    expect(food.getAttribute('aria-label')).toBe(
      'Food, Sep 2026: SAR 1,199.99 of SAR 1,500, Within plan. Open entries',
    );
    expect(food.getAttribute('href')).toBe(
      '/entries?fyId=2026-01&periodKey=2026-09&categoryId=food',
    );
    expect(cell('Food, FY 2026').getAttribute('href')).toBe(
      '/entries?fyId=2026-01&categoryId=food',
    );
    expect(cell('Total, Sep 2026').getAttribute('href')).toBe(
      '/entries?fyId=2026-01&periodKey=2026-09',
    );
    expect(words(cell('Total, Sep 2026'))).toBe('SAR 1,299.99 of 1,900');
    expect(cell('Total, FY 2026').getAttribute('href')).toBe('/entries?fyId=2026-01');

    // Spending without a plan is red, with "Over by" in the label.
    const transportAug = cell('Transport, Aug 2026');
    expect(transportAug.classList).toContain('danger');
    expect(transportAug.getAttribute('aria-label')).toContain('Over by SAR 30');

    // Category rows expand to sub-categories.
    expect(cell('Groceries, Sep 2026')).toBeUndefined();
    const toggle = button(root(), 'Show sub-categories of Transport');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    toggle.click();
    await settle();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(cell('Fuel, Sep 2026').getAttribute('href')).toBe(
      '/entries?fyId=2026-01&periodKey=2026-09&subCategoryId=fuel',
    );
    expect(cell('Fuel, FY 2026').getAttribute('href')).toBe(
      '/entries?fyId=2026-01&subCategoryId=fuel',
    );
    // Nothing planned and nothing spent: a dash, no color.
    const fuelAug = cell('Fuel, Aug 2026');
    expect(text(fuelAug)).toBe('–');
    expect(fuelAug.getAttribute('aria-label')).toBe(
      'Fuel, Aug 2026: nothing planned or spent. Open entries',
    );
    expect(cell('Taxi (archived), Aug 2026').classList).toContain('danger');
    http.verify();
  });

  // TBL-03, UX-04: the Entries dialog; afterwards the overview and the open months reload.
  it('deletes an entry from a month card and reloads the overview and the open months', async () => {
    await open();
    button(card('2026-09'), 'Edit Groceries, SAR 799.99, 17 Oct 2026').click();
    (
      await vi.waitFor(() =>
        http.expectOne('/api/indicator?subCategoryId=groceries&date=2026-10-17'),
      )
    ).flush({
      period: sep,
      planned: 1500,
      spent: 1199.99,
      remaining: 300.01,
      percent: 79,
    });
    await settle();
    const dialog = document.querySelector('mat-dialog-container')!;

    button(dialog, 'Delete').click();
    await settle();
    button(document.querySelector('mat-dialog-container')!, 'Delete').click();
    const remove = await vi.waitFor(() => http.expectOne('/api/entries/e2'));
    expect(remove.request.method).toBe('DELETE');
    remove.flush(null, { status: 204, statusText: 'No Content' });

    const reloaded = overview();
    reloaded.months[1] = month(sep, 'inProgress', 17500.25, 1900, 500);
    (await overviewCall()).flush(reloaded);
    (await entriesCall('2026-09')).flush(entries([]));
    await settle();
    await vi.waitFor(() => expect(document.querySelector('mat-dialog-container')).toBeNull());
    http.verify();

    expect(text(root().querySelector('.status'))).toBe('Entry deleted.');
    expect(words(card('2026-09').querySelector('.kpi'))).toBe('Remaining SAR 1,400');
    expect(card('2026-09').querySelector('mat-expansion-panel')!.classList).toContain(
      'mat-expanded',
    );
    expect(card('2026-09').querySelector('.entry')).toBeNull();
  });
});
