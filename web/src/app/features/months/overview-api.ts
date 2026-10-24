import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PeriodInfo } from '../../shared/month-label';
import { Rollover } from '../../shared/rollover-banner';
import { Category, FiscalYear } from '../plan/plan-api';

// GET /api/fiscal-years/{fyId}/overview (PRD 7.5), for Months and Dashboard. Amounts are decimals in JSON (R-MON-01); every
// sum is made by the API, the page only formats. R-PLN-05: what is left carries into the next month,
// so remaining = carriedIn + planned - spent, from 0 at the FY start; carriedIn + planned is "available".

// R-STA-01, worked out by the API with today in the profile time zone.
export type MonthStatus = 'closed' | 'inProgress' | 'yetToCome';

// income is the actual income and plannedIncome Plan's (R-INC-01, owner note 17); both null when
// Tracker is filtered to a category: income belongs to the whole budget. unspent is the month's
// income minus its spent and saved that added up from the FY's first month (owner note 18, Totals);
// both null with a filter and for a month yet to come. projectedUnspent and projectedSaved carry them
// on to the FY's end (owner note 23): the month in progress also spends the rest of its plan, a month
// yet to come is its income minus its planned; null for a closed month and with a filter.
// additionalIncome is the part of income from additional items (Actual and Both), null with a filter.
export interface MonthOverview {
  period: PeriodInfo;
  status: MonthStatus;
  income: number | null;
  plannedIncome: number | null;
  additionalIncome: number | null;
  carriedIn: number;
  planned: number;
  spent: number;
  remaining: number;
  unspent: number | null;
  saved: number | null;
  projectedUnspent: number | null;
  projectedSaved: number | null;
}

export interface Cell {
  periodKey: string;
  carriedIn: number;
  planned: number;
  spent: number;
  remaining: number;
}

export interface SubCategoryRow {
  subCategoryId: string;
  months: Cell[];
  planned: number;
  spent: number;
}

// Only sub-categories with a plan or spending in the FY (owner's choice, M7).
export interface CategoryRow {
  categoryId: string;
  months: Cell[];
  planned: number;
  spent: number;
  subCategories: SubCategoryRow[];
}

// The FY: remaining is FY planned - FY spent. plannedSaved (R-PLN-04) is the last month's Unassigned,
// null with a filter. saved is the latest started month's saved (owner note 18), null with a filter or
// before the FY starts. projectedSaved is the accumulated savings projected at the FY's end (owner
// note 23), null with a filter or once every month is closed.
export interface Totals {
  income: number | null;
  plannedIncome: number | null;
  additionalIncome: number | null;
  planned: number;
  spent: number;
  remaining: number;
  plannedSaved: number | null;
  saved: number | null;
  projectedSaved: number | null;
}

// DSH-02 (owner's choice, M9): the months that started on or before today, each with its full income
// and every entry in it. saved is income minus spent and can be negative. plannedSaved is the
// Unassigned of the latest started month (R-PLN-03): what the plan meant to keep by now.
// planned is the started months' planned spending; savedClosed is income minus spent of the closed
// months only (DSH-04 "this FY added").
export interface ToDate {
  income: number;
  planned: number;
  spent: number;
  saved: number;
  plannedSaved: number;
  savedClosed: number;
}

// Owner note 24, Dashboard's "Plan vs actual" (DSH-02) and "Saving: plan vs projection" (DSH-06),
// from the FY's first month. The started months: planned and actual income (additionalIncome the part
// from additional items), planned and spent, plannedSaved (Plan's Accumulated Planned Savings) and
// saved (Tracker's Accumulated savings); stillToSpend is what is left of the month in progress's plan.
// At the FY's end: plannedSavedAtEnd (Plan's) and projectedSaved (Tracker's) = saved - stillToSpend +
// futureUnspent.
export interface PlanVsActual {
  plannedIncome: number;
  income: number;
  additionalIncome: number;
  planned: number;
  spent: number;
  stillToSpend: number;
  plannedSaved: number;
  saved: number;
  futureUnspent: number;
  plannedSavedAtEnd: number;
  projectedSaved: number;
}

// fiscalYears (oldest first) feed the MON-03 selector; categories is GET /api/categories (names and
// the edit dialog's pickers). rollover is the FY-01 banner (null when hidden). toDate is the FY to
// date from the first month that carries (DSH-04 uses its savedClosed); planVsActual is null with a
// filter.
export interface OverviewResponse {
  fiscalYears: FiscalYear[];
  fyId: string;
  months: MonthOverview[];
  categories: Category[];
  rows: CategoryRow[];
  totals: Totals;
  toDate: ToDate;
  planVsActual: PlanVsActual | null;
  rollover: Rollover | null;
  // R-PLN-06: the first month that carries; earlier months stand alone, and to date starts there.
  carryFromPeriodKey: string | null;
}

// Owner note 9: GET /api/fiscal-years/{fyId}/balances, the Year view's Balance row. Each month's
// balance at its end: real for closed months, projected (R-PLN-07) for the month in progress and the
// months yet to come. null when unknown (before an account's opening date, an FY not started, or until
// the balance is complete).
export interface MonthBalance {
  periodKey: string;
  balance: number | null;
  projected: boolean;
}

export interface BalancesResponse {
  complete: boolean;
  months: MonthBalance[];
}

// Tracker's filter (owner's choice, 20 Oct 2026): rows, months and totals cover only this category
// or sub-category. toDate always covers everything.
export interface OverviewFilter {
  categoryId?: string;
  subCategoryId?: string;
}

@Injectable({ providedIn: 'root' })
export class OverviewApi {
  private readonly http = inject(HttpClient);

  // 404 when the FY, the category or the sub-category is unknown.
  overview(fyId: string, filter: OverviewFilter = {}): Promise<OverviewResponse> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value) {
        params = params.set(key, value);
      }
    }
    return firstValueFrom(this.http.get<OverviewResponse>(`/api/fiscal-years/${fyId}/overview`, { params }));
  }

  // Only Totals and the Year view ask: balances read every entry since the earliest opening date.
  balances(fyId: string): Promise<BalancesResponse> {
    return firstValueFrom(this.http.get<BalancesResponse>(`/api/fiscal-years/${fyId}/balances`));
  }
}
