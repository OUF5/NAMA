import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// GET /api/me (PRD 7.5). Profile, current FY and current month are filled in from M3.
export interface Me {
  setupCompleted: boolean;
  profile: null;
  currentFyId: string | null;
  currentPeriodKey: string | null;
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

  clear(): void {
    this.me.set(null);
  }
}
