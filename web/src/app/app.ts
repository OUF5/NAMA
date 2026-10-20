import { DOCUMENT, NgTemplateOutlet } from '@angular/common';
import { Component, Injector, computed, effect, inject, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, firstValueFrom, map } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { AuthService } from './core/auth';
import { MeService } from './core/me';
import { SummaryService } from './core/summary';
import { visibleSections } from './features/settings/settings-sections';
import { dialogSize } from './shared/dialog';
import { formatMoney, toMinor } from './shared/money';
import { periodWithDates } from './shared/month-label';
import { spendStatus } from './shared/plan-status';

interface NavItem {
  path: string;
  label: string;
  icon: string;
  prominent?: boolean;
  // Phones reach it from the top bar's Balance instead (the bottom bar holds 5 items).
  desktopOnly?: boolean;
}

@Component({
  imports: [NgTemplateOutlet, RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatIconModule],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly me = inject(MeService);
  private readonly summaryService = inject(SummaryService);
  private readonly auth = inject(AuthService);
  private readonly injector = inject(Injector);
  private readonly document = inject(DOCUMENT);
  private readonly router = inject(Router);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  // Navigation only works once setup is complete (setupGuard), so it is hidden on sign-in,
  // no-access and setup.
  protected readonly showNav = computed(() => this.me.me()?.setupCompleted === true);

  // UX-02: the project name and the current month with its dates (R-CAL-03), from /api/me.
  protected readonly projectName = computed(() => this.me.me()?.profile?.projectName ?? null);
  protected readonly currentMonth = computed(() => {
    const period = this.me.me()?.currentPeriod;
    return period ? periodWithDates(period) : null;
  });

  // UX-08: "Left in Sep 2026 · 6 days left" (the month's remaining, carried, R-PLN-05, in its plan
  // status color) and "Balance" (the active accounts' total, R-BAL-03), or "Set up" until every
  // account has its opening balance. The API makes the sums; this only formats.
  protected readonly header = computed(() => {
    const summary = this.summaryService.summary();
    if (!summary) {
      return null;
    }
    const currency = this.me.me()?.profile?.currency ?? 'SAR';
    const money = (amount: number) => formatMoney(toMinor(amount), currency);
    const month = summary.month;
    const days = month ? (month.daysLeft === 1 ? 'last day' : `${month.daysLeft} days left`) : '';
    const accounts = summary.accounts
      .filter((account) => account.balance !== null)
      .map((account) => `${account.name} ${money(account.balance!)}`)
      .join(' · ');
    return {
      left: month
        ? {
            caption: `Left in ${month.period.label} · ${days}`,
            amount: money(month.remaining),
            status: spendStatus(toMinor(month.carriedIn) + toMinor(month.planned), toMinor(month.spent)) ?? '',
            label: `Left to spend in ${month.period.label}: ${money(month.remaining)}, ${days}. Open Tracker`,
          }
        : null,
      balance: {
        amount: summary.balance === null ? null : money(summary.balance),
        negative: (summary.balance ?? 0) < 0,
        title: accounts,
        label:
          summary.balance === null
            ? 'Balance: set up your bank balances. Open Banks'
            : `Balance ${money(summary.balance)}${accounts ? `: ${accounts}` : ''}. Open Banks`,
      },
    };
  });

  constructor() {
    // The top bar's numbers load once the navigation shows (setup done), and go on sign-out.
    effect(() => {
      if (this.showNav()) {
        untracked(() => void this.summaryService.load());
      } else {
        untracked(() => this.summaryService.clear());
      }
    });
  }

  // Desktop side bar (owner's choice, M8): Settings opens its sections under it while you are in
  // Settings. The gear in the top bar stays (UX-01); phones get the Settings list instead. People
  // is listed for admins only (STG-07).
  protected readonly settingsSections = computed(() => visibleSections(this.me.me()?.isAdmin === true));
  protected readonly inSettings = computed(() => this.url().startsWith('/settings'));

  // AUTH-04, owner's choice (M8): outside Settings, always visible (the bottom of the side bar, the
  // top bar on phones), after a confirmation. The dialog code is loaded on first use, so it stays
  // out of the initial bundle.
  protected async signOut(): Promise<void> {
    const [{ MatDialog }, { SignOutDialog }] = await Promise.all([
      import('@angular/material/dialog'),
      import('./shared/sign-out-dialog'),
    ]);
    const phone = this.document.defaultView?.matchMedia?.('(max-width: 767.98px)').matches ?? false;
    const dialog = this.injector.get(MatDialog).open<unknown, unknown, boolean>(SignOutDialog, dialogSize(phone, '400px'));
    if (await firstValueFrom(dialog.afterClosed())) {
      await this.auth.signOut();
    }
  }

  // UX-01: five always-visible items, Add is prominent and in the middle. Settings lives in the
  // top bar. Plan and Tracker (plan vs actual) sit side by side (owner's choice, 20 Oct 2026). Add
  // adds an expense or a transfer (TRF-01), so it is "Add". Banks (BNK-01) is in the side bar; on
  // phones the top bar's Balance opens it, so it is never hidden.
  protected readonly navItems: NavItem[] = [
    { path: '/', label: 'Dashboard', icon: 'dashboard' },
    { path: '/entries', label: 'Entries', icon: 'receipt_long' },
    { path: '/add', label: 'Add', icon: 'add', prominent: true },
    { path: '/plan', label: 'Plan', icon: 'event_note' },
    { path: '/tracker', label: 'Tracker', icon: 'query_stats' },
    { path: '/banks', label: 'Banks', icon: 'account_balance', desktopOnly: true },
  ];
}
