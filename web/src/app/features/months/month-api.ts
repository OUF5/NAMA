import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PeriodInfo } from '../../shared/month-label';
import { BankAccount, Entry } from '../add-entry/entry-api';
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

// days has every day of the month; accounts and categories are largest first; latest (newest first)
// and biggest hold at most 5 entries; count and total cover the whole month. lastMonth is null for
// the first stored month. bankAccounts feed the edit dialog.
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
}

@Injectable({ providedIn: 'root' })
export class MonthApi {
  private readonly http = inject(HttpClient);

  // 404 when the FY or the month is unknown. Tracker's filter narrows every part.
  month(fyId: string, periodKey: string, filter: OverviewFilter = {}): Promise<MonthDetail> {
    let params = new HttpParams();
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
