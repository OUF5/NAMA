import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Me, Profile } from '../../core/me';
import { Category, FiscalYear, SubCategory } from '../plan/plan-api';

// PRD 5.7 Settings. Amounts are decimals in JSON (R-MON-01); every sum comes from the API.

// STG-02, R-CAL-07: the settings apply from the FY after the latest stored one. nextFiscalYear is
// that FY as it would be created now.
export interface Calendar {
  fiscalStartMonth: number;
  paydayDay: number;
  takesEffectFrom: string;
  nextFiscalYear: FiscalYear;
}

// R-INC-03: "once" has toPeriodKey equal to fromPeriodKey; "monthly" with a null toPeriodKey is open ended.
export interface IncomeItem {
  id: string;
  kind: 'salary' | 'additional';
  label: string;
  amount: number;
  type: 'once' | 'monthly';
  fromPeriodKey: string;
  toPeriodKey: string | null;
}

// GET /api/income. Items are newest first. currentTotal is the month that contains today (STG-03),
// null when no FY contains today.
export interface Income {
  fiscalYears: FiscalYear[];
  fyId: string;
  salaries: IncomeItem[];
  additional: IncomeItem[];
  totals: { periodKey: string; total: number }[];
  currentPeriodKey: string | null;
  currentTotal: number | null;
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
  categories: Category[];
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
  type: 'once' | 'monthly';
  fromPeriodKey: string;
  toPeriodKey: string | null;
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

  updateCalendar(fiscalStartMonth: number, paydayDay: number): Promise<Calendar> {
    return firstValueFrom(this.http.put<Calendar>('/api/settings/calendar', { fiscalStartMonth, paydayDay }));
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

  // Rename, archive and restore (STG-05). There is no delete (PRD 4.6).
  updateCategory(id: string, name: string, archived: boolean): Promise<Category> {
    return firstValueFrom(this.http.put<Category>(`/api/categories/${id}`, { name, archived }));
  }

  updateSubCategory(id: string, name: string, archived: boolean): Promise<SubCategory> {
    return firstValueFrom(this.http.put<SubCategory>(`/api/sub-categories/${id}`, { name, archived }));
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
