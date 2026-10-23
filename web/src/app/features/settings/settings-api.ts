import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Me, Profile } from '../../core/me';
import { IncomeCounts } from '../../shared/income-months';
import { MonthNaming } from '../../shared/month-label';
import { BankAccount } from '../add-entry/entry-api';
import { FiscalYear } from '../plan/plan-api';

// PRD 5.7 Settings. Amounts are decimals in JSON (R-MON-01); every sum comes from the API.

// STG-02, R-CAL-07: the start month and payday apply from the FY after the latest stored one.
// nextFiscalYear is that FY as it would be created now. monthNaming applies right away (R-CAL-11).
export interface Calendar {
  fiscalStartMonth: number;
  paydayDay: number;
  monthNaming: MonthNaming;
  monthKeyOffset: number;
  takesEffectFrom: string;
  nextFiscalYear: FiscalYear;
}

// R-INC-03: "once" has toPeriodKey equal to fromPeriodKey; "monthly" with a null toPeriodKey is open ended.
// counts is R-INC-04. bankAccountId is the account it goes into on payday, null through the income
// split (R-INC-05).
export interface IncomeItem {
  id: string;
  kind: 'salary' | 'additional';
  label: string;
  amount: number;
  type: 'once' | 'monthly';
  fromPeriodKey: string;
  toPeriodKey: string | null;
  counts: IncomeCounts;
  bankAccountId: string | null;
}

// GET /api/income. Items are newest first. totals are Plan's and the actual income of each month
// (R-INC-01). currentTotal (actual) and currentPlanned are the month that contains today (STG-03),
// null when no FY contains today. bankAccounts (archived ones flagged) feed the account picker.
export interface Income {
  fiscalYears: FiscalYear[];
  fyId: string;
  salaries: IncomeItem[];
  additional: IncomeItem[];
  totals: { periodKey: string; planned: number; actual: number }[];
  currentPeriodKey: string | null;
  currentTotal: number | null;
  currentPlanned: number | null;
  bankAccounts: BankAccount[];
}

// AUTH-07, STG-07. Dates are YYYY-MM-DD in the admin's time zone. A member id is not a user id.
export interface Member {
  id: string;
  email: string;
  joinedOn: string;
}

export interface Invite {
  id: string;
  createdOn: string;
  expiresOn: string;
  expired: boolean;
}

export interface People {
  admins: string[];
  members: Member[];
  invites: Invite[];
}

// AUTH-06: the token is sent only once; just its hash is stored.
export interface CreatedInvite {
  token: string;
  expiresOn: string;
  people: People;
}

// Owner's choice (M8, NFR-01): every section in one call. people is null unless you are an admin.
export interface SettingsResponse {
  profile: Profile;
  calendar: Calendar;
  income: Income;
  people: People | null;
}

export interface ProfileChange {
  displayName: string;
  projectName: string;
  currency: string;
  timezone: string;
}

export interface IncomeItemChange {
  label: string;
  amount: number;
  counts: IncomeCounts;
  type: 'once' | 'monthly';
  fromPeriodKey: string;
  toPeriodKey: string | null;
  bankAccountId: string | null;
}

@Injectable({ providedIn: 'root' })
export class SettingsApi {
  private readonly http = inject(HttpClient);

  settings(): Promise<SettingsResponse> {
    return firstValueFrom(this.http.get<SettingsResponse>('/api/settings'));
  }

  // STG-06: the git commit of the running API.
  version(): Promise<string> {
    return firstValueFrom(this.http.get<{ version: string }>('/api/health')).then((health) => health.version);
  }

  // Answers with the new /api/me.
  updateProfile(change: ProfileChange): Promise<Me> {
    return firstValueFrom(this.http.put<Me>('/api/profile', change));
  }

  updateCalendar(fiscalStartMonth: number, paydayDay: number, monthNaming: MonthNaming): Promise<Calendar> {
    return firstValueFrom(this.http.put<Calendar>('/api/settings/calendar', { fiscalStartMonth, paydayDay, monthNaming }));
  }

  // Add's Income (INC-01): the month pickers and the current month, without the rest of Settings.
  income(): Promise<Income> {
    return firstValueFrom(this.http.get<Income>('/api/income'));
  }

  changeSalary(amount: number, effectiveFromPeriodKey: string): Promise<Income> {
    return firstValueFrom(this.http.post<Income>('/api/income/salary', { amount, effectiveFromPeriodKey }));
  }

  addIncome(item: IncomeItemChange): Promise<Income> {
    return firstValueFrom(this.http.post<Income>('/api/income/additional', item));
  }

  updateIncome(id: string, item: IncomeItemChange): Promise<Income> {
    return firstValueFrom(this.http.put<Income>(`/api/income/additional/${id}`, item));
  }

  deleteIncome(id: string): Promise<Income> {
    return firstValueFrom(this.http.delete<Income>(`/api/income/additional/${id}`));
  }

  // AUTH-06, AUTH-07 (admins only). Each answers with the whole People list.
  createInvite(): Promise<CreatedInvite> {
    return firstValueFrom(this.http.post<CreatedInvite>('/api/invites', {}));
  }

  revokeInvite(id: string): Promise<{ people: People }> {
    return firstValueFrom(this.http.delete<{ people: People }>(`/api/invites/${id}`));
  }

  removePerson(memberId: string): Promise<{ people: People }> {
    return firstValueFrom(this.http.delete<{ people: People }>(`/api/people/${memberId}`));
  }
}
