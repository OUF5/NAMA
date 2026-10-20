import { ApplicationRef, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { App } from './app';
import { routes } from './app.routes';
import { AuthService } from './core/auth';
import { Me, MeService } from './core/me';
import { settingsMatcher } from './features/settings/settings-sections';

describe('App', () => {
  const me = signal<Me | null>(null);
  const signOut = vi.fn();

  beforeEach(async () => {
    me.set({ setupCompleted: true, isAdmin: false, profile: null, currentFyId: null, currentPeriodKey: null, currentPeriod: null, today: null, firstDate: null, lastDate: null });
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([{ matcher: settingsMatcher, children: [] }]),
        { provide: MeService, useValue: { me } },
        { provide: AuthService, useValue: { signOut } },
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

  it('should show the five navigation items (UX-01)', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    const labels = Array.from(compiled.querySelectorAll('.nav > .nav-item span')).map((el) => el.textContent?.trim());
    expect(labels).toEqual(['Dashboard', 'Entries', 'Add expense', 'Plan', 'Tracker']);
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
    expect(subs.map((a) => a.textContent?.trim())).toEqual(['Profile', 'Fiscal year', 'Income', 'Bank accounts', 'Categories']);
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

  // PRD 5.4 (owner's choice, 20 Oct 2026): Add expense is one tap away in the nav; the top bar has no copy.
  it('should show Add expense in the nav and not in the top bar', async () => {
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
