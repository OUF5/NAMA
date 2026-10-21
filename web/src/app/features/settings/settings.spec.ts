import { BreakpointObserver } from '@angular/cdk/layout';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { MeService } from '../../core/me';
import { FiscalYear } from '../plan/plan-api';
import { Settings } from './settings';
import { InviteLinkDialog, RemovePersonDialog } from './people-dialogs';
import { People, SettingsResponse } from './settings-api';
import { settingsMatcher } from './settings-sections';

// FY 2026 with payday 27; today is 17 Oct 2026, so the current month is Sep 2026.
const sep = { key: '2026-09', label: 'Sep 2026', startDate: '2026-09-27', endDate: '2026-10-26' };
const fy2026: FiscalYear = {
  id: '2026-01',
  label: 'FY 2026',
  startDate: '2026-01-27',
  endDate: '2027-01-26',
  isTransition: false,
  periods: [sep],
};
// R-CAL-08 Example A: the start month changes to July, so the next FY is a 6-month transition FY.
const transition: FiscalYear = {
  id: '2027-01',
  label: 'FY 2027',
  startDate: '2027-01-27',
  endDate: '2027-07-26',
  isTransition: true,
  periods: ['01', '02', '03', '04', '05', '06'].map((m) => ({ key: `2027-${m}`, label: '', startDate: '', endDate: '' })),
};

// AUTH-07: what an admin gets. Dates are in the admin's time zone.
const people: People = {
  admins: ['owner@example.com'],
  members: [{ id: 'm1', email: 'friend@example.com', joinedOn: '2026-10-18' }],
  invites: [
    { id: 'i2', createdOn: '2026-10-17', expiresOn: '2026-10-24', expired: false },
    { id: 'i1', createdOn: '2026-10-01', expiresOn: '2026-10-08', expired: true },
  ],
};

function settings(currentPeriodKey: string | null = '2026-09', peopleList: People | null = null): SettingsResponse {
  return {
    profile: {
      displayName: 'Owner',
      email: 'o@example.com',
      projectName: 'Home budget',
      currency: 'SAR',
      timezone: 'Asia/Riyadh',
      fiscalStartMonth: 7,
      paydayDay: 27,
      lastBankAccountId: null,
    },
    calendar: { fiscalStartMonth: 7, paydayDay: 27, takesEffectFrom: '2027-01-27', nextFiscalYear: transition },
    income: {
      fiscalYears: [fy2026],
      fyId: '2026-01',
      // The M2 example: 10,000 from Jan, then 11,000 from Mar.
      salaries: [
        { id: 's2', kind: 'salary', label: 'Salary', amount: 11000, type: 'monthly', fromPeriodKey: '2026-03', toPeriodKey: null },
        { id: 's1', kind: 'salary', label: 'Salary', amount: 10000, type: 'monthly', fromPeriodKey: '2026-01', toPeriodKey: '2026-02' },
      ],
      additional: [
        { id: 'b', kind: 'additional', label: 'Bonus', amount: 500.25, type: 'once', fromPeriodKey: '2026-09', toPeriodKey: '2026-09' },
      ],
      totals: [{ periodKey: '2026-09', total: 11500.25 }],
      currentPeriodKey,
      currentTotal: currentPeriodKey ? 11500.25 : null,
    },
    people: peopleList,
  };
}

describe('Settings (PRD 5.7)', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let phone: boolean;
  // What the opened dialog closes with.
  let dialogResult: unknown;
  let dialogOpen: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    phone = false;
    dialogResult = undefined;
    dialogOpen = vi.fn(() => ({ afterClosed: () => of(dialogResult) }));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ matcher: settingsMatcher, component: Settings }]),
        { provide: MeService, useValue: { me: signal(null), set: vi.fn(), patchProfile: vi.fn() } },
        { provide: MatDialog, useValue: { open: dialogOpen } },
        {
          provide: BreakpointObserver,
          useValue: { observe: () => of({ matches: phone, breakpoints: {} }), isMatched: () => phone },
        },
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

  // NFR-01: GET /api/settings and GET /api/health, once for every section.
  async function open(url: string, data = settings()): Promise<HTMLElement> {
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    http.expectOne('/api/settings').flush(data);
    http.expectOne('/api/health').flush({ status: 'ok', version: '3e8c6997f2b1' });
    await settle();
    return harness.routeNativeElement!;
  }

  async function go(url: string): Promise<HTMLElement> {
    await harness.navigateByUrl(url);
    await settle();
    return harness.routeNativeElement!;
  }

  // Every text node, joined with spaces (Angular drops the whitespace between sibling elements).
  function text(el: Element | null): string {
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

  function fold(root: HTMLElement, label: string): HTMLButtonElement {
    return Array.from(root.querySelectorAll<HTMLButtonElement>('button.fold')).find((b) => text(b).endsWith(label))!;
  }

  // Owner's choice (M8): desktop picks a section in the side bar; /settings opens Profile.
  it('opens Profile on desktop and keeps the data when the section changes', async () => {
    let root = await open('/settings');

    expect(TestBed.inject(Router).url).toBe('/settings/profile');
    expect(root.querySelector('h1')?.textContent).toBe('Profile');
    expect(text(root)).toContain('Home budget');
    expect(text(root)).toContain('Asia/Riyadh');
    expect(text(root)).toContain('Signed in as o@example.com');
    // STG-06: at the bottom of every Settings page.
    expect(text(root.querySelector('.version'))).toBe('Version 3e8c699');
    expect(root.querySelector('.back')).toBeNull();
    // No filled button on the page; each dialog has its own Save.
    expect(root.querySelectorAll('.mat-mdc-unelevated-button').length).toBe(0);

    root = await go('/settings/fiscal-year');
    expect(root.querySelector('h1')?.textContent).toBe('Fiscal year');
    expect(text(root)).toContain('Starts in July');
    expect(text(root)).toContain('Takes effect from the fiscal year starting 27 Jan 2027.');
    expect(text(root)).toContain('Next: FY 2027 · 27 Jan 2027 to 26 Jul 2027 · 6 months (transition)');
    // http.verify (afterEach): no second GET /api/settings.
  });

  it('sends an unknown section back to Settings', async () => {
    await open('/settings/nope');

    expect(TestBed.inject(Router).url).toBe('/settings/profile');
  });

  // Phones: /settings is a list with a summary per section; a section page has a back link.
  it('lists the sections on phones and opens one with a back link', async () => {
    phone = true;
    let root = await open('/settings');

    expect(TestBed.inject(Router).url).toBe('/settings');
    expect(root.querySelector('h1')?.textContent).toBe('Settings');
    const rows = Array.from(root.querySelectorAll<HTMLAnchorElement>('a.menu-row'));
    expect(rows.map((row) => text(row))).toEqual([
      'person Profile Home budget · SAR chevron_right',
      'event Fiscal year Starts in July · Payday 27 chevron_right',
      'payments Income SAR 11,500.25 this month chevron_right',
    ]);
    expect(text(root.querySelector('.version'))).toBe('Version 3e8c699');
    expect(rows[2].getAttribute('href')).toBe('/settings/income');

    root = await go('/settings/income');
    expect(root.querySelector('h1')?.textContent).toBe('Income');
    expect(root.querySelector('a.back')?.getAttribute('href')).toBe('/settings');
  });

  // STG-03: the total, the newest salary (older ones fold away) and the additional items.
  it('shows the income with the salary history folded away', async () => {
    const root = await open('/settings/income');

    expect(text(root)).toContain('Total income in Sep 2026 · 27 Sep to 26 Oct SAR 11,500.25');
    expect(text(root)).toContain('From Mar 2026 SAR 11,000');
    expect(text(root)).not.toContain('Jan 2026 to Feb 2026');
    expect(text(root)).toContain('Bonus Sep 2026 SAR 500.25');

    fold(root, 'History (1)').click();
    harness.detectChanges();

    expect(text(root)).toContain('Jan 2026 to Feb 2026 SAR 10,000');
  });

  it('says so when no fiscal year contains today', async () => {
    const root = await open('/settings/income', settings(null));

    expect(text(root)).toContain('No fiscal year contains today.');
  });

  // STG-07: People is for admins only. /api/settings sends people: null to everyone else.
  it('hides People from someone who is not an admin', async () => {
    await open('/settings/people');

    expect(TestBed.inject(Router).url).toBe('/settings/profile');
  });

  it('lists People on phones for an admin', async () => {
    phone = true;
    const root = await open('/settings', settings('2026-09', people));

    const rows = Array.from(root.querySelectorAll<HTMLAnchorElement>('a.menu-row'));
    expect(text(rows[3])).toBe('group People 1 person · 1 unused link chevron_right');
    expect(rows[3].getAttribute('href')).toBe('/settings/people');
  });

  // AUTH-07: the admins, who joined and when, and the unused links with their expiry.
  it('shows the admins, the people who joined and the links', async () => {
    const root = await open('/settings/people', settings('2026-09', people));

    expect(root.querySelector('h1')?.textContent).toBe('People');
    const items = Array.from(root.querySelectorAll('.item')).map((item) => text(item));
    expect(items).toEqual([
      'owner@example.com Admin',
      'friend@example.com Joined 18 Oct 2026 person_remove',
      'Created 17 Oct 2026 Expires on 24 Oct 2026 link_off',
      'Created 1 Oct 2026 Expired on 8 Oct 2026 link_off',
    ]);
    // Admins come from ALLOWED_EMAILS and cannot be removed here.
    expect(root.querySelector('[aria-label="Remove owner@example.com"]')).toBeNull();
  });

  // AUTH-06: the link is built from the token and shown once in a dialog.
  it('creates a link and shows it once', async () => {
    const root = await open('/settings/people', settings('2026-09', people));
    const create = Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) => text(b) === 'Create invite link')!;

    create.click();
    const request = http.expectOne('/api/invites');
    expect(request.request.method).toBe('POST');
    const newer = { ...people, invites: [{ id: 'i3', createdOn: '2026-10-18', expiresOn: '2026-10-25', expired: false }, ...people.invites] };
    request.flush({ token: 'tok_123', expiresOn: '2026-10-25', people: newer });
    await settle();

    expect(dialogOpen).toHaveBeenCalledTimes(1);
    expect(dialogOpen.mock.calls[0][0]).toBe(InviteLinkDialog);
    expect(dialogOpen.mock.calls[0][1].data).toEqual({ link: `${location.origin}/invite/tok_123`, expiresOn: '2026-10-25' });
    expect(text(root.querySelector('.status'))).toBe('Invite link created.');
    expect(text(root)).toContain('Created 18 Oct 2026 Expires on 25 Oct 2026');
  });

  // An unused link loses nothing when revoked, so there is no confirmation.
  it('revokes a link straight away', async () => {
    const root = await open('/settings/people', settings('2026-09', people));

    root.querySelector<HTMLButtonElement>('[aria-label="Revoke the link created on 1 Oct 2026"]')!.click();
    const request = http.expectOne('/api/invites/i1');
    expect(request.request.method).toBe('DELETE');
    request.flush({ people: { ...people, invites: [people.invites[0]] } });
    await settle();

    expect(dialogOpen).not.toHaveBeenCalled();
    expect(text(root.querySelector('.status'))).toBe('Link revoked.');
    expect(text(root)).not.toContain('Created 1 Oct 2026');
  });

  // UX-04: removing a person asks first; Cancel sends nothing.
  it('asks before removing a person', async () => {
    const root = await open('/settings/people', settings('2026-09', people));
    const remove = root.querySelector<HTMLButtonElement>('[aria-label="Remove friend@example.com"]')!;

    remove.click();
    await settle();
    expect(dialogOpen.mock.calls[0][0]).toBe(RemovePersonDialog);
    expect(dialogOpen.mock.calls[0][1].data).toBe('friend@example.com');
    // http.verify (afterEach) proves Cancel sent nothing; now confirm.

    dialogResult = true;
    remove.click();
    await settle();
    const request = http.expectOne('/api/people/m1');
    expect(request.request.method).toBe('DELETE');
    request.flush({ people: { ...people, members: [] } });
    await settle();

    expect(text(root.querySelector('.status'))).toBe('Removed friend@example.com.');
    expect(text(root)).toContain('Nobody has joined yet.');
  });
});
