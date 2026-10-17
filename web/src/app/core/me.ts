import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PeriodInfo } from '../shared/month-label';

export interface Profile {
  displayName: string;
  email: string;
  projectName: string;
  currency: string;
  timezone: string;
  fiscalStartMonth: number;
  paydayDay: number;
}

// GET /api/me (PRD 7.5). The current FY and month are null when no FY contains today.
export interface Me {
  setupCompleted: boolean;
  profile: Profile | null;
  currentFyId: string | null;
  currentPeriodKey: string | null;
  currentPeriod: PeriodInfo | null;
}

// Loads /api/me once per sign-in.
@Injectable({ providedIn: 'root' })
export class MeService {
  private readonly http = inject(HttpClient);

  // null until loaded, and again after sign-out.
  readonly me = signal<Me | null>(null);

  async load(): Promise<Me> {
    const cached = this.me();
    if (cached) {
      return cached;
    }
    const me = await firstValueFrom(this.http.get<Me>('/api/me'));
    this.me.set(me);
    return me;
  }

  // POST /api/setup answers with the new /api/me, so no second call is needed.
  set(me: Me): void {
    this.me.set(me);
  }

  clear(): void {
    this.me.set(null);
  }
}
