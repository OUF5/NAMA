import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MeService } from './core/me';

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

  // Navigation only works once setup is complete (setupGuard), so it is hidden on sign-in,
  // no-access and setup.
  protected readonly showNav = computed(() => this.me.me()?.setupCompleted === true);

  // UX-01: five always-visible items, Add is prominent. Settings lives in the top bar.
  protected readonly navItems: NavItem[] = [
    { path: '/', label: 'Dashboard', icon: 'dashboard' },
    { path: '/months', label: 'Months', icon: 'calendar_month' },
    { path: '/add', label: 'Add', icon: 'add', prominent: true },
    { path: '/entries', label: 'Entries', icon: 'receipt_long' },
    { path: '/plan', label: 'Plan', icon: 'event_note' },
  ];
}
