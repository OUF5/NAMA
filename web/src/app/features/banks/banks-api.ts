import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { FiscalYear } from '../plan/plan-api';

// PRD 5.11 Banks and 7.5. Amounts and percents are decimals in JSON (R-MON-01); dates are YYYY-MM-DD.

// R-BAL-03: what makes up an account's balance today.
export interface AccountBalance {
  opening: number;
  income: number;
  spent: number;
  transfersIn: number;
  transfersOut: number;
  balance: number;
}

// openingBalance and openingDate are null until set (R-BAL-01), and balance with them. share is the
// account's percent of the shown month's income and monthIncome what that gives (null without a split).
export interface Account {
  id: string;
  name: string;
  archived: boolean;
  openingBalance: number | null;
  openingDate: string | null;
  share: number | null;
  monthIncome: number | null;
  balance: AccountBalance | null;
}

// R-BAL-02: account id → percent (80.5 is 80.5%), from a month on.
export interface Split {
  fromPeriodKey: string;
  shares: Record<string, number>;
}

// R-TRF-01. The API sets periodKey and fyId from the date.
export interface NewTransfer {
  fromBankAccountId: string;
  toBankAccountId: string;
  date: string;
  amount: number;
  note: string;
}

export interface Transfer extends NewTransfer {
  id: string;
  periodKey: string;
  fyId: string;
}

// GET /api/banks. Accounts by name, archived included. total is null until an active account has a
// balance; complete is false until every active one has and income has a split. split is the one of
// the shown month (today's, else the latest stored month); splitHistory is newest first. income is
// month key → total income, for the split dialog's preview. Transfers are newest first.
export interface BanksResponse {
  today: string;
  currentPeriodKey: string | null;
  accounts: Account[];
  total: number | null;
  complete: boolean;
  split: Split | null;
  splitHistory: Split[];
  fiscalYears: FiscalYear[];
  income: Record<string, number>;
  transfers: Transfer[];
}

export interface AccountChange {
  name: string;
  archived?: boolean;
  openingBalance: number | null;
  openingDate: string | null;
}

@Injectable({ providedIn: 'root' })
export class BanksApi {
  private readonly http = inject(HttpClient);

  banks(): Promise<BanksResponse> {
    return firstValueFrom(this.http.get<BanksResponse>('/api/banks'));
  }

  // BNK-02. Both answer with GET /api/banks, because a change moves the balances. No delete (PRD 4.6).
  addAccount(change: AccountChange): Promise<BanksResponse> {
    return firstValueFrom(this.http.post<BanksResponse>('/api/bank-accounts', change));
  }

  updateAccount(id: string, change: AccountChange): Promise<BanksResponse> {
    return firstValueFrom(this.http.put<BanksResponse>(`/api/bank-accounts/${id}`, change));
  }

  // R-BAL-02: shares in percent; together exactly 100.
  setSplit(effectiveFromPeriodKey: string, shares: Record<string, number>): Promise<BanksResponse> {
    return firstValueFrom(this.http.put<BanksResponse>('/api/income-split', { effectiveFromPeriodKey, shares }));
  }

  // R-TRF-01: 422 when the date is outside every FY, 400 for other errors (ProblemDetails).
  addTransfer(transfer: NewTransfer): Promise<Transfer> {
    return firstValueFrom(this.http.post<Transfer>('/api/transfers', transfer));
  }

  updateTransfer(id: string, transfer: NewTransfer): Promise<Transfer> {
    return firstValueFrom(this.http.put<Transfer>(`/api/transfers/${id}`, transfer));
  }

  deleteTransfer(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/transfers/${id}`));
  }
}
