import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PeriodInfo } from '../../shared/month-label';

// PRD 7.5. Amounts are decimals in JSON (R-MON-01); dates are YYYY-MM-DD.
export interface BankAccount {
  id: string;
  name: string;
  archived: boolean;
}

// ENT-01, for one sub-category in the month of a date. percent is null when nothing is planned.
export interface Indicator {
  period: PeriodInfo;
  planned: number;
  spent: number;
  remaining: number;
  percent: number | null;
}

export interface NewEntry {
  subCategoryId: string;
  bankAccountId: string;
  date: string;
  amount: number;
  note: string;
}

// The API sets periodKey and fyId from the date (R-CAL-09).
export interface Entry extends NewEntry {
  id: string;
  periodKey: string;
  fyId: string;
  categoryId: string;
}

@Injectable({ providedIn: 'root' })
export class EntryApi {
  private readonly http = inject(HttpClient);

  // Sorted by name; archived ones are included and flagged.
  bankAccounts(): Promise<BankAccount[]> {
    return firstValueFrom(this.http.get<BankAccount[]>('/api/bank-accounts'));
  }

  indicator(subCategoryId: string, date: string): Promise<Indicator> {
    const params = new HttpParams().set('subCategoryId', subCategoryId).set('date', date);
    return firstValueFrom(this.http.get<Indicator>('/api/indicator', { params }));
  }

  // 422 when the date is outside every FY, 400 for other errors (ProblemDetails).
  addEntry(entry: NewEntry): Promise<Entry> {
    return firstValueFrom(this.http.post<Entry>('/api/entries', entry));
  }
}
