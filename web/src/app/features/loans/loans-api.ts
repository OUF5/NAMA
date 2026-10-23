import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PeriodInfo } from '../../shared/month-label';

// PRD 5.12 Loans and 7.5 (owner note 20). Information only (R-LON-02). Amounts are decimals in JSON
// (R-MON-01); dates are YYYY-MM-DD.

// R-LON-01: lent (you gave the money; they owe you) or borrowed (you received it; you owe).
export type LoanType = 'lent' | 'borrowed';

// settledOn is null while open (R-LON-03).
export interface Loan {
  id: string;
  name: string;
  type: LoanType;
  amount: number;
  date: string;
  bankAccountId: string;
  note: string;
  settledOn: string | null;
}

export interface NewLoan {
  name: string;
  type: LoanType;
  amount: number;
  date: string;
  bankAccountId: string;
  note: string;
}

export interface TypeTotals {
  open: number;
  openCount: number;
  settled: number;
  settledCount: number;
}

// R-LON-04: net is Lent − Borrowed of the open loans.
export interface LoanTotals {
  lent: TypeTotals;
  borrowed: TypeTotals;
  net: number;
}

// LON-03 By person.
export interface LoanPerson {
  name: string;
  lentOpen: number;
  borrowedOpen: number;
  openCount: number;
}

// Every account, archived ones too; balance is today's (R-BAL-03), null without an opening balance.
export interface LoanAccount {
  id: string;
  name: string;
  archived: boolean;
  balance: number | null;
}

// LON-02: the current FY's month ends, real or projected (Tracker's Balance row).
export interface MonthEnd {
  period: PeriodInfo;
  balance: number | null;
  projected: boolean;
}

export interface LoansResponse {
  today: string;
  loans: Loan[];
  totals: LoanTotals;
  people: LoanPerson[];
  accounts: LoanAccount[];
  lastBankAccountId: string | null;
  complete: boolean;
  balance: number | null;
  months: MonthEnd[];
}

// Every change answers like GET /api/loans (NFR-01).
@Injectable({ providedIn: 'root' })
export class LoansApi {
  private readonly http = inject(HttpClient);

  loans(): Promise<LoansResponse> {
    return firstValueFrom(this.http.get<LoansResponse>('/api/loans'));
  }

  // DSH-07: no names.
  totals(): Promise<LoanTotals> {
    return firstValueFrom(this.http.get<LoanTotals>('/api/loans/totals'));
  }

  add(loan: NewLoan): Promise<LoansResponse> {
    return firstValueFrom(this.http.post<LoansResponse>('/api/loans', loan));
  }

  update(id: string, loan: NewLoan): Promise<LoansResponse> {
    return firstValueFrom(this.http.put<LoansResponse>(`/api/loans/${id}`, loan));
  }

  // R-LON-03: null opens it again.
  settle(id: string, settledOn: string | null): Promise<LoansResponse> {
    return firstValueFrom(this.http.put<LoansResponse>(`/api/loans/${id}/settled`, { settledOn }));
  }

  remove(id: string): Promise<LoansResponse> {
    return firstValueFrom(this.http.delete<LoansResponse>(`/api/loans/${id}`));
  }
}
