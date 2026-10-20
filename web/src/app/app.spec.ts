import { ApplicationRef, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { App } from './app';
import { routes } from './app.routes';
import { AuthService } from './core/auth';
import { Me, MeService } from './core/me';
import { Summary, SummaryService } from './core/summary';
import { settingsMatcher } from './features/settings/settings-sections';

// A top bar number: its caption and its value.
function statText(stat: Element): string {
  return Array.from(stat.children).map((child) => child.textContent?.trim()).join(' ');
}

describe('App', () => {
  const me = signal<Me | null>(null);
  const signOut = vi.fn();
  // UX-08: the top bar's numbers; load and clear are spied, the summary is set by each test.
  const summary = signal<Summary | null>(null);
  const summaryService = { summary, load: vi.fn(async () => undefined), clear: vi.fn() };

  beforeEach(async () => {
    me.set({ setupCompleted: true, isAdmin: false, profile: null, currentFyId: null, currentPeriodKey: null, currentPeriod: null, today: null, firstDate: null, lastDate: null });
    summary.set(null);
    summaryService.load.mockClear();
    summaryService.clear.mockClear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([{ matcher: settingsMatcher, children: [] }]),
        { provide: MeService, useValue: { me } },
        { provide: AuthService, useValue: { signOut } },
        { provide: SummaryService, useValue: summaryService },
      ],
    }).compileComponents();
  });

  it('should show NAMA in the top bar', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.brand')?.textContent).toContain('NAMA');
  });

  it('should show the project name and the current month with its dates (UX-02)', async () => {
    me.set({
      setupCompleted: true,
      isAdmin: false,
      profile: {
        displayName: 'Owner',
        email: 'owner@example.com',
        projectName: 'Home budget',
        currency: 'SAR',
        timezone: 'Asia/Riyadh',
        fiscalStartMonth: 1,
        paydayDay: 27,
        lastBankAccountId: null,
      },
      currentFyId: '2026-01',
      currentPeriodKey: '2026-09',
      currentPeriod: { key: '2026-09', label: 'Sep 2026', startDate: '2026-09-27', endDate: '2026-10-26' },
      today: '2026-10-17',
      firstDate: '2026-01-27',
      lastDate: '2027-01-26',
    });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.brand')?.textContent).toBe('NAMA · Home budget');
    expect(compiled.querySelector('.month')?.textContent).toBe('Sep 2026 · 27 Sep to 26 Oct');
  });

  // UX-01: five items on phones; Banks (BNK-01) joins them in the side bar.
  it('should show the five navigation items and Banks on desktop (UX-01, BNK-01)', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    const items = Array.from(compiled.querySelectorAll('.nav > .nav-item'));
    expect(items.map((el) => el.querySelector('span')?.textContent?.trim())).toEqual(['Dashboard', 'Entries', 'Add', 'Plan', 'Tracker', 'Banks']);
    expect(items.filter((el) => el.classList.contains('desktop-only')).map((el) => el.getAttribute('href'))).toEqual(['/banks']);
  });

  // UX-08: Left this month (in its plan status color) and the balance, each a link, on every page.
  it('should show this month and the balance in the top bar', async () => {
    summary.set({
      balance: 28000,
      complete: true,
      accounts: [
        { id: 'main', name: 'Main', balance: 4100 },
        { id: 'savings', name: 'Savings', balance: 23900 },
      ],
      month: {
        period: { key: '2026-09', label: 'Sep 2026', startDate: '2026-09-27', endDate: '2026-10-26' },
        daysLeft: 10,
        income: 17000,
        carriedIn: 11950,
        planned: 1500,
        spent: 1199.99,
        remaining: 12250.01,
        unassigned: 139500,
      },
    });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(summaryService.load).toHaveBeenCalledTimes(1);

    const stats = Array.from(compiled.querySelectorAll<HTMLAnchorElement>('.stats.bar .stat'));
    expect(stats.map(statText)).toEqual([
      'Left in Sep 2026 · 10 days left SAR 12,250.01',
      'Balance SAR 28,000',
    ]);
    expect(stats.map((a) => a.getAttribute('href'))).toEqual(['/tracker', '/banks']);
    expect(stats[0].querySelector('.value')!.classList).toContain('ok');
    expect(stats[1].getAttribute('title')).toBe('Main SAR 4,100 · Savings SAR 23,900');
    expect(stats[1].getAttribute('aria-label')).toBe('Balance SAR 28,000: Main SAR 4,100 · Savings SAR 23,900. Open Banks');
    // Phones get the same numbers in the strip under the top bar.
    expect(compiled.querySelectorAll('.stats.strip .stat').length).toBe(2);
  });

  // BNK-05: until every account has its opening balance, the top bar asks for it.
  it('should ask to set up the balance until it is complete', async () => {
    summary.set({ balance: null, complete: false, accounts: [{ id: 'main', name: 'Main', balance: null }], month: null });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    const stats = Array.from(compiled.querySelectorAll<HTMLAnchorElement>('.stats.bar .stat'));
    expect(stats.map(statText)).toEqual(['Balance Set up']);
    expect(stats[0].getAttribute('aria-label')).toBe('Balance: set up your bank balances. Open Banks');
  });

  // Owner's choice (M8): Settings in the side bar, with its sections while you are in Settings.
  it('should open the Settings sections in the side bar while in Settings', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    const settings = compiled.querySelector('.settings-group a')!;
    expect(settings.getAttribute('href')).toBe('/settings');
    expect(settings.getAttribute('aria-expanded')).toBe('false');
    expect(compiled.querySelector('.sub-nav')).toBeNull();

    await TestBed.inject(Router).navigateByUrl('/settings/income');
    await fixture.whenStable();

    expect(settings.getAttribute('aria-expanded')).toBe('true');
    const subs = Array.from(compiled.querySelectorAll<HTMLAnchorElement>('.sub-nav a'));
    expect(subs.map((a) => a.textContent?.trim())).toEqual(['Profile', 'Fiscal year', 'Income', 'Categories']);
    expect(subs[2].classList).toContain('active');
    expect(subs[2].getAttribute('href')).toBe('/settings/income');

  });

  // AUTH-04, owner's choice (M8): outside Settings, after a confirmation.
  describe('sign out', () => {
    afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((el) => (el.innerHTML = '')));

    async function confirmWith(choice: string): Promise<void> {
      signOut.mockClear();
      const fixture = TestBed.createComponent(App);
      await fixture.whenStable();
      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.top-sign-out')?.getAttribute('aria-label')).toBe('Sign out');

      compiled.querySelector<HTMLButtonElement>('.nav-sign-out')!.click();
      const dialog = await vi.waitFor(() => {
        const el = document.querySelector('app-sign-out-dialog');
        expect(el).not.toBeNull();
        return el!;
      });
      expect(dialog.querySelector('h2')?.textContent).toBe('Sign out?');
      expect(signOut).not.toHaveBeenCalled();
      // The dialog's bindings ([mat-dialog-close]="true") are set by its first change detection.
      TestBed.inject(ApplicationRef).tick();

      Array.from(dialog.querySelectorAll('button')).find((b) => b.textContent?.trim() === choice)!.click();
      await fixture.whenStable();
      await new Promise((resolve) => setTimeout(resolve));
    }

    it('signs out after the confirmation', async () => {
      await confirmWith('Sign out');
      // afterClosed fires once the close animation ends.
      await vi.waitFor(() => expect(signOut).toHaveBeenCalledTimes(1));
    });

    it('stays signed in on Cancel', async () => {
      await confirmWith('Cancel');
      await vi.waitFor(() => expect(document.querySelector('app-sign-out-dialog')).toBeNull());
      expect(signOut).not.toHaveBeenCalled();
    });
  });

  // PRD 5.4 (owner's choice, 20 Oct 2026): Add is one tap away in the nav; the top bar has no copy.
  it('should show Add in the nav and not in the top bar', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.nav-add')?.getAttribute('href')).toBe('/add');
    expect(compiled.querySelector('.top-bar a[href="/add"]')).toBeNull();
  });

  it('should hide navigation and settings when signed out or not allowed', async () => {
    me.set(null);
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.brand')?.textContent).toContain('NAMA');
    expect(compiled.querySelector('.nav')).toBeNull();
    expect(compiled.querySelector('[aria-label="Settings"]')).toBeNull();
    // UX-08: no numbers, and none are loaded.
    expect(compiled.querySelector('.stats')).toBeNull();
    expect(summaryService.load).not.toHaveBeenCalled();
    expect(summaryService.clear).toHaveBeenCalled();
  });

  it('should hide navigation and settings until setup is complete', async () => {
    me.set({ setupCompleted: false, isAdmin: false, profile: null, currentFyId: null, currentPeriodKey: null, currentPeriod: null, today: null, firstDate: null, lastDate: null });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.nav')).toBeNull();
    expect(compiled.querySelector('[aria-label="Settings"]')).toBeNull();
    expect(compiled.querySelector('.nav-add')).toBeNull();
  });
});

// Months is Tracker now (owner's choice, 20 Oct 2026): old bookmarks and Home Screen links still work.
describe('the old Months address', () => {
  @Component({ template: '' })
  class Tracker {}

  it('goes to Tracker with its query', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([routes.find((route) => route.path === 'months')!, { path: 'tracker', component: Tracker }])],
    });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/months?fyId=2026-01&view=year');

    expect(TestBed.inject(Router).url).toBe('/tracker?fyId=2026-01&view=year');
  });
});
