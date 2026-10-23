import { HttpClient, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom, tap } from 'rxjs';
import { PeriodInfo } from '../shared/month-label';
import { MeService } from './me';

// GET /api/summary (UX-08). Amounts are decimals (R-MON-01). balance and openingDate are null on an
// account without an opening balance (R-BAL-01).
export interface SummaryAccount {
  id: string;
  name: string;
  balance: number | null;
  openingDate: string | null;
}

// The month in progress for the whole budget: remaining carries (R-PLN-05), unassigned carries
// (R-PLN-03). daysLeft counts today.
export interface SummaryMonth {
  period: PeriodInfo;
  // Today's day of the month (1 on payday) and the month's length (DSH-01's marker).
  day: number;
  days: number;
  daysLeft: number;
  income: number;
  carriedIn: number;
  planned: number;
  spent: number;
  remaining: number;
  unassigned: number;
}

// A month yet to come with its income minus its planned (R-PLN-07).
export interface FutureMonth {
  period: PeriodInfo;
  unallocated: number;
}

// Dashboard's Projection and Planned saving vs projection (DSH-05, DSH-06) for the FY that contains
// today: R-PLN-07 projectedBalance = balanceToday - stillToSpend + futureUnallocated, and R-PLN-08
// projectedSaving = savedToDate - stillToSpend + futureUnallocated. The API makes every sum.
// balanceToday and projectedBalance are null until the balance is complete.
export interface Projection {
  fiscalYearLabel: string;
  endDate: string;
  balanceToday: number | null;
  stillToSpend: number;
  futureUnallocated: number;
  futureMonths: FutureMonth[];
  projectedBalance: number | null;
  savedToDate: number;
  plannedSaved: number;
  projectedSaving: number;
  // R-PLN-06: the month savedToDate and plannedSaved count from when NAMA was set up after the FY's
  // first month; null when they cover the whole FY (owner note 11).
  countedFrom: PeriodInfo | null;
}

// balance is the active accounts' total, null until complete (every active account has an opening
// balance and income has a split). month and projection are null when no FY contains today.
export interface Summary {
  balance: number | null;
  complete: boolean;
  accounts: SummaryAccount[];
  month: SummaryMonth | null;
  projection: Projection | null;
}

// UX-08: the top bar's numbers. Loaded once the shell shows the navigation, and again after every
// change (summaryInterceptor), so it never shows an old balance. It belongs to the shell, so a page
// still makes its own 1 or 2 calls (NFR-01).
@Injectable({ providedIn: 'root' })
export class SummaryService {
  private readonly http = inject(HttpClient);
  private readonly me = inject(MeService);

  // null until loaded, and when it could not be loaded (the top bar then shows nothing).
  readonly summary = signal<Summary | null>(null);

  private request = 0;
  private timer: ReturnType<typeof setTimeout> | null = null;

  // Only the latest answer is kept.
  async load(): Promise<void> {
    const request = ++this.request;
    try {
      const summary = await firstValueFrom(this.http.get<Summary>('/api/summary'));
      if (request === this.request) {
        this.summary.set(summary);
      }
    } catch {
      if (request === this.request) {
        this.summary.set(null);
      }
    }
  }

  // After a change: several changes in a row (a save, then a page reload) make one call. Before
  // setup (joining with an invite) there is nothing to show yet.
  refresh(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
    }
    this.timer = setTimeout(() => {
      this.timer = null;
      if (this.me.me()?.setupCompleted) {
        void this.load();
      }
    }, 300);
  }

  clear(): void {
    this.request++;
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
    this.summary.set(null);
  }
}

// Every successful change to the API (POST, PUT, DELETE) can move a balance or this month's numbers,
// so the top bar loads again.
export const summaryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method === 'GET' || !req.url.startsWith('/api/')) {
    return next(req);
  }
  const summary = inject(SummaryService);
  return next(req).pipe(
    tap((event) => {
      if (event instanceof HttpResponse) {
        summary.refresh();
      }
    }),
  );
};
