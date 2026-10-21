import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PeriodInfo } from '../../shared/month-label';

// PRD 7.5. Amounts are decimals in JSON (R-MON-01); the page turns them into minor units.
export interface SubCategory {
  id: string;
  categoryId: string;
  name: string;
  sortOrder: number;
  archived: boolean;
}

export interface Category {
  id: string;
  name: string;
  sortOrder: number;
  archived: boolean;
  subCategories: SubCategory[];
}

// The months are the ones stored on the FY (R-CAL-09).
export interface FiscalYear {
  id: string;
  label: string;
  startDate: string;
  endDate: string;
  isTransition: boolean;
  periods: PeriodInfo[];
}

export interface PlanItem {
  subCategoryId: string;
  categoryId: string;
  amounts: Record<string, number>;
  total: number;
}

// PLN-03: one row per month, worked out in the API (R-PLN-02, R-PLN-03). thisMonth is the month on its
// own (income - planned); unassigned ("Accumulated Planned Savings") is every thisMonth added up from
// the FY's first month (owner note 10).
export interface MonthTotals {
  periodKey: string;
  income: number;
  planned: number;
  thisMonth: number;
  unassigned: number;
}

// The FY column of PLN-03, the whole FY, so Income - Planned = plannedSaved, the last month's
// Unassigned (R-PLN-04).
export interface FiscalYearTotals {
  income: number;
  planned: number;
  plannedSaved: number;
}

// fiscalYears (oldest first) feed Plan's FY selector (owner's choice, M8).
export interface PlansResponse {
  fiscalYears: FiscalYear[];
  fiscalYear: FiscalYear;
  plans: PlanItem[];
  categoryTotals: Record<string, number>;
  // Category id → month key → planned (R-PLN-02), for the month column (M8).
  categoryMonthTotals: Record<string, Record<string, number>>;
  totals: MonthTotals[];
  fiscalYearTotals: FiscalYearTotals;
}

@Injectable({ providedIn: 'root' })
export class PlanApi {
  private readonly http = inject(HttpClient);

  categories(): Promise<Category[]> {
    return firstValueFrom(this.http.get<Category[]>('/api/categories'));
  }

  fiscalYears(): Promise<FiscalYear[]> {
    return firstValueFrom(this.http.get<FiscalYear[]>('/api/fiscal-years'));
  }

  plans(fyId: string): Promise<PlansResponse> {
    return firstValueFrom(this.http.get<PlansResponse>(`/api/fiscal-years/${fyId}/plans`));
  }

  // Replaces all months of one sub-category; months left out are cleared. Answers like plans().
  savePlan(fyId: string, subCategoryId: string, amounts: Record<string, number>): Promise<PlansResponse> {
    return firstValueFrom(
      this.http.put<PlansResponse>(`/api/fiscal-years/${fyId}/plans/${subCategoryId}`, { amounts }),
    );
  }

  addCategory(name: string): Promise<Category> {
    return firstValueFrom(this.http.post<Category>('/api/categories', { name }));
  }

  addSubCategory(categoryId: string, name: string): Promise<SubCategory> {
    return firstValueFrom(this.http.post<SubCategory>('/api/sub-categories', { categoryId, name }));
  }

  // Rename, archive and restore (STG-05, on Plan's Categories tab since owner note 9). There is no
  // delete (PRD 4.6).
  updateCategory(id: string, name: string, archived: boolean): Promise<Category> {
    return firstValueFrom(this.http.put<Category>(`/api/categories/${id}`, { name, archived }));
  }

  updateSubCategory(id: string, name: string, archived: boolean): Promise<SubCategory> {
    return firstValueFrom(this.http.put<SubCategory>(`/api/sub-categories/${id}`, { name, archived }));
  }
}
