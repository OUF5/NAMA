import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { MONTH_NAMES, formatDate } from '../../shared/month-label';
import { Calendar, SettingsApi } from './settings-api';

// STG-02, R-CAL-07: the FY start month and the payday. They are saved now and apply from the next
// FY; the current FY keeps its dates. Closes with the new calendar.
@Component({
  selector: 'app-calendar-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  providers: [OUTLINE_FORM_FIELDS],
  templateUrl: './calendar-dialog.html',
  styleUrl: './dialog.scss',
})
export class CalendarDialog {
  private readonly api = inject(SettingsApi);
  private readonly dialogRef = inject<MatDialogRef<CalendarDialog, Calendar>>(MatDialogRef);
  protected readonly calendar = inject<Calendar>(MAT_DIALOG_DATA);

  protected readonly monthNames = MONTH_NAMES;
  protected readonly formatDate = formatDate;

  // PRD 4.7: FY start month 1 to 12, payday 1 to 31.
  protected readonly form = new FormGroup({
    fiscalStartMonth: new FormControl(this.calendar.fiscalStartMonth, { nonNullable: true, validators: Validators.required }),
    paydayDay: new FormControl<number | null>(this.calendar.paydayDay, [
      Validators.required,
      Validators.min(1),
      Validators.max(31),
      Validators.pattern(/^\d+$/),
    ]),
  });

  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.busy.set(true);
    this.error.set('');
    try {
      this.dialogRef.close(await this.api.updateCalendar(value.fiscalStartMonth, Number(value.paydayDay)));
    } catch {
      this.error.set('The settings could not be saved. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
