import { Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';

// One month of the picker: "Sep 2026", with a hint such as "In progress".
export interface MonthOption {
  key: string;
  label: string;
  hint?: string;
}

// Owner note 9: one month at a time, on Plan's Planning tab and on Tracker. Previous and next step
// through the FY's months; the select jumps to any of them. The page that uses it keeps the choice.
@Component({
  selector: 'app-month-picker',
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatSelectModule],
  template: `
    <div class="picker" role="group" aria-label="Month">
      <button
        mat-icon-button
        type="button"
        aria-label="Previous month"
        [disabled]="index() <= 0"
        (click)="step(-1)"
      >
        <mat-icon>chevron_left</mat-icon>
      </button>
      <mat-form-field subscriptSizing="dynamic">
        <mat-label>Month</mat-label>
        <mat-select [value]="selected()" (selectionChange)="selectedChange.emit($event.value)">
          @for (month of months(); track month.key) {
            <mat-option [value]="month.key">
              {{ month.label }}
              @if (month.hint) {
                <span class="hint"> · {{ month.hint }}</span>
              }
            </mat-option>
          }
        </mat-select>
      </mat-form-field>
      <button
        mat-icon-button
        type="button"
        aria-label="Next month"
        [disabled]="index() < 0 || index() >= months().length - 1"
        (click)="step(1)"
      >
        <mat-icon>chevron_right</mat-icon>
      </button>
    </div>
  `,
  styles: `
    .picker {
      display: flex;
      align-items: center;
      gap: 4px;
    }

    mat-form-field {
      width: 220px;
    }

    .hint {
      color: var(--text-muted);
    }

    @media (max-width: 767.98px) {
      .picker {
        width: 100%;
      }

      mat-form-field {
        flex: 1;
        width: auto;
        min-width: 0;
      }
    }
  `,
})
export class MonthPicker {
  readonly months = input.required<MonthOption[]>();
  readonly selected = input.required<string | null>();
  readonly selectedChange = output<string>();

  protected readonly index = computed(() =>
    this.months().findIndex((month) => month.key === this.selected()),
  );

  protected step(by: number): void {
    const next = this.months()[this.index() + by];
    if (next) {
      this.selectedChange.emit(next.key);
    }
  }
}
