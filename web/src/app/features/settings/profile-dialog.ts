import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Me, Profile } from '../../core/me';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { CURRENCIES } from '../../shared/money';
import { NAME_MAX_LENGTH, nameValidator } from '../../shared/names';
import { SettingsApi } from './settings-api';
import { timeZones } from './settings-lines';

// STG-01: name, project name, currency (formatting only, R-MON-03) and time zone (R-CAL-10).
// Closes with the new /api/me.
@Component({
  selector: 'app-profile-dialog',
  imports: [ReactiveFormsModule, MatAutocompleteModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  providers: [OUTLINE_FORM_FIELDS],
  templateUrl: './profile-dialog.html',
  styleUrl: './dialog.scss',
})
export class ProfileDialog {
  private readonly api = inject(SettingsApi);
  private readonly dialogRef = inject<MatDialogRef<ProfileDialog, Me>>(MatDialogRef);
  private readonly profile = inject<Profile>(MAT_DIALOG_DATA);

  protected readonly currencies = CURRENCIES;
  protected readonly nameMax = NAME_MAX_LENGTH;
  private readonly zones = timeZones(this.profile.timezone);

  protected readonly form = inject(NonNullableFormBuilder).group({
    displayName: [this.profile.displayName, nameValidator()],
    projectName: [this.profile.projectName, nameValidator()],
    currency: [this.profile.currency, Validators.required],
    timezone: [this.profile.timezone, (control: AbstractControl): ValidationErrors | null =>
      this.zones.includes(control.value) ? null : { timezone: true }],
  });

  private readonly typedZone = toSignal(this.form.controls.timezone.valueChanges, { initialValue: this.profile.timezone });

  // The whole list while the saved zone is shown; otherwise the zones that contain the typed text.
  protected readonly zoneOptions = computed(() => {
    const typed = this.typedZone().trim().toLowerCase();
    return typed === this.profile.timezone.toLowerCase() ? this.zones : this.zones.filter((zone) => zone.toLowerCase().includes(typed));
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
      this.dialogRef.close(
        await this.api.updateProfile({
          displayName: value.displayName.trim(),
          projectName: value.projectName.trim(),
          currency: value.currency,
          timezone: value.timezone,
        }),
      );
    } catch (error: unknown) {
      this.error.set(
        error instanceof HttpErrorResponse && error.status === 400
          ? 'Some values were not accepted. Please check them.'
          : 'The profile could not be saved. Please try again.',
      );
    } finally {
      this.busy.set(false);
    }
  }
}
