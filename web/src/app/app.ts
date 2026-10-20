import { DOCUMENT } from '@angular/common';
import { Component, Injector, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, firstValueFrom, map } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { AuthService } from './core/auth';
import { MeService } from './core/me';
import { visibleSections } from './features/settings/settings-sections';
import { dialogSize } from './shared/dialog';
import { periodWithDates } from './shared/month-label';

interface NavItem {
  path: string;
  label: string;
  icon: string;
  prominent?: boolean;
}

@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatIconModule],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly me = inject(MeService);
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
  // top bar. Plan and Tracker (plan vs actual) sit side by side (owner's choice, 20 Oct 2026).
  protected readonly navItems: NavItem[] = [
    { path: '/', label: 'Dashboard', icon: 'dashboard' },
    { path: '/entries', label: 'Entries', icon: 'receipt_long' },
    { path: '/add', label: 'Add', icon: 'add', prominent: true },
    { path: '/plan', label: 'Plan', icon: 'event_note' },
    { path: '/tracker', label: 'Tracker', icon: 'query_stats' },
  ];
}
