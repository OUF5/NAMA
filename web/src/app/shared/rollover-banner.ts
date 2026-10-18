import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { firstValueFrom } from 'rxjs';
import { Me, MeService } from '../core/me';
import { FiscalYear } from '../features/plan/plan-api';
import { formatDate } from './month-label';

// FY-01, from the overview: the latest FY, and the label and first day of the FY that Start creates.
// Set during the last month of the latest FY and after it ends; null otherwise.
export interface Rollover {
  fyId: string;
  label: string;
  endDate: string;
  nextLabel: string;
  nextStartDate: string;
}

// POST /api/fiscal-years/next (FY-02). The new /api/me comes with it.
interface StartNextResponse {
  fiscalYear: FiscalYear;
  copiedPlans: number;
  me: Me;
}

// FY-01 banner on Months (and Dashboard in M9). Start is inline, with no dialog: starting an FY is
// not destructive (UX-04). Its button is the page's one filled button.
@Component({
  selector: 'app-rollover-banner',
  imports: [MatButtonModule, MatCheckboxModule, MatIconModule],
  template: `
    <section class="banner" aria-labelledby="rollover-title">
      <mat-icon aria-hidden="true">info</mat-icon>
      <div class="body">
        <h2 id="rollover-title">{{ message() }}</h2>
        <p class="caption">
          {{ rollover().nextLabel }} starts on {{ formatDate(rollover().nextStartDate) }}. Entries after
          {{ formatDate(rollover().endDate) }} need it.
        </p>
        <mat-checkbox [checked]="copyPlans()" (change)="copyPlans.set($event.checked)">
          Copy planned amounts from the same months a year earlier
        </mat-checkbox>
        @if (error()) {
          <p class="error" role="alert">{{ error() }}</p>
        }
      </div>
      <button mat-flat-button type="button" [disabled]="busy()" (click)="start()">
        Start {{ rollover().nextLabel }}
      </button>
    </section>
  `,
  styles: `
    .banner {
      display: flex;
      align-items: flex-start;
      gap: 12px 16px;
      margin-bottom: 16px;
      padding: 20px;
      background: var(--surface);
      border: 2px solid var(--primary-text);
      border-radius: 12px;
    }

    .body {
      flex: 1;
      min-width: 0;
    }

    h2 {
      margin: 0 0 4px;
    }

    p {
      margin: 0;
    }

    mat-checkbox {
      margin: 8px 0 0 -8px;
    }

    .error {
      color: var(--danger);
    }

    @media (max-width: 767.98px) {
      .banner {
        flex-wrap: wrap;
        padding: 16px;
      }

      button {
        width: 100%;
        height: 44px;
      }
    }
  `,
})
export class RolloverBanner {
  private readonly http = inject(HttpClient);
  private readonly meService = inject(MeService);

  readonly rollover = input.required<Rollover>();
  readonly started = output<StartNextResponse>();

  protected readonly formatDate = formatDate;
  // FY-02: on by default.
  protected readonly copyPlans = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  // "FY 2026 ends on 26 Jan 2027." while it runs, "ended on" after.
  protected readonly message = computed(() => {
    const rollover = this.rollover();
    const today = this.meService.me()?.today;
    const verb = today && today > rollover.endDate ? 'ended' : 'ends';
    return `${rollover.label} ${verb} on ${formatDate(rollover.endDate)}.`;
  });

  protected async start(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const response = await firstValueFrom(
        this.http.post<StartNextResponse>('/api/fiscal-years/next', { copyPlans: this.copyPlans() }),
      );
      // The top bar, the current FY and the Add date range (FY-03) follow the new /api/me.
      this.meService.set(response.me);
      this.started.emit(response);
    } catch {
      this.error.set(`${this.rollover().nextLabel} could not be started. Please reload the page and try again.`);
    } finally {
      this.busy.set(false);
    }
  }
}
