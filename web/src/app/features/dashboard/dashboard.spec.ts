import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { WritableSignal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Me, MeService } from '../../core/me';
import { MonthOverview, OverviewResponse } from '../months/overview-api';
import { FiscalYear } from '../plan/plan-api';
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

function months(statuses = 'CCCCCCCCIYYY'): MonthOverview[] {
  const status = { C: 'closed', I: 'inProgress', Y: 'yetToCome' } as const;
  return periods.map((period, i) => {
    const income = i === 8 ? 17500.25 : 17000;
    const planned = i >= 8 ? 1900 : 1500;
    const spent = i === 7 ? 50 : i === 8 ? 1299.99 : 0;
    return {
      period,
      status: status[statuses[i] as keyof typeof status],
      income,
      planned,
      spent,
      remaining: Math.round((planned - spent) * 100) / 100,
    };
  });
}

function overview(statuses?: string): OverviewResponse {
  return {
    fiscalYears: [fy2026],
    fyId: '2026-01',
    months: months(statuses),
    categories: [],
    rows: [
      {
        categoryId: 'food',
        months: [],
        planned: 18000,
        spent: 1249.99,
        subCategories: [],
      },
    ],
    totals: { income: 204500.25, planned: 19600, spent: 1349.99, remaining: 18250.01 },
    toDate: { income: 153500.25, spent: 1349.99, saved: 152150.26 },
    rollover: null,
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

  // NFR-01: one call, the overview (/api/me is cached by setupGuard).
  async function open(data = overview()): Promise<void> {
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/');
    (await overviewCall(data.fyId)).flush(data);
    await settle();
    http.verify();
  }

  function root(): HTMLElement {
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

  function button(scope: ParentNode, label: string): HTMLButtonElement {
    return Array.from(scope.querySelectorAll('button')).find(
      (el) => el.textContent?.trim() === label,
    )!;
  }

  it('is labeled as a preview and shows the FY', async () => {
    await open();

    expect(text(root().querySelector('.preview'))).toBe(
      'Preview: the full dashboard comes in Phase 2',
    );
    expect(text(root().querySelector('.fy'))).toBe('FY 2026 · 27 Jan 2026 to 26 Jan 2027');
    expect(root().querySelector('mat-select')).toBeNull();
    expect(root().querySelector('app-rollover-banner')).toBeNull();
  });

  // DSH-01: label and dates, income, planned, spent, remaining, progress bar.
  it('shows the current month with its numbers and bar', async () => {
    await open();

    const card = root().querySelector('.current')!;
    expect(text(card.querySelector('.card-head'))).toBe('Sep 2026 27 Sep to 26 Oct In progress');
    expect(Array.from(card.querySelectorAll('.kpi')).map((kpi) => text(kpi))).toEqual([
      'Remaining SAR 600.01',
      'Income SAR 17,500.25',
      'Planned SAR 1,900',
      'Spent SAR 1,299.99',
    ]);
    expect(card.querySelector('mat-progress-bar')!.getAttribute('aria-valuenow')).toBe(
      String((129999 * 100) / 190000),
    ); // 68%
    expect(text(card.querySelector('.bar-line .chip'))).toBe('Within plan');
  });

  // DSH-02 (owner's choice, M9): the started months, Jan to Sep on 17 Oct 2026.
  it('shows the FY to date', async () => {
    await open();

    const card = root().querySelector('[aria-labelledby="to-date-title"]')!;
    expect(text(card.querySelector('.card-head'))).toBe(
      'FY 2026 to date Jan 2026 to Sep 2026 · 9 of 12 months',
    );
    expect(Array.from(card.querySelectorAll('.kpi')).map((kpi) => text(kpi))).toEqual([
      'Income SAR 153,500.25',
      'Spent SAR 1,349.99',
      'Saved SAR 152,150.26',
    ]);
    expect(card.querySelector('.danger')).toBeNull();
  });

  // UX-07: a negative saved has a text label, not only a color.
  it('says when more was spent than earned', async () => {
    const data = overview();
    data.toDate = { income: 153500.25, spent: 201349.99, saved: -47849.74 };
    await open(data);

    const saved = root().querySelector('.saved')!;
    expect(text(saved)).toBe('Saved SAR -47,849.74 Spent more than income');
    expect(saved.querySelector('.value')!.classList).toContain('danger');
  });

  // DSH-03: a column chart, one Planned and one Spent column per month, on one scale (the largest
  // value, 1,900). The month in progress is written out under the chart; a tap picks another month.
  it('shows planned vs spent per month as a column chart', async () => {
    const data = overview();
    data.months[7] = { ...data.months[7], spent: 1600, remaining: -100 };
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
      'Sep 2026 In progress Within plan Planned SAR 1,900 · Spent SAR 1,299.99',
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

  // UX-03
  it('shows an empty state when nothing is planned or spent', async () => {
    await open({ ...overview(), rows: [] });

    const empty = root().querySelector('.empty')!;
    expect(text(empty.querySelector('h2'))).toBe('Nothing planned or spent in FY 2026 yet');
    expect(Array.from(empty.querySelectorAll('a')).map((a) => a.getAttribute('href'))).toEqual([
      '/plan',
      '/add',
    ]);
    expect(root().querySelector('app-month-bars')).toBeNull();
  });

  // FY-01 after the latest FY ended: the latest FY (GET /api/fiscal-years), no month in progress,
  // the banner; Start reloads the overview of the new FY once.
  it('shows the banner when no FY contains today and starts the next FY', async () => {
    meValue = { ...me, currentFyId: null, currentPeriodKey: null, currentPeriod: null, today: '2027-02-03' };
    meSignal.set(meValue);
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
