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
import { Entry } from '../add-entry/entry-api';
import { Category, FiscalYear } from '../plan/plan-api';
import { Months } from './months';
import { MonthDetail } from './month-api';
import { BalancesResponse, MonthOverview, OverviewResponse } from './overview-api';

// Today is 17 Oct 2026; FY 2026 has payday 27, so Sep 2026 (27 Sep to 26 Oct) is in progress. Owner
// note 9: Tracker shows one month at a time (the overview plus that month's detail), and the Year view
// has a Balance row (the overview plus the balances).
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
    monthNaming: 'payday',
    lastBankAccountId: 'rajhi',
  },
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

// Nothing carries in unless a test says so (R-PLN-05: remaining = carried in + planned - spent).
function month(
  period: typeof sep,
  status: MonthOverview['status'],
  income: number,
  planned: number,
  spent: number,
  carriedIn = 0,
): MonthOverview {
  return {
    period,
    status,
    income,
    plannedIncome: income,
    carriedIn,
    planned,
    spent,
    remaining: Math.round((carriedIn + planned - spent) * 100) / 100,
    additionalIncome: 0,
    unspent: null,
    saved: null,
    projectedUnspent: null,
    projectedSaved: null,
  };
}

// Owner note 18: each started month's income minus spent, added up from the FY's first month. Owner
// note 23: carried on to the FY's end, the month in progress spending what is left of its plan and a
// month yet to come adding its income minus its planned.
function withSaved(months: MonthOverview[]): MonthOverview[] {
  const round = (value: number) => Math.round(value * 100) / 100;
  let saved = 0;
  return months.map((item) => {
    if (item.status === 'yetToCome') {
      const projectedUnspent = round((item.income ?? 0) - item.planned);
      saved = round(saved + projectedUnspent);
      return { ...item, projectedUnspent, projectedSaved: saved };
    }
    const unspent = round((item.income ?? 0) - item.spent);
    saved = round(saved + unspent);
    if (item.status === 'closed') {
      return { ...item, unspent, saved };
    }
    const stillToSpend = Math.max(0, item.remaining);
    const result = { ...item, unspent, saved, projectedUnspent: round(unspent - stillToSpend), projectedSaved: round(saved - stillToSpend) };
    saved = result.projectedSaved;
    return result;
  });
}

const cells = (...values: [number, number][]) =>
  values.map(([planned, spent], i) => ({
    periodKey: [aug, sep, oct][i].key,
    carriedIn: 0,
    planned,
    spent,
    remaining: Math.round((planned - spent) * 100) / 100,
  }));

// Taxi (archived) has 30 spent in Aug with no plan; Fuel has nothing in Aug.
function overview(
  fyId = '2026-01',
  statuses: MonthOverview['status'][] = ['closed', 'inProgress', 'yetToCome'],
  fiscalYears = [fy2026],
): OverviewResponse {
  return {
    fiscalYears,
    fyId,
    months: withSaved([
      month(aug, statuses[0], 17000, 1500, 80),
      { ...month(sep, statuses[1], 17500.25, 1900, 1299.99), additionalIncome: 500.25 },
      month(oct, statuses[2], 17000, 1900, 0),
    ]),
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
    totals: { income: 51500.25, plannedIncome: 51500.25, planned: 5300, spent: 1379.99, remaining: 3920.01, plannedSaved: 46200.25, saved: 33120.26, projectedSaved: 47620.25, additionalIncome: 500.25 },
    toDate: { income: 34500.25, planned: 3400, spent: 1379.99, saved: 33120.26, plannedSaved: 31100.25, savedClosed: 17000 },
    planVsActual: null,
    rollover: null,
    carryFromPeriodKey: '2026-08',
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

const fuelEntry: Entry = { ...big, id: 'e1', date: '2026-10-01', categoryId: 'transport', subCategoryId: 'fuel', bankAccountId: 'snb', amount: 100, note: '' };

// Every day of a month, with the given spending on some of them.
function days(period: typeof sep, spent: Record<string, number> = {}): MonthDetail['days'] {
  const list: MonthDetail['days'] = [];
  const day = new Date(`${period.startDate}T00:00:00Z`);
  const end = new Date(`${period.endDate}T00:00:00Z`);
  for (; day <= end; day.setUTCDate(day.getUTCDate() + 1)) {
    const date = day.toISOString().slice(0, 10);
    list.push({ date, spent: spent[date] ?? 0 });
  }
  return list;
}

// Sep 2026 as the API's hand check would give it: Fuel 100 on 1 Oct (SNB) and the 799.99 big shop
// on 17 Oct (Al Rajhi); Aug had Food 50 and Transport 30.
function detail(period = sep, latest: Entry[] = [big, fuelEntry], count = latest.length): MonthDetail {
  const total = latest.reduce((sum, entry) => sum + entry.amount, 0);
  return {
    period,
    lastMonth: period === sep ? aug : null,
    total,
    count,
    days: days(period, Object.fromEntries(latest.map((entry) => [entry.date, entry.amount]))),
    accounts: latest.length
      ? [
          { bankAccountId: 'rajhi', spent: 799.99 },
          { bankAccountId: 'snb', spent: 100 },
        ]
      : [],
    categories: latest.length
      ? [
          { categoryId: 'food', spent: 799.99, lastSpent: 50 },
          { categoryId: 'transport', spent: 100, lastSpent: 130 },
        ]
      : [],
    latest,
    biggest: [...latest].sort((a, b) => b.amount - a.amount),
    bankAccounts: [
      { id: 'rajhi', name: 'Al Rajhi', archived: false },
      { id: 'snb', name: 'SNB', archived: false },
    ],
    additionalIncome: [],
  };
}

// Aug closed at 18,450; Sep and Oct are projected.
const balances: BalancesResponse = {
  complete: true,
  months: [
    { periodKey: '2026-08', balance: 18450, projected: false },
    { periodKey: '2026-09', balance: 22000, projected: true },
    { periodKey: '2026-10', balance: -500, projected: true },
  ],
};

describe('Tracker (PRD 5.6)', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let meValue: Me;

  beforeEach(() => {
    meValue = me;
    localStorage.clear();
    // Most tests are about the Months view; the Totals tests clear this (owner note 18: the default).
    localStorage.setItem('nama.months.view', 'months');
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
    return vi.waitFor(() => http.expectOne((req) => req.url === `/api/fiscal-years/${fyId}/overview`));
  }

  function monthCall(periodKey: string, fyId = '2026-01'): Promise<TestRequest> {
    return vi.waitFor(() => http.expectOne((req) => req.url === `/api/fiscal-years/${fyId}/months/${periodKey}`));
  }

  function balancesCall(fyId = '2026-01'): Promise<TestRequest> {
    return vi.waitFor(() => http.expectOne(`/api/fiscal-years/${fyId}/balances`));
  }

  // NFR-01: the overview, then the shown month's detail (Months view) or the balances (Year view).
  async function open(url = '/months', data = overview(), month: MonthDetail | BalancesResponse = detail()): Promise<void> {
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    (await overviewCall(data.fyId)).flush(data);
    if ('months' in month) {
      (await balancesCall(data.fyId)).flush(month);
    } else {
      (await monthCall(month.period.key, data.fyId)).flush(month);
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

  function section(title: string): HTMLElement {
    return root().querySelector(`[aria-labelledby="${title}-title"]`)!;
  }

  function lines(scope: ParentNode, selector: string): string[] {
    return Array.from(scope.querySelectorAll(selector)).map((el) => words(el));
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

  // MON-01, MON-02, owner note 9: the month in progress first, with its status, numbers and plan lines.
  it('shows the month in progress with its status, numbers and plan lines', async () => {
    await open();

    const summary = section('month');
    expect(summary.classList).toContain('inProgress');
    expect(words(summary.querySelector('.card-head'))).toBe('Sep 2026 27 Sep to 26 Oct In progress');
    // R-PLN-05: Remaining = From last month + Planned - Spent; Income is the month's own.
    expect(lines(summary, '.kpi')).toEqual([
      'Remaining SAR 600.01',
      'From last month SAR 0',
      'Planned SAR 1,900',
      'Spent SAR 1,299.99',
      'Income SAR 17,500.25',
    ]);
    expect(text(summary.querySelector('.bar-line .chip'))).toBe('Within plan');
    expect(summary.querySelector('.remaining .value')!.classList).toContain('ok');
    expect(summary.querySelectorAll('.kpi .value.ok, .kpi .value.warn, .kpi .value.danger').length).toBe(1);

    // Owner note 26: one line per category; a single sub-category is named beside it, not repeated;
    // no chip while within plan.
    expect(lines(section('plan'), '.spend-row')).toEqual([
      'Food Groceries SAR 1,199.99 of 1,500',
      'Transport Fuel SAR 100 of 400',
    ]);
    expect(section('plan').querySelector('.chip')).toBeNull();
    // Owner note 26: Plan vs spent and Compared with last month each go across the page.
    expect([section('days'), section('plan'), section('change')].map((card) => card.classList.contains('wide'))).toEqual([true, true, true]);

    // The month picker, with each month's status.
    expect(root().querySelector('app-month-picker mat-select')?.textContent).toContain('Sep 2026');
    expect(button(root(), 'Next month').disabled).toBe(false);

    // One FY: no selector, the FY as a caption (MON-03).
    expect(text(root().querySelector('.fy'))).toBe('FY 2026 · 27 Jan 2026 to 26 Jan 2027');
    expect(root().textContent).not.toContain('Fiscal year');
  });

  // Owner note 9: spending by day, against last month, by account, the biggest and the newest entries.
  it('shows the month in detail', async () => {
    await open('/months', overview(), detail(sep, [big, fuelEntry], 7));

    const byDay = section('days');
    expect(words(byDay.querySelector('.card-head'))).toBe('Spending by day SAR 899.99 in 7 entries Biggest day: 17 Oct · SAR 799.99');
    const chart = byDay.querySelector('app-day-bars [role="img"]')!;
    expect(chart.getAttribute('aria-label')).toBe('Spending by day: 1 Oct SAR 100, 17 Oct SAR 799.99');
    const columns = byDay.querySelectorAll<HTMLElement>('.col');
    expect(columns.length).toBe(30);
    expect(columns[20].classList).toContain('today');
    expect(columns[21].classList).toContain('future');
    expect(columns[20].querySelector<HTMLElement>('.bar')!.style.height).toBe('100%');
    expect(columns[20].title).toBe('17 Oct: SAR 799.99');

    expect(words(section('change').querySelector('.card-head'))).toBe('Compared with last month Aug 2026');
    expect(lines(section('change'), '.row')).toEqual([
      'Food SAR 749.99 more than Aug 2026 SAR 799.99',
      'Transport SAR 30 less than Aug 2026 SAR 100',
    ]);
    expect(lines(section('accounts'), '.row')).toEqual(['Al Rajhi 89% SAR 799.99', 'SNB 11% SAR 100']);

    expect(lines(section('biggest'), '.entry').map((line) => line.replace(' edit', ''))).toEqual([
      '17 Oct Groceries SAR 799.99',
      '1 Oct Fuel SAR 100',
    ]);
    expect(lines(section('latest'), '.entry')).toHaveLength(2);
    const link = section('latest').querySelector('a.see-all')!;
    expect(text(link)).toBe('See all 7 entries in Sep 2026');
    expect(link.getAttribute('href')).toBe('/entries?fyId=2026-01&periodKey=2026-09');
  });

  // Owner note 17 (R-INC-04): the month lists its additional income by name, and Income is the actual
  // income with Plan's under it when they differ.
  it('lists the month\'s additional income and its planned income', async () => {
    const data = overview();
    data.months[1] = { ...data.months[1], plannedIncome: 17000.25 };
    await open('/months', data, {
      ...detail(),
      additionalIncome: [
        { id: 'g', kind: 'additional', label: 'Gift', amount: 500, type: 'once', fromPeriodKey: '2026-09', toPeriodKey: '2026-09', counts: 'actual', bankAccountId: null },
        { id: 'b', kind: 'additional', label: 'Bonus', amount: 0.25, type: 'once', fromPeriodKey: '2026-09', toPeriodKey: '2026-09', counts: 'both', bankAccountId: null },
      ],
    });

    expect(lines(section('income'), '.row')).toEqual([
      'Gift Actual SAR 500',
      'Bonus Planned and actual SAR 0.25',
    ]);
    expect(words(root().querySelector('app-month-summary'))).toContain('Income SAR 17,500.25 Planned SAR 17,000.25');
  });

  it('shows no additional income card when the month has none', async () => {
    await open('/months', overview(), detail());

    expect(root().querySelector('[aria-labelledby="income-title"]')).toBeNull();
    expect(words(root().querySelector('app-month-summary'))).toContain('Income SAR 17,500.25 Within plan');
  });

  // Owner note 9: the picker steps through the months; the month is kept in the URL and only its
  // detail is loaded again.
  it('picks another month and keeps it in the URL', async () => {
    await open();

    button(root(), 'Previous month').click();
    (await monthCall('2026-08')).flush(detail(aug, []));
    await settle();
    http.verify();

    expect(TestBed.inject(Router).url).toBe('/months?month=2026-08');
    expect(words(section('month').querySelector('.card-head'))).toBe('Aug 2026 27 Aug to 26 Sep Closed');
    expect(button(root(), 'Previous month').disabled).toBe(true);
    // Spending without a plan is over plan (R-PLN-01): Taxi (archived) is listed in Aug.
    // Over plan comes first (owner note 26).
    expect(lines(section('plan'), '.spend-row')[0]).toBe('Transport Taxi (archived) Over by SAR 30 SAR 30 of 0');
    expect(text(section('change').querySelector('.note'))).toBe('This is the first month NAMA has.');
    expect(text(section('latest').querySelector('.note'))).toBe('No entries in Aug 2026.');
  });

  // R-PLN-05: what was left last month carries in. Groceries left 300 in Aug, so Sep has 1,800; Fuel
  // spent 500 of its 400 but 200 carried in, so it is near its limit, not over.
  it('shows what carried in from last month, and counts it in each line', async () => {
    const data = overview();
    data.months[1] = month(sep, 'inProgress', 17500.25, 1900, 1699.99, 500);
    const food = data.rows[0];
    food.months[1] = { ...food.months[1], carriedIn: 300, remaining: 600.01 };
    food.subCategories[0].months[1] = { ...food.subCategories[0].months[1], carriedIn: 300, remaining: 600.01 };
    const transport = data.rows[1];
    transport.months[1] = { ...transport.months[1], carriedIn: 200, spent: 500, remaining: 100 };
    transport.subCategories[0].months[1] = { ...transport.subCategories[0].months[1], carriedIn: 200, spent: 500, remaining: 100 };
    await open('/months', data);

    expect(lines(section('month'), '.kpi').slice(0, 2)).toEqual(['Remaining SAR 700.01', 'From last month SAR 500']);
    // Near limit before within plan (owner note 26).
    expect(lines(section('plan'), '.spend-row')).toEqual([
      'Transport Fuel Near limit SAR 500 of 600',
      'Food Groceries SAR 1,199.99 of 1,800',
    ]);
  });

  // R-PLN-06: set up in Sep 2026, so Aug stands alone and the page says why.
  it('says which month the carry starts in', async () => {
    await open('/months', { ...overview(), carryFromPeriodKey: '2026-09' });

    expect(words(root().querySelector('.carry-from'))).toBe(
      'What is left carries from Sep 2026, the month you started NAMA. Earlier months stand alone.',
    );
  });

  it('says nothing about the carry when it starts with the FY', async () => {
    await open();

    expect(root().querySelector('.carry-from')).toBeNull();
  });

  // UX-03: the Months view says what to do when nothing is planned or spent in the FY.
  it('shows an empty state above the month when nothing is planned or spent', async () => {
    await open('/months', { ...overview(), rows: [] }, detail(sep, []));

    const empty = root().querySelector('.empty')!;
    expect(text(empty.querySelector('h2'))).toBe('Nothing planned or spent in FY 2026 yet');
    expect(Array.from(empty.querySelectorAll('a')).map((a) => a.getAttribute('href'))).toEqual(['/plan?tab=planning', '/add']);
    expect(section('month')).not.toBeNull();
  });

  it('shows no empty state when something is planned or spent', async () => {
    await open();

    expect(root().querySelector('.empty')).toBeNull();
  });

  // Tracker's filter (owner's choice, 20 Oct 2026): the API narrows the sums to Food, income is
  // hidden (it is null), and the month's detail is Food's.
  it('focuses on one category from the URL, without income', async () => {
    const food = overview();
    food.rows = food.rows.slice(0, 1);
    food.months = food.months.map((m, i) => ({ ...m, income: null, planned: 1500, spent: [50, 1199.99, 0][i], remaining: 1500 - [50, 1199.99, 0][i] }));
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/months?categoryId=food');
    (await vi.waitFor(() =>
      http.expectOne((req) => req.url === '/api/fiscal-years/2026-01/overview' && req.params.get('categoryId') === 'food'),
    )).flush(food);
    const sepDetail = await monthCall('2026-09');
    expect(sepDetail.request.params.get('categoryId')).toBe('food');
    sepDetail.flush(detail(sep, [big]));
    await settle();

    expect(words(section('month'))).not.toContain('Income');
    expect(words(section('month'))).toContain('SAR 300.01');
    expect(words(root().querySelector('.filters'))).toContain('Clear filters');
    expect(section('latest').querySelector('a.see-all')).toBeNull();
    http.verify();
  });

  it('writes the category to the URL and clears the sub-category', async () => {
    await open('/months?categoryId=transport&subCategoryId=fuel');
    const page = harness.routeDebugElement!.componentInstance as { setCategory(id: string): void };
    page.setCategory('food');
    await settle();

    expect(TestBed.inject(Router).url).toBe('/months?categoryId=food');
  });

  // Owner note 26: a category with more than one sub-category opens to them, over plan first. Taxi
  // spent 30 with no plan in Sep, so Transport has Fuel and Taxi; Food's 80% comes before its 32%.
  it('opens a category to its sub-categories', async () => {
    const data = overview();
    const transport = data.rows[1];
    transport.months[1] = { ...transport.months[1], spent: 130, remaining: 270 };
    transport.subCategories[1].months[1] = { ...transport.subCategories[1].months[1], spent: 30, remaining: -30 };
    await open('/months', data);

    const plan = section('plan');
    expect(lines(plan, '.spend-row')).toEqual(['Food Groceries SAR 1,199.99 of 1,500', 'chevron_right Transport SAR 130 of 400']);
    const fold = plan.querySelector<HTMLButtonElement>('button.fold-row')!;
    expect(fold.getAttribute('aria-expanded')).toBe('false');

    fold.click();
    await settle();
    expect(fold.getAttribute('aria-expanded')).toBe('true');
    expect(lines(plan, '.subs .spend-row')).toEqual(['Taxi (archived) Over by SAR 30 SAR 30 of 0', 'Fuel SAR 100 of 400']);
    expect(plan.querySelector(`#${fold.getAttribute('aria-controls')}`)).not.toBeNull();

    fold.click();
    await settle();
    expect(plan.querySelector('.subs')).toBeNull();
  });

  // "Over plan only": Taxi went over in Aug (30 with no plan); nothing went over in Sep.
  it('shows only what went over plan', async () => {
    await open('/months?over=1');
    expect(words(section('plan'))).toContain('Nothing over plan in Sep 2026.');

    await harness.navigateByUrl('/months?over=1&month=2026-08');
    (await monthCall('2026-08')).flush(detail(aug, []));
    await settle();
    const plan = words(section('plan'));
    expect(plan).toContain('Taxi (archived)');
    expect(plan).not.toContain('Groceries');
  });

  it('keeps only the rows over plan in the Year view', async () => {
    await open('/months?over=1&view=year', overview(), balances);

    const grid = words(root().querySelector('app-year-grid tbody'));
    expect(grid).toContain('Transport');
    expect(grid).not.toContain('Food');
  });

  // Tracker: actual vs projection (owner note 23), with the way back to Plan for the same FY.
  it('is titled Actual vs projection and links to Plan for the shown FY', async () => {
    await open('/months?fyId=2026-01', overview('2026-01', ['closed', 'closed', 'closed']), detail(aug, []));

    expect(root().querySelector('h1')?.textContent).toBe('Actual vs projection');
    expect(root().querySelector('.page-head a')?.getAttribute('href')).toBe('/plan?fyId=2026-01&tab=planning');
  });

  it("shows the FY's first month when no month is in progress", async () => {
    await open('/months?fyId=2026-01', overview('2026-01', ['closed', 'closed', 'closed']), detail(aug, []));

    expect(words(section('month').querySelector('h2'))).toBe('Aug 2026');
  });

  // MON-03: same default as Entries; with no current FY, the latest.
  it('uses the latest FY when no FY contains today', async () => {
    meValue = { ...me, currentFyId: null, currentPeriodKey: null, currentPeriod: null };
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/months');

    (await vi.waitFor(() => http.expectOne('/api/fiscal-years'))).flush([fy2026, fy2027]);
    (await overviewCall('2027-01')).flush(overview('2027-01', ['yetToCome', 'yetToCome', 'yetToCome'], [fy2026, fy2027]));
    (await monthCall('2026-08', '2027-01')).flush(detail(aug, []));
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
    (await overviewCall('2027-01')).flush(overview('2027-01', ['yetToCome', 'yetToCome', 'yetToCome'], [fy2026, fy2027]));
    (await monthCall('2026-08', '2027-01')).flush(detail(aug, []));
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
    await open('/months', lastMonth, detail(oct, []));
    const banner = root().querySelector('app-rollover-banner')!;
    expect(text(banner)).toContain('FY 2026 ends on 26 Jan 2027.');

    button(banner, 'Start FY 2027').click();
    const post = await vi.waitFor(() => http.expectOne('/api/fiscal-years/next'));
    expect(post.request.body).toEqual({ copyPlans: true });
    post.flush({ fiscalYear: fy2027, copiedPlans: 1, me: { ...me, currentFyId: '2027-01', lastDate: '2028-01-26' } });

    (await overviewCall('2027-01')).flush(overview('2027-01', ['yetToCome', 'yetToCome', 'yetToCome'], [fy2026, fy2027]));
    (await monthCall('2026-08', '2027-01')).flush(detail(aug, []));
    await settle();
    http.verify();

    expect(root().querySelector('app-rollover-banner')).toBeNull();
    expect(text(root().querySelector('.status'))).toBe('FY 2027 started.');
  });

  // Plan's "See the year grid" link (M8).
  it('opens the Year view from the URL', async () => {
    await open('/months?fyId=2026-01&view=year', overview(), balances);

    expect(root().querySelector('app-year-grid')).not.toBeNull();
    expect(localStorage.getItem('nama.months.view')).toBe('year');
  });

  // MON-02: the last choice is remembered; each view loads its own call once.
  it('remembers the view choice', async () => {
    await open();

    button(root(), 'Year').click();
    (await balancesCall()).flush(balances);
    await settle();
    expect(localStorage.getItem('nama.months.view')).toBe('year');
    expect(root().querySelector('table.grid')).not.toBeNull();

    button(root(), 'Months').click();
    await settle();
    http.verify();
    expect(localStorage.getItem('nama.months.view')).toBe('months');
    expect(section('month')).not.toBeNull();
  });

  it('opens on the remembered Year view, and loads the month when Months is shown', async () => {
    localStorage.setItem('nama.months.view', 'year');
    await open('/months', overview(), balances);
    expect(root().querySelector('table.grid')).not.toBeNull();

    button(root(), 'Months').click();
    (await monthCall('2026-09')).flush(detail());
    await settle();
    http.verify();
    expect(section('latest').querySelector('.entry')).not.toBeNull();
  });

  it('works without storage', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    await open('/months', overview(), balances);
    expect(root().querySelector('app-tracker-totals')).not.toBeNull();

    button(root(), 'Months').click();
    (await monthCall('2026-09')).flush(detail());
    await settle();
    expect(section('month')).not.toBeNull();
  });

  // Owner note 18: Totals is the first view and the default; it shares the balances with Year.
  it('opens on Totals when nothing is remembered, before Year and Months', async () => {
    localStorage.clear();
    await open('/months', overview(), balances);

    expect(lines(root(), 'mat-button-toggle')).toEqual(['Totals', 'Year', 'Months']);
    expect(root().querySelector('app-tracker-totals')).not.toBeNull();
    // Totals has no lines to leave out.
    expect(root().textContent).not.toContain('Over plan only');

    button(root(), 'Year').click();
    await settle();
    http.verify();
    expect(root().querySelector('table.grid')).not.toBeNull();
    expect(root().textContent).toContain('Over plan only');
  });

  function totalsRows(): string[] {
    return lines(root(), 'app-tracker-totals tbody tr');
  }

  // Owner note 18: Allocating's table with what really happened. Aug kept 17,000 - 80 and Sep
  // 17,500.25 - 1,299.99, added up from Aug; Oct has not started. Owner note 23: Sep still has
  // 1,900 - 1,299.99 = 600.01 of its plan to spend (33,120.26 - 600.01 = 32,520.25), then Oct adds
  // 17,000 - 1,900 = 15,100, so the FY ends at 47,620.25.
  it('shows the totals per month: what came in, went out and was kept', async () => {
    await open('/months?view=totals', overview(), balances);

    const card = section('totals');
    expect(words(card.querySelector('h2'))).toBe('Totals per month (SAR)');
    expect(words(card.querySelector('.saved-kpi'))).toBe('Accumulated savings so far in FY 2026 SAR 33,120.26');
    expect(words(card.querySelector('.projected-kpi'))).toBe('Projected by the end of FY 2026 SAR 47,620.25');
    expect(lines(card, 'thead th')).toEqual(['Aug 2026', 'Sep 2026', 'Oct 2026', 'FY total']);
    expect(totalsRows()).toEqual([
      'Income 17,000 17,500.25 17,000 51,500.25',
      'Additional income Actual and Both, in Income – 500.25 – 500.25',
      'Spent 80 1,299.99 – 1,379.99',
      'Unspent this month Income − Spent 16,920 16,200.26 15,100 Projected 33,120.26 so far',
      'Accumulated savings Unspent this month, added up from Aug 2026 16,920 33,120.26 32,520.25 Projected 47,620.25 Projected 33,120.26 so far',
      'Balance End of month 18,450 Actual 22,000 Projected -500 Projected -500 End of FY',
    ]);
    // The month in progress is marked in its header and every row.
    expect(card.querySelector('thead th.now')?.getAttribute('title')).toBe('Sep 2026 · In progress');
    expect(card.querySelectorAll('tbody td.now').length).toBe(6);
    // Aug, Sep, Oct (projected) and the FY are green.
    expect(card.querySelectorAll('tr.result td.ok').length).toBe(4);
    expect(text(card.querySelector('.totals-note'))).toContain('It is not your balance');
  });

  // Owner note 23: the plan is on Plan, so neither Planned income nor Planned is shown, even when they differ.
  it('shows no planned rows, and spending above income in red', async () => {
    const data = overview();
    data.months = withSaved(data.months.map((item, i) => (i === 0 ? { ...item, plannedIncome: 18000, spent: 20000 } : item)));
    data.totals = { ...data.totals, plannedIncome: 52500.25, spent: 21299.99, saved: 13200.26 };
    await open('/months?view=totals', data, balances);

    const rows = totalsRows();
    expect(rows.filter((row) => row.startsWith('Planned'))).toEqual([]);
    expect(rows[3]).toBe('Unspent this month Income − Spent -3,000 16,200.26 15,100 Projected 13,200.26 so far');
    const unspent = section('totals').querySelectorAll('tr.subtotal td');
    expect(unspent[0].classList).toContain('danger');
    expect(unspent[1].classList).toContain('ok');
  });

  // Owner note 23: a month yet to come that plans more than its income lowers the projection.
  it('shows a projected month below 0 in red', async () => {
    const data = overview();
    data.months = withSaved(data.months.map((item, i) => (i === 2 ? { ...item, income: 0 } : item)));
    data.totals = { ...data.totals, projectedSaved: 30620.25 };
    await open('/months?view=totals', data, balances);

    const unspent = section('totals').querySelectorAll('tr.subtotal td');
    expect(text(unspent[2])).toBe('-1,900 Projected');
    expect(unspent[2].classList).toContain('danger');
    expect(text(section('totals').querySelectorAll('tr.result td')[2])).toBe('30,620.25 Projected');
    expect(words(section('totals').querySelector('.projected-kpi'))).toBe('Projected by the end of FY 2026 SAR 30,620.25');
  });

  // Before the FY starts nothing is saved yet: the head shows only the projection.
  it('shows only the projection before the FY starts', async () => {
    const data = overview('2026-01', ['yetToCome', 'yetToCome', 'yetToCome']);
    data.totals = { ...data.totals, saved: null, projectedSaved: 46700.25 };
    await open('/months?view=totals', data, balances);

    const card = section('totals');
    expect(card.querySelector('.saved-kpi:not(.projected-kpi)')).toBeNull();
    expect(words(card.querySelector('.projected-kpi'))).toBe('Projected by the end of FY 2026 SAR 46,700.25');
    expect(text(card.querySelectorAll('tr.result td')[0])).toBe('15,500 Projected');
  });

  // MON-05: income, savings and the balance belong to the whole budget.
  it('shows only Spent for a category', async () => {
    const data = overview();
    data.months = data.months.map((item) => ({
      ...item,
      income: null,
      plannedIncome: null,
      additionalIncome: null,
      unspent: null,
      saved: null,
      projectedUnspent: null,
      projectedSaved: null,
    }));
    data.totals = { ...data.totals, income: null, plannedIncome: null, additionalIncome: null, plannedSaved: null, saved: null, projectedSaved: null };
    await open('/months?view=totals&categoryId=food', data, balances);

    expect(totalsRows()).toEqual(['Spent 80 1,299.99 – 1,379.99']);
    expect(section('totals').querySelector('.saved-kpi')).toBeNull();
    expect(text(section('totals').querySelector('.totals-note'))).toContain('hidden while a category is picked');
  });

  // MON-02 Year view and MON-04 links; owner note 9: every value has its label.
  it('shows the year grid with month statuses, labels, colors and links to Entries', async () => {
    await open('/months?view=year', overview(), balances);

    const headers = Array.from(root().querySelectorAll('thead th')).map((th) => words(th));
    expect(headers).toEqual(['Category', 'Aug 2026 Closed', 'Sep 2026 In progress', 'Oct 2026 Upcoming', 'FY total']);
    // The in-progress column is marked in every row (Food, Transport, Total, Balance).
    expect(root().querySelectorAll('td.now').length).toBe(4);

    const food = cell('Food, Sep 2026');
    expect(words(food)).toBe('Spent 1,199.99 Plan 1,500');
    expect(food.classList).toContain('ok');
    expect(food.getAttribute('aria-label')).toBe('Food, Sep 2026: SAR 1,199.99 of SAR 1,500 plan, Within plan. Open entries');
    expect(food.getAttribute('href')).toBe('/entries?fyId=2026-01&periodKey=2026-09&categoryId=food');
    expect(cell('Food, FY 2026').getAttribute('href')).toBe('/entries?fyId=2026-01&categoryId=food');
    expect(cell('Total, Sep 2026').getAttribute('href')).toBe('/entries?fyId=2026-01&periodKey=2026-09');
    expect(words(cell('Total, Sep 2026'))).toBe('Spent 1,299.99 Plan 1,900');
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
    expect(cell('Fuel, Sep 2026').getAttribute('href')).toBe('/entries?fyId=2026-01&periodKey=2026-09&subCategoryId=fuel');
    expect(cell('Fuel, FY 2026').getAttribute('href')).toBe('/entries?fyId=2026-01&subCategoryId=fuel');
    // Nothing planned and nothing spent: a dash, no color.
    const fuelAug = cell('Fuel, Aug 2026');
    expect(text(fuelAug)).toBe('–');
    expect(fuelAug.getAttribute('aria-label')).toBe('Fuel, Aug 2026: nothing planned or spent. Open entries');
    expect(cell('Taxi (archived), Aug 2026').classList).toContain('danger');
    http.verify();
  });

  // Owner note 9: the balance at each month's end, real for closed months, projected after; the FY
  // column is the last month's.
  it('shows the balance at the end of each month in the Year view', async () => {
    await open('/months?view=year', overview(), balances);

    const row = root().querySelector('tr.balance-row')!;
    expect(words(row.querySelector('th'))).toBe('Balance End of month');
    expect(lines(row, 'td')).toEqual(['18,450 Actual', '22,000 Projected', '-500 Projected', '-500 End of FY']);
    expect(row.querySelectorAll('td .money.danger').length).toBe(2);
    expect(row.querySelector('td .cell')!.getAttribute('aria-label')).toBe('Aug 2026: balance at the end SAR 18,450');
  });

  it('asks for the balances before it shows them', async () => {
    await open('/months?view=year', overview(), { complete: false, months: [] });

    const row = root().querySelector('tr.balance-row')!;
    expect(words(row.querySelector('td'))).toBe("Set every account's balance and the income split on Banks to see it.");
    expect(row.querySelector('td a')!.getAttribute('href')).toBe('/banks');
  });

  // TBL-03, UX-04: the Entries dialog; afterwards the overview and the month reload.
  it('deletes an entry and reloads the overview and the month', async () => {
    await open();
    button(section('latest'), 'Edit Groceries, SAR 799.99, 17 Oct 2026').click();
    (await vi.waitFor(() => http.expectOne('/api/indicator?subCategoryId=groceries&date=2026-10-17'))).flush({
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
    (await monthCall('2026-09')).flush(detail(sep, [fuelEntry]));
    await settle();
    await vi.waitFor(() => expect(document.querySelector('mat-dialog-container')).toBeNull());
    http.verify();

    expect(text(root().querySelector('.status'))).toBe('Entry deleted.');
    expect(words(section('month').querySelector('.kpi'))).toBe('Remaining SAR 1,400');
    expect(lines(section('latest'), '.entry .name')).toEqual(['Fuel']);
  });
});
