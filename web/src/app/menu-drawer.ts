import {
  Component,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink, RouterLinkActive } from '@angular/router';

// One page of the navigation (UX-01).
export interface NavItem {
  path: string;
  label: string;
  icon: string;
  prominent?: boolean;
  // Phones reach it from the drawer and the top bar's Balance instead (the bottom bar holds 5 items).
  desktopOnly?: boolean;
}

// Owner note 9, phones: every page, Settings and Sign out in a drawer over the page. It closes on a tap
// outside, on Escape, on Close and when a page opens (the shell). The shell loads it the first time it
// opens (@defer), so it is not in the initial bundle. It takes the focus on its Close button.
@Component({
  selector: 'app-menu-drawer',
  imports: [RouterLink, RouterLinkActive, MatButtonModule, MatIconModule],
  template: `
    <div class="backdrop" aria-hidden="true" (click)="closed.emit()"></div>
    <nav id="drawer" class="drawer" aria-label="All pages" (keydown.escape)="closed.emit()">
      <div class="drawer-head">
        <span class="brand"><span class="logo" aria-hidden="true"></span>NAMA</span>
        <button
          mat-icon-button
          type="button"
          class="drawer-close"
          aria-label="Close the menu"
          (click)="closed.emit()"
        >
          <mat-icon>close</mat-icon>
        </button>
      </div>
      @for (item of pages(); track item.path) {
        <a
          class="drawer-item"
          [routerLink]="item.path"
          routerLinkActive="active"
          [routerLinkActiveOptions]="{ exact: item.path === '/' }"
          (click)="closed.emit()"
        >
          <mat-icon aria-hidden="true">{{ item.icon }}</mat-icon>
          <span>{{ item.label }}</span>
        </a>
      }
      <button type="button" class="drawer-item sign-out" (click)="signOut.emit()">
        <mat-icon aria-hidden="true">logout</mat-icon>
        <span>Sign out</span>
      </button>
    </nav>
  `,
  styleUrl: './menu-drawer.scss',
})
export class MenuDrawer {
  readonly items = input.required<NavItem[]>();
  // Every page, then Settings (the side bar has it apart); Sign out follows.
  protected readonly pages = computed(() => [
    ...this.items(),
    { path: '/settings', label: 'Settings', icon: 'settings' },
  ]);
  readonly closed = output<void>();
  readonly signOut = output<void>();

  constructor() {
    const host = inject<ElementRef<HTMLElement>>(ElementRef);
    afterNextRender(() => host.nativeElement.querySelector<HTMLElement>('.drawer-close')?.focus());
  }
}
