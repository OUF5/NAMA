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

// income is null when Tracker is filtered to a category: income belongs to the whole budget.
export interface MonthOverview {
  period: PeriodInfo;
  status: MonthStatus;
  income: number | null;
  carriedIn: number;
  planned: number;
  spent: number;
  remaining: number;
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
// null with a filter.
export interface Totals {
  income: number | null;
  planned: number;
  spent: number;
  remaining: number;
  plannedSaved: number | null;
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

// fiscalYears (oldest first) feed the MON-03 selector; categories is GET /api/categories (names and
// the edit dialog's pickers). rollover is the FY-01 banner (null when hidden). toDate is Dashboard's
// "FY to date".
export interface OverviewResponse {
  fiscalYears: FiscalYear[];
  fyId: string;
  months: MonthOverview[];
  categories: Category[];
  rows: CategoryRow[];
  totals: Totals;
  toDate: ToDate;
  rollover: Rollover | null;
  // R-PLN-06: the first month that carries; earlier months stand alone, and to date starts there.
  carryFromPeriodKey: string | null;
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
}
