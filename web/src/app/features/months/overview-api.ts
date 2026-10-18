import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PeriodInfo } from '../../shared/month-label';
import { Rollover } from '../../shared/rollover-banner';
import { Category, FiscalYear } from '../plan/plan-api';

// GET /api/fiscal-years/{fyId}/overview (PRD 7.5). Amounts are decimals in JSON (R-MON-01); every
// sum is made by the API, the page only formats. Remaining is planned minus spent (M7).

// R-STA-01, worked out by the API with today in the profile time zone.
export type MonthStatus = 'closed' | 'inProgress' | 'yetToCome';

export interface MonthOverview {
  period: PeriodInfo;
  status: MonthStatus;
  income: number;
  planned: number;
  spent: number;
  remaining: number;
}

export interface Cell {
  periodKey: string;
  planned: number;
  spent: number;
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

export interface Totals {
  income: number;
  planned: number;
  spent: number;
  remaining: number;
}

// fiscalYears (oldest first) feed the MON-03 selector; categories is GET /api/categories (names and
// the edit dialog's pickers). rollover is the FY-01 banner (null when hidden).
export interface OverviewResponse {
  fiscalYears: FiscalYear[];
  fyId: string;
  months: MonthOverview[];
  categories: Category[];
  rows: CategoryRow[];
  totals: Totals;
  rollover: Rollover | null;
}

@Injectable({ providedIn: 'root' })
export class OverviewApi {
  private readonly http = inject(HttpClient);

  // 404 when the FY is unknown.
  overview(fyId: string): Promise<OverviewResponse> {
    return firstValueFrom(this.http.get<OverviewResponse>(`/api/fiscal-years/${fyId}/overview`));
  }
}
