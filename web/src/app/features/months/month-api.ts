import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PeriodInfo } from '../../shared/month-label';
import { BankAccount, Entry } from '../add-entry/entry-api';
import { IncomeItem } from '../settings/settings-api';
import { OverviewFilter } from './overview-api';

// Owner note 9: GET /api/fiscal-years/{fyId}/months/{periodKey}, Tracker's one month in detail.
// Amounts are decimals (R-MON-01); the API makes every sum.
export interface DaySpent {
  date: string;
  spent: number;
}

export interface AccountSpent {
  bankAccountId: string;
  spent: number;
}

// A category's spending this month and in the month before (lastMonth).
export interface CategoryChange {
  categoryId: string;
  spent: number;
  lastSpent: number;
}

// Owner note 32 (R-PLN-09): a category's overspend not made up yet, what is left of its plan this month
// and after = left - overspent (below 0 when this month cannot cover it).
export interface OverspentCategory {
  categoryId: string;
  overspent: number;
  left: number;
  after: number;
}

// monthsBack is the months counted (the picked number, at most the FY's months before this one; 0 in
// its first month), from the first of them; total is every category's overspend; categories are only
// those with one, the shortest first.
export interface Overspent {
  monthsBack: number;
  from: PeriodInfo | null;
  total: number;
  categories: OverspentCategory[];
}

// days has every day of the month; accounts and categories are largest first; latest (newest first)
// and biggest hold at most 5 entries; count and total cover the whole month. lastMonth is null for
// the first stored month. bankAccounts feed the edit dialog. additionalIncome is the month's
// additional income, whatever it counts in (R-INC-04, owner note 17), biggest first; empty with a
// filter. overspent is what to make up, counted over the months back (owner note 32).
export interface MonthDetail {
  period: PeriodInfo;
  lastMonth: PeriodInfo | null;
  total: number;
  count: number;
  days: DaySpent[];
  accounts: AccountSpent[];
  categories: CategoryChange[];
  latest: Entry[];
  biggest: Entry[];
  bankAccounts: BankAccount[];
  additionalIncome: IncomeItem[];
  overspent: Overspent;
}

@Injectable({ providedIn: 'root' })
export class MonthApi {
  private readonly http = inject(HttpClient);

  // 404 when the FY or the month is unknown. Tracker's filter narrows every part; back is how many
  // months back the overspend is counted over (owner note 32, 1 when left out).
  month(fyId: string, periodKey: string, filter: OverviewFilter = {}, back = 1): Promise<MonthDetail> {
    let params = back > 1 ? new HttpParams().set('back', back) : new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value) {
        params = params.set(key, value);
      }
    }
    return firstValueFrom(
      this.http.get<MonthDetail>(`/api/fiscal-years/${fyId}/months/${periodKey}`, { params }),
    );
  }
}
