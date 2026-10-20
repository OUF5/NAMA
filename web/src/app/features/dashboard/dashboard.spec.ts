import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { WritableSignal, signal } from '@angular/core';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestBed } from '@angular/core/testing';
import { MatFormFieldHarness } from '@angular/material/form-field/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Me, MeService } from '../../core/me';
import { Summary, SummaryService } from '../../core/summary';
import { Cell, MonthOverview, OverviewResponse } from '../months/overview-api';
import { Category, FiscalYear } from '../plan/plan-api';
import { Dashboard } from './dashboard';

// The M7 hand check, today 17 Oct 2026: FY 2026 with payday 27, Sep 2026 (27 Sep to 26 Oct) in
// progress. Salary 15,000 + rent 2,000 every month and a 500.25 bonus in Sep; Groceries 1,500
// every month and Fuel 400 from Sep; spent 50 in Aug and 1,299.99 in Sep.
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const periods = MONTHS.map((name, i) => {
  const m = i + 1;
  const pad = (n: number) => String(n).padStart(2, '0');
  const next = m === 12 ? '2027-01' : `2026-${pad(m + 1)}`;
  return {
    key: `2026-${pad(m)}`,
    label: `${name} 2026`,
    startDate: `2026-${pad(m)}-27`,
    endDate: `${next}-26`,
  };
});
const fy2026: FiscalYear = {
  id: '2026-01',
  label: 'FY 2026',
  startDate: '2026-01-27',
  endDate: '2027-01-26',
  isTransition: false,
  periods,
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
  currentPeriod: periods[8],
  today: '2026-10-17',
  firstDate: '2026-01-27',
  lastDate: '2027-01-26',
};

// The API hand check, carried (R-PLN-05): Jan to Jul leave 1,500 each, Aug leaves 11,950 and Sep 12,550.01.
function months(statuses = 'CCCCCCCCIYYY'): MonthOverview[] {
  const status = { C: 'closed', I: 'inProgress', Y: 'yetToCome' } as const;
  let carried = 0;
  return periods.map((period, i) => {
    const income = i === 8 ? 17500.25 : 17000;
    const planned = i >= 8 ? 1900 : 1500;
    const spent = i === 7 ? 50 : i === 8 ? 1299.99 : 0;
    const carriedIn = carried / 100;
    carried += Math.round(planned * 100) - Math.round(spent * 100);
    return {
      period,
      status: status[statuses[i] as keyof typeof status],
      income,
      carriedIn,
      planned,
      spent,
      remaining: carried / 100,
    };
  });
}

// Carried cells of one sub-category (R-PLN-05), from 0 in Jan.
function cells(planned: (i: number) => number, spent: (i: number) => number): Cell[] {
  let carried = 0;
  return periods.map((period, i) => {
    const carriedIn = carried / 100;
    carried += Math.round(planned(i) * 100) - Math.round(spent(i) * 100);
    return { periodKey: period.key, carriedIn, planned: planned(i), spent: spent(i), remaining: carried / 100 };
  });
}

// Groceries (Food) 1,500 a month, 50 spent in Aug and 1,200 in Sep; Fuel (Transport) 400 from Sep,
// 99.99 spent in Sep. Together they are the months above.
const groceries = cells(() => 1500, (i) => (i === 7 ? 50 : i === 8 ? 1200 : 0));
const fuel = cells((i) => (i >= 8 ? 400 : 0), (i) => (i === 8 ? 99.99 : 0));

const categories: Category[] = [
  {
    id: 'food',
    name: 'Food',
    sortOrder: 0,
    archived: false,
    subCategories: [{ id: 'groceries', categoryId: 'food', name: 'Groceries', sortOrder: 0, archived: false }],
  },
  {
    id: 'transport',
    name: 'Transport',
    sortOrder: 1,
    archived: false,
    subCategories: [{ id: 'fuel', categoryId: 'transport', name: 'Fuel', sortOrder: 0, archived: false }],
  },
];

function overview(statuses?: string): OverviewResponse {
  return {
    fiscalYears: [fy2026],
    fyId: '2026-01',
    months: months(statuses),
    categories,
    rows: [
      {
        categoryId: 'food',
        months: groceries,
        planned: 18000,
        spent: 1250,
        subCategories: [{ subCategoryId: 'groceries', months: groceries, planned: 18000, spent: 1250 }],
      },
      {
        categoryId: 'transport',
        months: fuel,
        planned: 1600,
        spent: 99.99,
        subCategories: [{ subCategoryId: 'fuel', months: fuel, planned: 1600, spent: 99.99 }],
      },
    ],
    totals: { income: 204500.25, planned: 19600, spent: 1349.99, remaining: 18250.01, plannedSaved: 184900.25 },
    toDate: {
      income: 153500.25,
      planned: 13900,
      spent: 1349.99,
      saved: 152150.26,
      plannedSaved: 139600.25,
      savedClosed: 135950,
    },
    rollover: null,
    carryFromPeriodKey: '2026-01',
  };
}

// The API's SummaryTests hand check (R-PLN-07, R-PLN-08): 17 Oct is day 21 of 30 in Sep 2026, Sep has
// 12,250.01 left to spend, Oct to Dec leave 15,500 each, and the balance today is 300.01.
function summary(): Summary {
  return {
    balance: 300.01,
    complete: true,
    accounts: [
      { id: 'rajhi', name: 'Al Rajhi', balance: 500 },
      { id: 'bank', name: 'SNB', balance: -199.99 },
    ],
    month: {
      period: periods[8],
      day: 21,
      days: 30,
      daysLeft: 10,
      income: 17000,
      carriedIn: 11950,
      planned: 1500,
      spent: 1199.99,
      remaining: 12250.01,
      unassigned: 139500,
    },
    projection: {
      fiscalYearLabel: 'FY 2026',
      endDate: '2027-01-26',
      balanceToday: 300.01,
      stillToSpend: 12250.01,
      futureUnallocated: 46500,
      futureMonths: periods.slice(9).map((period) => ({ period, unallocated: 15500 })),
      projectedBalance: 34550,
      savedToDate: 151750.01,
      plannedSaved: 186000,
      projectedSaving: 186000,
    },
  };
}

const rollover = {
  fyId: '2026-01',
  label: 'FY 2026',
  endDate: '2027-01-26',
  nextLabel: 'FY 2027',
  nextStartDate: '2027-01-27',
};

describe('Dashboard (PRD 5.9)', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let meValue: Me;
  let meSignal: WritableSignal<Me | null>;

  beforeEach(() => {
    meValue = me;
    meSignal = signal(me);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '', component: Dashboard }]),
        {
          provide: MeService,
          useValue: {
            me: meSignal,
            load: async () => meValue,
            set: (value: Me) => {
              meValue = value;
              meSignal.set(value);
            },
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  async function settle(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  function overviewCall(fyId = '2026-01'): Promise<TestRequest> {
    return vi.waitFor(() => http.expectOne(`/api/fiscal-years/${fyId}/overview`));
  }

  // NFR-01: one call, the overview (/api/me is cached by setupGuard; the summary is the shell's).
  async function open(data = overview(), withSummary: Summary | null = summary()): Promise<void> {
    TestBed.inject(SummaryService).summary.set(withSummary);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/');
    (await overviewCall(data.fyId)).flush(data);
    await settle();
    http.verify();
  }

  function root(): HTMLElement {
    return harness.routeNativeElement!;
  }

  function card(title: string): Element {
    return root().querySelector(`[aria-labelledby="${title}-title"]`)!;
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

  function button(scope: ParentNode, label: string): HTMLButtonElement {
    return Array.from(scope.querySelectorAll('button')).find(
      (el) => el.textContent?.trim() === label,
    )!;
  }

  function lines(scope: Element, selector: string): string[] {
    return Array.from(scope.querySelectorAll(selector)).map((el) => text(el));
  }

  // Owner note 8: the chart first, then the cards in this order, every card with the month's border.
  it('shows the FY and the cards in order', async () => {
    await open();

    expect(text(root().querySelector('.page-head'))).toBe('Dashboard FY 2026 · 27 Jan 2026 to 26 Jan 2027');
    expect(root().textContent).not.toContain('Preview');
    expect(Array.from(root().querySelectorAll('.cards > section')).map((el) => el.getAttribute('aria-labelledby'))).toEqual([
      'bars-title',
      'current-title',
      'projection-title',
      'to-date-title',
      'saving-title',
      'banks-title',
    ]);
    expect(root().querySelectorAll('.cards > section.card').length).toBe(6);
    expect(root().querySelector('app-rollover-banner')).toBeNull();
  });

  // DSH-01: label and dates, Remaining in its status color with the chip, the bar of spent against
  // what was available (11,950 + 1,900), today's marker, and the month's numbers.
  it('shows the current month with its numbers, bar and day', async () => {
    await open();

    const current = card('current');
    expect(text(current.querySelector('.card-head'))).toBe('Sep 2026 27 Sep to 26 Oct In progress');
    expect(text(current.querySelector('.hero-line'))).toBe('SAR 12,550.01 Within plan');
    expect(current.querySelector('.hero')!.classList).toContain('ok');
    expect(current.querySelector('mat-progress-bar')!.getAttribute('aria-valuenow')).toBe(
      String((129999 * 100) / 1385000),
    ); // 9%
    expect(current.querySelector<HTMLElement>('.marker')!.style.left).toBe('70%');
    expect(text(current.querySelector('.day'))).toBe('Day 21 of 30 · 10 days left');
    expect(lines(current, '.mini > div')).toEqual([
      'From last month SAR 11,950',
      'Planned SAR 1,900',
      'Spent SAR 1,299.99',
      'Income SAR 17,500.25',
    ]);
    expect(text(current.querySelector('.note'))).toBe('Every sub-category is within plan.');
  });

  it('has no day marker until the summary is loaded', async () => {
    await open(overview(), null);

    expect(card('current').querySelector('.marker')).toBeNull();
    expect(card('current').querySelector('.day')).toBeNull();
    expect(card('projection')).toBeNull();
    expect(card('banks')).toBeNull();
  });

  // DSH-01: the sub-categories near or over their plan this month, over plan first, each a link to
  // Tracker filtered to it.
  it('lists the sub-categories to watch this month', async () => {
    const data = overview();
    const nearGroceries = cells(() => 1500, (i) => (i === 8 ? 1300 : 1500)); // Sep: 1,300 of 1,500, 86%
    const overFuel = cells((i) => (i >= 8 ? 400 : 0), (i) => (i === 8 ? 450 : 0)); // Sep: over by 50
    data.rows[0].subCategories[0].months = nearGroceries;
    data.rows[1].subCategories[0].months = overFuel;
    await open(data);

    const items = card('current').querySelectorAll('.watch li');
    expect(Array.from(items).map((li) => text(li))).toEqual([
      'Fuel SAR 450 of SAR 400 Over by SAR 50',
      'Groceries SAR 1,300 of SAR 1,500 Near limit',
    ]);
    expect(items[0].querySelector('a')!.getAttribute('href')).toBe(
      '/tracker?fyId=2026-01&categoryId=transport&subCategoryId=fuel',
    );
  });

  // DSH-05, R-PLN-07: the projected balance and the lines that make it; "How?" explains and lists the
  // months yet to come.
  it('shows the projection and how it adds up', async () => {
    await open();

    const projection = card('projection');
    expect(text(projection.querySelector('.card-head'))).toBe('Projection End of FY 2026 · 26 Jan 2027');
    expect(text(projection.querySelector('.hero'))).toBe('SAR 34,550');
    expect(lines(projection, '.breakdown > div')).toEqual([
      'Balance today SAR 300.01',
      '− Still to spend in Sep 2026 SAR 12,250.01',
      '+ Future unallocated SAR 46,500',
      '= Projected balance SAR 34,550',
    ]);
    expect(projection.querySelector('.danger')).toBeNull();

    const fold = button(projection, 'chevron_right How?');
    expect(fold.getAttribute('aria-expanded')).toBe('false');
    expect(projection.querySelector('.months')).toBeNull();
    fold.click();
    harness.detectChanges();
    expect(fold.getAttribute('aria-expanded')).toBe('true');
    expect(lines(projection, '.months > div')).toEqual([
      'Oct 2026 SAR 15,500',
      'Nov 2026 SAR 15,500',
      'Dec 2026 SAR 15,500',
    ]);
  });

  // UX-07: a projected balance below 0 says so in words.
  it('says when the projected balance is below 0', async () => {
    const data = summary();
    data.projection = { ...data.projection!, projectedBalance: -1200, futureUnallocated: -500 };
    await open(overview(), data);

    const projection = card('projection');
    expect(text(projection.querySelector('.hero'))).toBe('SAR -1,200');
    expect(projection.querySelector('.hero')!.classList).toContain('danger');
    expect(text(projection.querySelector('.sub'))).toBe('Below 0 · if you spend exactly what is planned from now on');
  });

  // BNK-05: without a complete balance there is nothing to project from.
  it('asks for the balances before it projects', async () => {
    const data = summary();
    data.projection = { ...data.projection!, balanceToday: null, projectedBalance: null };
    await open(overview(), data);

    const projection = card('projection');
    expect(text(projection.querySelector('.note'))).toBe(
      'Set the opening balance of every account and the income split on Banks to see the projection.',
    );
    expect(projection.querySelector('.note a')!.getAttribute('href')).toBe('/banks');
    expect(projection.querySelector('.breakdown')).toBeNull();
  });

  it('explains when no month is in progress', async () => {
    await open(overview(), { ...summary(), month: null, projection: null });

    expect(text(card('projection'))).toBe('Projection The projection shows while a month of a fiscal year is in progress.');
    expect(card('saving')).toBeNull();
  });

  // DSH-02 (owner's choice, M9): the started months, Jan to Sep on 17 Oct 2026, with planned until now.
  it('shows the FY to date', async () => {
    await open();

    const toDate = card('to-date');
    expect(text(toDate.querySelector('.card-head'))).toBe('FY 2026 to date Jan 2026 to Sep 2026 · 9 of 12 months');
    expect(lines(toDate, '.kpi')).toEqual([
      'Income SAR 153,500.25',
      'Planned until now SAR 13,900',
      // 13,900 − 1,349.99
      'Spent SAR 1,349.99 SAR 12,550.01 under plan',
      // R-PLN-03: Sep's Unassigned is 139,600.25.
      'Saved SAR 152,150.26 SAR 12,550.01 more than planned',
    ]);
    expect(toDate.querySelector('.danger')).toBeNull();
  });

  // R-PLN-06: set up in Sep 2026, so to date starts there.
  it('counts to date from the month NAMA was set up', async () => {
    await open({ ...overview(), carryFromPeriodKey: '2026-09' });

    expect(text(card('to-date').querySelector('.card-head'))).toBe('FY 2026 to date Sep 2026 · 1 of 12 months');
  });

  // UX-07: overspending and a negative saved have a text label, not only a color.
  it('says when more was spent than planned or earned', async () => {
    const data = overview();
    data.toDate = { ...data.toDate, spent: 201349.99, saved: -47849.74 };
    await open(data);

    const toDate = card('to-date');
    expect(text(toDate.querySelector('.spent'))).toBe('Spent SAR 201,349.99 SAR 187,449.99 over plan');
    expect(text(toDate.querySelector('.saved'))).toBe('Saved SAR -47,849.74 Spent more than income');
    expect(toDate.querySelector('.saved .value')!.classList).toContain('danger');
  });

  // DSH-06, R-PLN-08: on track while nothing is overspent.
  it('compares the planned saving with the projection', async () => {
    await open();

    const saving = card('saving');
    expect(text(saving.querySelector('.card-head'))).toBe('Saving: plan vs projection By the end of FY 2026 On track');
    expect(saving.querySelector('.card-head .chip')!.classList).toContain('ok');
    expect(lines(saving, '.kpi')).toEqual(['Planned saving SAR 186,000', 'Projected saving SAR 186,000']);
    expect(text(saving.querySelector('.reason'))).toBe('Your plan is covered so far.');

    button(saving, 'chevron_right How?').click();
    harness.detectChanges();
    expect(lines(saving, '.breakdown > div')).toEqual([
      'Saved so far SAR 151,750.01',
      '− Still to spend this month SAR 12,250.01',
      '+ Future unallocated SAR 46,500',
      '= Projected saving SAR 186,000',
    ]);
  });

  it('says how far below plan an overspend leaves the saving', async () => {
    const data = summary();
    data.projection = { ...data.projection!, stillToSpend: 0, savedToDate: 139000, projectedSaving: 185500 };
    await open(overview(), data);

    const saving = card('saving');
    expect(text(saving.querySelector('.card-head .chip'))).toBe('SAR 500 below plan');
    expect(saving.querySelector('.card-head .chip')!.classList).toContain('danger');
    expect(text(saving.querySelector('.reason'))).toBe('You have spent more than planned so far.');
  });

  // DSH-03: a column chart, one Planned and one Spent column per month, on one scale (the largest
  // value, 1,900). The month in progress is written out under the chart; a tap picks another month.
  // R-PLN-05: the status counts what carried in, so Aug here carries nothing in and goes over.
  it('shows planned vs spent per month as a column chart', async () => {
    const data = overview();
    data.months[7] = { ...data.months[7], carriedIn: 0, spent: 1600, remaining: -100 };
    await open(data);

    const chart = root().querySelector('app-month-bars')!;
    const cols = Array.from(chart.querySelectorAll<HTMLButtonElement>('button.col'));
    expect(cols.map((col) => text(col.querySelector('.month')))).toEqual(MONTHS);
    expect(text(chart.querySelector('.axis'))).toBe('1,900 0');

    const heights = (col: Element) =>
      Array.from(col.querySelectorAll<HTMLElement>('.bar')).map((bar) => bar.style.height);
    expect(heights(cols[8])).toEqual(['100%', `${(129999 * 100) / 190000}%`]);
    expect(heights(cols[7])).toEqual([`${(150000 * 100) / 190000}%`, `${(160000 * 100) / 190000}%`]);
    expect(cols[7].querySelector('.bar.spent')!.classList).toContain('danger');
    expect(cols[8].querySelector('.bar.spent')!.classList).toContain('ok');

    // UX-07: every value is also text.
    expect(cols[8].getAttribute('aria-label')).toBe(
      'Sep 2026, In progress, planned SAR 1,900, spent SAR 1,299.99, Within plan',
    );
    expect(cols[8].getAttribute('aria-pressed')).toBe('true');
    expect(text(chart.querySelector('.detail'))).toBe(
      'Sep 2026 In progress Within plan Planned SAR 1,900 · Spent SAR 1,299.99 · From last month SAR 11,950',
    );
    expect(text(chart.querySelector('.over'))).toBe('Over plan: Aug 2026 (Over by SAR 100)');

    cols[7].click();
    harness.detectChanges();
    expect(text(chart.querySelector('.detail'))).toBe(
      'Aug 2026 Over by SAR 100 Planned SAR 1,500 · Spent SAR 1,600',
    );
    expect(cols[7].getAttribute('aria-pressed')).toBe('true');
    expect(cols[8].getAttribute('aria-pressed')).toBe('false');
  });

  it('lists no over-plan months when every month is within plan', async () => {
    await open();

    expect(root().querySelector('app-month-bars .over')).toBeNull();
  });

  // DSH-03: the category picker narrows the chart to that category's cells (the API's sums).
  it('narrows the chart to one category', async () => {
    await open();
    const field = await TestbedHarnessEnvironment.loader(harness.fixture).getHarness(
      MatFormFieldHarness.with({ floatingLabelText: 'Category' }),
    );
    const select = (await field.getControl(MatSelectHarness))!;
    await select.open();
    expect(await Promise.all((await select.getOptions()).map((option) => option.getText()))).toEqual([
      'All categories',
      'Food',
      'Transport',
    ]);

    await select.clickOptions({ text: 'Transport' });
    harness.detectChanges();

    const chart = root().querySelector('app-month-bars')!;
    expect(text(chart.querySelector('.axis'))).toBe('400 0');
    expect(text(chart.querySelector('.detail'))).toBe('Sep 2026 In progress Within plan Planned SAR 400 · Spent SAR 99.99');
  });

  // UX-03
  it('shows an empty state when nothing is planned or spent', async () => {
    await open({ ...overview(), rows: [] });

    const empty = card('bars').querySelector('.empty')!;
    expect(text(empty.querySelector('.empty-title'))).toBe('Nothing planned or spent in FY 2026 yet');
    expect(Array.from(empty.querySelectorAll('a')).map((a) => a.getAttribute('href'))).toEqual([
      '/plan',
      '/add',
    ]);
    expect(root().querySelector('app-month-bars')).toBeNull();
    expect(card('bars').querySelector('mat-form-field')).toBeNull();
  });

  // DSH-04: the bank total, what the closed months added, and a pie of the accounts above 0; SNB is
  // below 0, so it is listed under the pie instead.
  it('shows the total balance as a pie by account', async () => {
    await open();

    const banks = card('banks');
    expect(text(banks.querySelector('.card-head'))).toBe('Total balance Today · 2 accounts Open Banks');
    expect(text(banks.querySelector('.hero'))).toBe('SAR 300.01');
    expect(text(banks.querySelector('.added'))).toBe(
      'This FY added SAR 135,950 · income minus spent in the closed months',
    );
    const pie = banks.querySelector('app-bank-pie')!;
    expect(pie.querySelectorAll('.slice').length).toBe(1);
    expect(pie.querySelector('svg')!.getAttribute('aria-label')).toBe('Balances by account: Al Rajhi SAR 500, 100%');
    expect(lines(pie, '.pie-row .legend li')).toEqual(['Al Rajhi 100% SAR 500']);
    expect(lines(pie, '.outside li')).toEqual(['SNB SAR -199.99']);
    expect(pie.querySelector('.outside .danger')).not.toBeNull();
  });

  // Each account keeps its color by its place in the list, and the slices go round the whole pie.
  it('gives each account its own slice and color', async () => {
    const data = summary();
    data.balance = 4000;
    data.accounts = [
      { id: 'a', name: 'A', balance: 1000 },
      { id: 'b', name: 'B', balance: 0 },
      { id: 'c', name: 'C', balance: 3000 },
    ];
    await open(overview(), data);

    const pie = card('banks').querySelector('app-bank-pie')!;
    const slices = Array.from(pie.querySelectorAll<SVGPathElement>('path.slice'));
    expect(slices.map((slice) => slice.style.fill)).toEqual(['var(--series-1)', 'var(--series-3)']);
    // A from 12 o'clock to 3 o'clock (25%), C from there round to 12 o'clock (75%, the large arc).
    expect(slices.map((slice) => slice.getAttribute('d'))).toEqual([
      'M 100 100 L 100 4 A 96 96 0 0 1 196 100 Z',
      'M 100 100 L 196 100 A 96 96 0 1 1 100 4 Z',
    ]);
    expect(lines(pie, '.pie-row .legend li')).toEqual(['A 25% SAR 1,000', 'C 75% SAR 3,000']);
    expect(lines(pie, '.outside li')).toEqual(['B SAR 0']);
  });

  it('asks for the balances before it shows the total', async () => {
    await open(overview(), { ...summary(), balance: null, complete: false });

    const banks = card('banks');
    expect(text(banks.querySelector('.note'))).toBe(
      'Set the opening balance of every account and the income split on Banks to see your total.',
    );
    expect(banks.querySelector('app-bank-pie')).toBeNull();
  });

  // FY-01 after the latest FY ended: the latest FY (GET /api/fiscal-years), no month in progress,
  // the banner; Start reloads the overview of the new FY once.
  it('shows the banner when no FY contains today and starts the next FY', async () => {
    meValue = { ...me, currentFyId: null, currentPeriodKey: null, currentPeriod: null, today: '2027-02-03' };
    meSignal.set(meValue);
    TestBed.inject(SummaryService).summary.set({ ...summary(), month: null, projection: null });
    const ended = { ...overview('CCCCCCCCCCCC'), rollover };
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/');
    (await vi.waitFor(() => http.expectOne('/api/fiscal-years'))).flush([fy2026]);
    (await overviewCall()).flush(ended);
    await settle();
    http.verify();

    expect(text(root().querySelector('.current'))).toBe(
      'Current month No month is in progress. FY 2026 ended on 26 Jan 2027. Start FY 2027 above to keep adding entries.',
    );
    const banner = root().querySelector('app-rollover-banner')!;
    expect(text(banner)).toContain('FY 2026 ended on 26 Jan 2027.');

    button(banner, 'Start FY 2027').click();
    const post = await vi.waitFor(() => http.expectOne('/api/fiscal-years/next'));
    expect(post.request.body).toEqual({ copyPlans: true });
    post.flush({
      fiscalYear: fy2027,
      copiedPlans: 1,
      me: { ...meValue, currentFyId: '2027-01', lastDate: '2028-01-26' },
    });
    (await overviewCall('2027-01')).flush({
      ...overview(),
      fiscalYears: [fy2026, fy2027],
      fyId: '2027-01',
      months: [{ ...months()[0], period: fy2027.periods[0], status: 'inProgress' }],
    });
    await settle();
    http.verify();

    expect(root().querySelector('app-rollover-banner')).toBeNull();
    expect(text(root().querySelector('.status'))).toBe('FY 2027 started.');
    expect(text(root().querySelector('.current h2'))).toBe('Jan 2027');
  });
});
