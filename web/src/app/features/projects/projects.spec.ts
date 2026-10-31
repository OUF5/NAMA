import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldHarness } from '@angular/material/form-field/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Me, MeService } from '../../core/me';
import { ProjectDialog } from './project-dialog';
import { deadlineLine, milestoneBar, projectBar, verdict } from './project-lines';
import { ProjectView } from './project-view';
import { Projects } from './projects';
import { Project, ProjectMonths, ProjectsResponse } from './projects-api';

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
    paydayDay: 25,
    monthNaming: 'end',
    lastBankAccountId: 'rajhi',
  },
  currentFyId: '2026-01',
  currentPeriodKey: '2026-10',
  currentPeriod: { key: '2026-10', label: 'Nov 2026', startDate: '2026-10-25', endDate: '2026-11-24' },
  today: '2026-10-31',
  firstDate: '2026-01-25',
  lastDate: '2027-01-24',
  monthKeyOffset: 1,
};

const oct = { key: '2026-10', label: 'Nov 2026', startDate: '2026-10-25', endDate: '2026-11-24' };
const sep = { key: '2026-09', label: 'Oct 2026', startDate: '2026-09-25', endDate: '2026-10-24' };
const nov = { key: '2026-11', label: 'Dec 2026', startDate: '2026-11-25', endDate: '2026-12-24' };
const aug27 = { key: '2027-07', label: 'Aug 2027', startDate: '2027-07-25', endDate: '2027-08-24' };
const dec = { key: '2026-12', label: 'Jan 2027', startDate: '2026-12-25', endDate: '2027-01-24' };

// PRD R-PRJ-07's worked example, as GET /api/projects answers it (named by the month they end in).
const de: Project = {
  id: 'de',
  name: 'Road to DE',
  deadline: '2027-08-30',
  status: 'active',
  doneOn: null,
  startPeriodKey: '2026-09',
  targets: [
    { level: 'best', amount: 126000, active: false, covered: true },
    { level: 'likely', amount: 144000, active: true, covered: false },
    { level: 'worst', amount: 184000, active: false, covered: false },
  ],
  activeTarget: 'likely',
  bankAccountId: 'alinma',
  monthlyAmount: 7000,
  // The plan's first, current, next and last months (the ones between are left out here).
  plan: [
    { period: sep, amount: 7000 },
    { period: oct, amount: 7000 },
    { period: nov, amount: 7000 },
    { period: aug27, amount: 7000 },
  ],
  state: 'offTrack',
  asOf: '2026-10-31',
  currentMonth: oct,
  money: 65375,
  progress: 65375,
  target: 144000,
  stillToSave: 78625,
  percent: 45,
  months: [
    { period: sep, planned: 7000, actual: 7000, behind: 0 },
    { period: oct, planned: 7000, actual: 1200, behind: 5800 },
  ],
  behindBefore: 0,
  stillToPutIn: 5800,
  monthsToCome: [
    { period: nov, amount: 7000 },
    { period: aug27, amount: 7000 },
  ],
  deadlinePassed: false,
  projected: 141175,
  short: 2825,
  extraAMonth: 257,
  monthsLeft: 11,
  milestones: [
    { id: 'm1', label: 'Deposit', date: '2027-05-01', amount: 50116, passed: false, money: 106175, difference: 56059 },
    { id: 'm0', label: 'Visa', date: '2026-10-01', amount: 60000, passed: true, money: 57175, difference: -2825 },
  ],
};

const car: Project = {
  ...de,
  id: 'car',
  name: 'Car',
  deadline: '2027-06-30',
  targets: [{ level: 'likely', amount: 20000, active: true, covered: true }],
  bankAccountId: 'snb',
  monthlyAmount: 2000,
  state: 'onTrack',
  progress: 16000,
  target: 20000,
  percent: 80,
  projected: 22000,
  short: 0,
  extraAMonth: 0,
  monthsLeft: 9,
  milestones: [],
};

const old: Project = { ...car, id: 'old', name: 'Wedding', status: 'done', state: 'done', doneOn: '2026-10-20', asOf: '2026-10-20', bankAccountId: 'rajhi' };

function response(projects: Project[] = [de, car, old]): ProjectsResponse {
  return {
    today: '2026-10-31',
    currentMonth: oct,
    projects,
    accounts: [
      { id: 'alinma', name: 'Alinma Saving', archived: false, balance: 65375, projectId: 'de' },
      { id: 'rajhi', name: 'Al Rajhi', archived: false, balance: 4100, projectId: null },
      { id: 'snb', name: 'SNB', archived: false, balance: 16000, projectId: 'car' },
    ],
  };
}

describe('Projects (PRD 5.14)', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'projects', component: Projects },
          { path: 'projects/:id', component: ProjectView },
        ]),
        { provide: MeService, useValue: { me: signal(me), load: async () => me } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function settle(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  async function open(url: string, data = response()): Promise<void> {
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    (await vi.waitFor(() => http.expectOne('/api/projects'))).flush(data);
    await settle();
  }

  function root(): HTMLElement {
    return harness.routeNativeElement!;
  }

  // Every text node, joined with spaces (Angular drops the whitespace between sibling elements).
  function words(el: Element | null): string {
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

  function text(selector: string, from: ParentNode = root()): string {
    return words(from.querySelector(selector));
  }

  function texts(selector: string): string[] {
    return Array.from(root().querySelectorAll(selector)).map(words);
  }

  // PRJ-02: status, progress and gap on one screen; Done ones fold away.
  it('lists the projects with their status, progress and gap', async () => {
    await open('/projects');

    expect(texts('.cards a.project .name')).toEqual(['Road to DE', 'Car']);
    const first = root().querySelector('a.project')!;
    expect(first.getAttribute('href')).toBe('/projects/de');
    expect(text('.chip', first)).toBe('Off track');
    expect(first.querySelector('.chip.danger')).not.toBeNull();
    expect(text('.money.line', first)).toBe('SAR 65,375 of 144,000 · 45% · Likely');
    expect(text('.line.danger', first)).toBe('Short SAR 2,825: put in SAR 257 more a month.');
    expect(text('.caption', first)).toBe('By 30 Aug 2027 · 11 months left');
    expect(text('.line.ok', root().querySelectorAll('a.project')[1])).toBe('On track, with SAR 2,000 to spare.');

    // Only the project cards (owner, 31 Oct 2026): no plan check, no Accounts.
    expect(root().querySelectorAll('section.card').length).toBe(0);

    expect(text('.fold')).toContain('Done (1)');
    expect(texts('a.project .name')).not.toContain('Wedding');
    (root().querySelector('.fold') as HTMLButtonElement).click();
    await settle();
    expect(texts('a.project .name')).toContain('Wedding');
  });

  it('says what a project is when there is none', async () => {
    await open('/projects', response([]));

    expect(text('.empty p')).toBe('No projects yet. A project is a goal with a deadline, such as a car down payment, saved in one of your bank accounts.');
  });

  // PRJ-03: where you are, where you will be, and what to change.
  it('shows one project', async () => {
    await open('/projects/de');

    expect(text('a.back')).toBe('arrow_back All projects');
    expect(root().querySelector('a.back')!.getAttribute('href')).toBe('/projects');
    expect(text('h1')).toBe('Road to DE');
    expect(text('.title .chip')).toBe('Off track');
    expect(text('.status')).toBe('By 30 Aug 2027 · 11 months left');
    expect(texts('.story > div')).toEqual([
      'Now SAR 65,375 In Alinma Saving',
      'By 30 Aug 2027 SAR 141,175 With SAR 19,800 more as planned',
      'Still to save SAR 78,625 SAR 65,375 of 144,000 · 45% · Likely',
    ]);
    expect(text('.verdict')).toBe('Short SAR 2,825: put in SAR 257 more a month.');

    // How?
    (root().querySelector('.fold') as HTMLButtonElement).click();
    await settle();
    expect(texts('.breakdown div')).toEqual([
      'Progress now SAR 65,375',
      '+ Still to put in this month SAR 5,800',
      '+ 2 months to come (Dec 2026 to Aug 2027) SAR 14,000',
      '= Projected by 30 Aug 2027 SAR 141,175',
    ]);

    expect(texts('#targets-title + .rows li')).toEqual([
      'Best SAR 126,000 Covered',
      'Likely · active SAR 144,000 Short SAR 2,825',
      'Worst SAR 184,000 Short SAR 42,825',
    ]);
    expect(texts('#month-title + .rows li')).toEqual([
      'Planned SAR 7,000',
      'Put in so far SAR 1,200',
      'Still to put in SAR 5,800',
      'From the months before On plan',
    ]);
    // R-PRJ-08: a bar of the money against each milestone, and what it means.
    expect(texts('.milestones li')).toEqual([
      'Deposit By 1 May 2027 · SAR 50,116 On track SAR 65,375 now · SAR 106,175 projected that day',
      'Visa By 1 Oct 2026 · SAR 60,000 Missed by SAR 2,825 SAR 57,175 in its account that day',
    ]);
    const bars = Array.from(root().querySelectorAll('.milestones app-project-bar .bar'));
    expect(bars.map((bar) => bar.getAttribute('aria-label'))).toEqual(['SAR 65,375 of SAR 50,116', 'SAR 57,175 of SAR 60,000']);
    expect(root().querySelector('.milestones li:last-child .fill.danger')).not.toBeNull();

    // R-PRJ-05, R-PRJ-06: every month of the plan, oldest first; months to come show only the plan.
    expect(texts('table tbody tr')).toEqual([
      'Oct 2026 SAR 7,000 SAR 7,000 On plan',
      'Nov 2026 SAR 7,000 SAR 1,200 SAR 5,800 behind',
      'Dec 2026 SAR 7,000 – –',
      'Aug 2027 SAR 7,000 – –',
    ]);
    expect(texts('table tbody tr.current td:first-child')).toEqual(['Nov 2026']);
    expect(root().querySelectorAll('table tbody tr.to-come').length).toBe(2);
    expect(root().querySelector('a[href^="/entries"]')).toBeNull();
  });

  it('says when a project no longer exists', async () => {
    await open('/projects/gone');

    expect(text('.empty p')).toBe('This project no longer exists.');
  });
});

describe('Project lines (R-PRJ-07 to R-PRJ-10)', () => {
  it('says what the numbers mean', () => {
    expect(verdict(de, 'SAR')).toEqual({ text: 'Short SAR 2,825: put in SAR 257 more a month.', tone: 'danger' });
    expect(verdict({ ...de, monthsLeft: 1, short: 2825 }, 'SAR').text).toBe('Short SAR 2,825: put it in this month.');
    expect(verdict({ ...car, milestones: [{ ...de.milestones[0], difference: -100 }] }, 'SAR')).toEqual({
      text: 'Deposit on 1 May 2027 is short SAR 100.',
      tone: 'danger',
    });
    expect(verdict({ ...de, deadlinePassed: true, projected: null }, 'SAR').text).toBe('The deadline has passed, SAR 78,625 short.');
    expect(verdict({ ...de, state: 'reached' }, 'SAR').text).toBe('Reached: the target is saved.');
    expect(verdict({ ...de, state: 'unknown' }, 'SAR').text).toBe('Set the balance of its account on Banks to see where it stands.');
    expect(verdict(old, 'SAR').text).toBe('Done on 20 Oct 2026');
  });

  it('counts the months left with this one', () => {
    expect(deadlineLine(de)).toBe('By 30 Aug 2027 · 11 months left');
    expect(deadlineLine({ ...de, monthsLeft: 1 })).toBe('By 30 Aug 2027 · this month');
    expect(deadlineLine({ ...de, deadlinePassed: true })).toBe('By 30 Aug 2027 · passed');
  });

  it('fills the bar up to the target', () => {
    expect(projectBar(car, 'SAR')).toEqual({ fill: 80, tone: 'ok', label: 'SAR 16,000 of SAR 20,000' });
    expect(projectBar({ ...de, progress: 150000 }, 'SAR').fill).toBe(100);
    expect(projectBar({ ...de, progress: null, state: 'unknown' }, 'SAR')).toEqual({ fill: 0, tone: 'neutral', label: 'Progress unknown' });
  });

  // R-PRJ-08: today's money before the day, the day's money after it.
  it('fills a milestone with the money against its amount', () => {
    expect(milestoneBar(de, de.milestones[0], 'SAR')).toEqual({ fill: 100, tone: 'ok', label: 'SAR 65,375 of SAR 50,116' });
    const visa = milestoneBar(de, de.milestones[1], 'SAR');
    expect(visa.fill).toBeCloseTo((57175 / 60000) * 100);
    expect(visa.tone).toBe('danger');
  });
});

describe('ProjectDialog (PRJ-04)', () => {
  let http: HttpTestingController;
  let closed: unknown;

  function create(project: Project | null) {
    closed = undefined;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: (value: unknown) => (closed = value) } },
        { provide: MAT_DIALOG_DATA, useValue: { project, data: response(), currency: 'SAR' } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ProjectDialog);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => http.verify());

  async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  // GET /api/projects/months, once typing stops (MONTHS_DELAY_MS).
  async function months(answer: ProjectMonths, params: Record<string, string>): Promise<void> {
    const request = await vi.waitFor(() => http.expectOne((req) => req.url === '/api/projects/months'));
    expect(Object.fromEntries(request.request.params.keys().map((key) => [key, request.request.params.get(key)]))).toEqual(params);
    request.flush(answer);
  }

  function words(el: Element | null): string {
    return (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function button(el: HTMLElement, label: string): HTMLButtonElement {
    return Array.from(el.querySelectorAll('button')).find((b) => words(b) === label || words(b).endsWith(' ' + label)) as HTMLButtonElement;
  }

  type Page = {
    form: { patchValue(value: object): void; controls: { monthly: { value: string } } };
    addMilestone(): void;
    save(): Promise<void>;
    milestones: { at(i: number): { patchValue(value: object): void } };
  };

  // Owner's flow (31 Oct 2026): name, deadline, account and targets; the recommended amount fills the
  // monthly amount; Plan by month changes one month.
  it('recommends a monthly amount and plans each month', async () => {
    const fixture = create(null);
    const page = fixture.componentInstance as unknown as Page;
    const el = fixture.nativeElement as HTMLElement;

    // One account from a dropdown; one another project counts cannot be picked (R-PRJ-03).
    const field = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatFormFieldHarness.with({ floatingLabelText: 'Bank account' }));
    const select = (await field.getControl(MatSelectHarness))!;
    await select.open();
    const options = await select.getOptions();
    expect(await Promise.all(options.map(async (item) => [(await item.getText()).replace(/\s+/g, ' ').trim(), await item.isDisabled()]))).toEqual([
      ['Alinma Saving SAR 65,375 · in Road to DE', true],
      ['Al Rajhi SAR 4,100', false],
      ['SNB SAR 16,000 · in Car', true],
    ]);
    await select.close();
    expect(words(el.querySelector('.recommend'))).toContain('Pick the deadline, the bank account and the Likely target');

    // Nothing is sent without a target and an account.
    page.form.patchValue({ name: 'Car 2', deadline: new Date(2027, 0, 10), likely: '' });
    await page.save();
    http.expectNone('/api/projects');
    await months({ months: [oct, nov, dec], currentKey: '2026-10', recommended: null }, { deadline: '2027-01-10' });
    await settle(fixture);
    expect(el.textContent).toContain('Set at least one target.');
    expect(el.textContent).toContain('Choose a bank account.');
    expect(words(el.querySelector('.recommend'))).toContain('Set the Likely target to see a recommended amount.');

    // R-PRJ-12: the recommendation fills the monthly amount.
    page.form.patchValue({ best: '18000', likely: '20000.5', bankAccountId: 'rajhi' });
    await months(
      { months: [oct, nov, dec], currentKey: '2026-10', recommended: 5300 },
      { deadline: '2027-01-10', bankAccountId: 'rajhi', likely: '20000.5' },
    );
    await settle(fixture);
    expect(words(el.querySelector('.recommend span'))).toBe('Recommended: SAR 5,300 a month for 3 months (Nov 2026 to Jan 2027) to reach Likely.');
    expect(page.form.controls.monthly.value).toBe('5300');
    expect(words(el.querySelector('.monthly mat-hint'))).toBe('3 months · SAR 15,900 in all');

    // Plan by month: Dec 2026 differs.
    button(el, 'Plan by month').click();
    await settle(fixture);
    expect(words(el.querySelector('h2'))).toBe('Plan by month Car 2 · SAR 15,900 in all');
    const inputs = Array.from(el.querySelectorAll<HTMLInputElement>('.months input'));
    expect(inputs.map((input) => input.value)).toEqual(['5300', '5300', '5300']);
    inputs[1].value = '6000';
    inputs[1].dispatchEvent(new Event('input'));
    await settle(fixture);
    expect(Array.from(el.querySelectorAll('.month .after')).map(words)).toEqual([
      'In all by then SAR 5,300',
      'In all by then SAR 11,300',
      'In all by then SAR 16,600',
    ]);
    button(el, 'Back').click();
    await settle(fixture);
    expect(words(el.querySelector('.monthly mat-label'))).toBe('Monthly amount (varies by month)');
    expect(words(el.querySelector('.recommend button'))).toBe('Use SAR 5,300');

    page.addMilestone();
    page.milestones.at(0).patchValue({ label: ' Deposit ', date: new Date(2027, 0, 1), amount: '5000' });
    void page.save();

    const post = await vi.waitFor(() => http.expectOne('/api/projects'));
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({
      name: 'Car 2',
      deadline: '2027-01-10',
      best: 18000,
      likely: 20000.5,
      worst: null,
      activeTarget: 'likely',
      bankAccountId: 'rajhi',
      monthly: [
        { periodKey: '2026-10', amount: 5300 },
        { periodKey: '2026-11', amount: 6000 },
        { periodKey: '2026-12', amount: 5300 },
      ],
      milestones: [{ id: null, label: 'Deposit', date: '2027-01-01', amount: 5000 }],
    });
    post.flush(response());
    await settle(fixture);
    expect(closed).toEqual(response());
  });

  // An edit keeps its months; "Use" puts the recommendation in every month from this month on.
  it('keeps past months and uses the recommendation from this month on', async () => {
    const fixture = create(de);
    const page = fixture.componentInstance as unknown as Page;
    const el = fixture.nativeElement as HTMLElement;
    await months(
      { months: [sep, oct, nov, aug27], currentKey: '2026-10', recommended: 7257 },
      { deadline: '2027-08-30', bankAccountId: 'alinma', likely: '144000', projectId: 'de' },
    );
    await settle(fixture);

    expect(page.form.controls.monthly.value).toBe('7000');
    expect(words(el.querySelector('.recommend span'))).toBe('Recommended: SAR 7,257 a month for 3 months (Nov 2026 to Aug 2027) to reach Likely.');
    button(el, 'Use SAR 7,257').click();
    void page.save();

    const put = await vi.waitFor(() => http.expectOne('/api/projects/de'));
    expect(put.request.method).toBe('PUT');
    expect(put.request.body.monthly).toEqual([
      { periodKey: '2026-09', amount: 7000 },
      { periodKey: '2026-10', amount: 7257 },
      { periodKey: '2026-11', amount: 7257 },
      { periodKey: '2027-07', amount: 7257 },
    ]);
    expect(put.request.body.milestones.map((m: { id: string }) => m.id)).toEqual(['m1', 'm0']);
    put.flush(response());
    await settle(fixture);
  });

  // R-PRJ-01: Pause right away; Mark done asks the day inside the dialog; 409 says which account.
  it('pauses, marks done and shows a taken account', async () => {
    const fixture = create(de);
    const el = fixture.nativeElement as HTMLElement;
    await months({ months: [sep, oct, nov, aug27], currentKey: '2026-10', recommended: 7257 },
      { deadline: '2027-08-30', bankAccountId: 'alinma', likely: '144000', projectId: 'de' });
    await settle(fixture);

    button(el, 'Pause').click();
    const pause = await vi.waitFor(() => http.expectOne('/api/projects/de/status'));
    expect(pause.request.body).toEqual({ status: 'paused' });
    pause.flush(response());
    await settle(fixture);
    expect(closed).toEqual(response());

    button(el, 'Mark done').click();
    fixture.detectChanges();
    expect(el.querySelector('h2')?.textContent).toBe('Mark Road to DE done?');
    button(el, 'Mark done').click();
    const done = await vi.waitFor(() => http.expectOne('/api/projects/de/status'));
    expect(done.request.body).toEqual({ status: 'done', doneOn: '2026-10-31' });
    done.flush({ detail: 'Alinma Saving is already counted by Car.' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    expect(el.querySelector('.error')?.textContent).toBe('Alinma Saving is already counted by Car.');
  });

  // R-PRJ-01: Resume asks nothing: the months are as they were.
  it('resumes a paused project right away', async () => {
    const fixture = create({ ...de, status: 'paused', state: 'paused' });
    const el = fixture.nativeElement as HTMLElement;
    await months({ months: [sep, oct, nov, aug27], currentKey: '2026-10', recommended: 7257 },
      { deadline: '2027-08-30', bankAccountId: 'alinma', likely: '144000', projectId: 'de' });
    await settle(fixture);

    button(el, 'Resume').click();
    const resume = await vi.waitFor(() => http.expectOne('/api/projects/de/status'));
    expect(resume.request.body).toEqual({ status: 'active' });
    resume.flush(response());
    await settle(fixture);
    expect(closed).toEqual(response());
  });

  it('deletes after a confirmation inside the dialog', async () => {
    const fixture = create(de);
    const el = fixture.nativeElement as HTMLElement;
    await months({ months: [sep, oct, nov, aug27], currentKey: '2026-10', recommended: 7257 },
      { deadline: '2027-08-30', bankAccountId: 'alinma', likely: '144000', projectId: 'de' });
    await settle(fixture);
    (el.querySelector('button.delete') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(el.querySelector('h2')?.textContent).toBe('Delete this project?');
    expect(el.textContent).toContain('Your balances and entries do not change.');
    button(el, 'Delete').click();
    const remove = await vi.waitFor(() => http.expectOne('/api/projects/de'));
    expect(remove.request.method).toBe('DELETE');
    remove.flush(response([car]));
    await settle(fixture);
    expect(closed).toEqual(response([car]));
  });
});
