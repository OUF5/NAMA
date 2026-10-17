import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PeriodInfo } from '../../shared/month-label';
import { FiscalYear } from '../plan/plan-api';

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

// TBL-04 filters, with the API parameter names; they are also the Entries page URL (MON-04).
export interface EntryFilters {
  fyId?: string;
  periodKey?: string;
  categoryId?: string;
  subCategoryId?: string;
  bankAccountId?: string;
  q?: string;
}

// GET /api/entries: the FYs (oldest first), the FY shown (by default the one that contains today),
// the accounts, the filtered entries newest first, and their total, summed by the API (TBL-04).
export interface EntriesResponse {
  fiscalYears: FiscalYear[];
  fyId: string;
  bankAccounts: BankAccount[];
  entries: Entry[];
  total: number;
}

function filterParams(filters: EntryFilters): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(filters)) {
    if (value) {
      params = params.set(key, value);
    }
  }
  return params;
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

  entries(filters: EntryFilters): Promise<EntriesResponse> {
    return firstValueFrom(this.http.get<EntriesResponse>('/api/entries', { params: filterParams(filters) }));
  }

  // ENT-04: the API moves the entry to the month of its date. 404 when it no longer exists.
  updateEntry(id: string, entry: NewEntry): Promise<Entry> {
    return firstValueFrom(this.http.put<Entry>(`/api/entries/${id}`, entry));
  }

  deleteEntry(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/entries/${id}`));
  }

  // TBL-06: a request (not a link), so the interceptor adds the token.
  exportCsv(filters: EntryFilters): Promise<Blob> {
    return firstValueFrom(this.http.get('/api/entries/export.csv', { params: filterParams(filters), responseType: 'blob' }));
  }
}
