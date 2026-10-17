import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { ErrorStateMatcher, provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { merge } from 'rxjs';
import { MeService } from '../../core/me';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { amountValidator, formatMoney, parseAmount, toMinor } from '../../shared/money';
import { formatDate, fromDateString, toDateString } from '../../shared/month-label';
import { PlannedIndicator } from '../../shared/planned-indicator';
import { Category, PlanApi } from '../plan/plan-api';
import { BankAccount, EntryApi, Indicator } from './entry-api';

// PRD 4.7: notes are optional, at most 200 characters.
const NOTE_MAX_LENGTH = 200;

// Errors show only on touched fields. Material's default also shows them once the form was
// submitted, which turned the cleared amount red right after every save (ENT-02 quick repeat).
// Save on an invalid form marks every field touched, so its errors still show.
class TouchedErrorStateMatcher implements ErrorStateMatcher {
  isErrorState(control: AbstractControl | null): boolean {
    return !!control && control.invalid && control.touched;
  }
}

// PRD 5.4 Add entry (ENT-01 to ENT-03). Page load is 2 calls (NFR-01): categories and bank accounts.
// /api/me is already loaded by setupGuard and brings the last used account, today and the FY range.
// The API assigns the month and FY from the date (R-CAL-09).
@Component({
  selector: 'app-add-entry',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    PlannedIndicator,
  ],
  providers: [
    OUTLINE_FORM_FIELDS,
    provideNativeDateAdapter(),
    { provide: ErrorStateMatcher, useClass: TouchedErrorStateMatcher },
  ],
  templateUrl: './add-entry.html',
  styleUrl: './add-entry.scss',
})
export class AddEntry {
  private readonly planApi = inject(PlanApi);
  private readonly entryApi = inject(EntryApi);
  private readonly meService = inject(MeService);

  protected readonly noteMax = NOTE_MAX_LENGTH;
  protected readonly toMinor = toMinor;

  protected readonly loadError = signal('');
  protected readonly loaded = signal(false);
  protected readonly categories = signal<Category[]>([]);
  protected readonly accounts = signal<BankAccount[]>([]);
  protected readonly indicator = signal<Indicator | null>(null);
  protected readonly busy = signal(false);
  protected readonly saveError = signal('');
  // ENT-02 "Saved": shown in the Save bar until the next amount is typed.
  protected readonly saved = signal('');

  // Fields in the PRD 5.4 order.
  protected readonly form = new FormGroup({
    categoryId: new FormControl('', { nonNullable: true, validators: Validators.required }),
    subCategoryId: new FormControl('', { nonNullable: true, validators: Validators.required }),
    amount: new FormControl('', { nonNullable: true, validators: amountValidator() }),
    bankAccountId: new FormControl('', { nonNullable: true, validators: Validators.required }),
    date: new FormControl<Date | null>(null, Validators.required),
    note: new FormControl('', { nonNullable: true, validators: Validators.maxLength(NOTE_MAX_LENGTH) }),
  });

  protected readonly currency = computed(() => this.meService.me()?.profile?.currency ?? 'SAR');

  // FY-03, R-STA-03: the picker only offers dates inside the stored FYs.
  protected readonly minDate = computed(() => this.dateOrNull(this.meService.me()?.firstDate));
  protected readonly maxDate = computed(() => this.dateOrNull(this.meService.me()?.lastDate));
  protected readonly dateRange = computed(() => {
    const me = this.meService.me();
    return me?.firstDate && me.lastDate ? `${formatDate(me.firstDate)} to ${formatDate(me.lastDate)}` : '';
  });

  // Archive comes with Settings (M8); archived items are never offered. Only categories with a
  // sub-category can take an entry.
  protected readonly categoryOptions = computed(() =>
    this.categories()
      .filter((category) => !category.archived)
      .map((category) => ({ ...category, subCategories: category.subCategories.filter((sub) => !sub.archived) }))
      .filter((category) => category.subCategories.length > 0),
  );
  protected readonly accountOptions = computed(() => this.accounts().filter((account) => !account.archived));

  private readonly categoryId = toSignal(this.form.controls.categoryId.valueChanges, { initialValue: '' });
  protected readonly subCategoryOptions = computed(
    () => this.categoryOptions().find((category) => category.id === this.categoryId())?.subCategories ?? [],
  );

  // ENT-01 "after this entry": the typed amount in minor units, 0 while it is not valid.
  private readonly amountText = toSignal(this.form.controls.amount.valueChanges, { initialValue: '' });
  protected readonly pending = computed(() => parseAmount(this.amountText()) ?? 0);

  private readonly noteText = toSignal(this.form.controls.note.valueChanges, { initialValue: '' });
  protected readonly noteLength = computed(() => this.noteText().length);

  private indicatorRequest = 0;

  constructor() {
    const controls = this.form.controls;
    // A new category starts with no sub-category.
    controls.categoryId.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => controls.subCategoryId.setValue(''));
    // ENT-01: as soon as a sub-category is chosen, and again when the date moves to another month.
    merge(controls.subCategoryId.valueChanges, controls.date.valueChanges)
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.refreshIndicator());
    // The "Saved" line stays until the next entry starts.
    controls.amount.valueChanges.pipe(takeUntilDestroyed()).subscribe((amount) => {
      if (amount) {
        this.saved.set('');
      }
    });
    void this.load();
  }

  // ENT-02: amount and note clear; category, sub-category, account and date stay for quick repeats.
  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.busy.set(true);
    this.saveError.set('');
    this.saved.set('');
    try {
      const entry = await this.entryApi.addEntry({
        subCategoryId: value.subCategoryId,
        bankAccountId: value.bankAccountId,
        date: toDateString(value.date!),
        amount: parseAmount(value.amount)! / 100,
        note: value.note.trim(),
      });
      this.meService.setLastBankAccount(entry.bankAccountId);
      this.form.controls.amount.reset('');
      this.form.controls.note.reset('');
      const sub = this.subCategoryOptions().find((option) => option.id === entry.subCategoryId);
      this.saved.set(`Saved ${formatMoney(toMinor(entry.amount), this.currency())} to ${sub?.name ?? 'the sub-category'}`);
      void this.refreshIndicator();
    } catch (error: unknown) {
      this.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const me = await this.meService.load();
      const [categories, accounts] = await Promise.all([this.planApi.categories(), this.entryApi.bankAccounts()]);
      this.categories.set(categories);
      this.accounts.set(accounts);

      // Decision 5: the last used account; before the first entry, the first one by name.
      const active = this.accountOptions();
      const last = active.find((account) => account.id === me.profile?.lastBankAccountId);
      this.form.controls.bankAccountId.setValue((last ?? active[0])?.id ?? '');

      // Today, when it is inside a stored FY. After the last FY ends (rollover is M8) a date is picked.
      if (me.today && me.firstDate && me.lastDate && me.firstDate <= me.today && me.today <= me.lastDate) {
        this.form.controls.date.setValue(fromDateString(me.today));
      }
      this.loaded.set(true);
    } catch {
      this.loadError.set('The form could not be loaded. Please reload the page.');
    }
  }

  // Only the latest answer is shown, so a slow reply never overwrites a newer choice.
  private async refreshIndicator(): Promise<void> {
    const request = ++this.indicatorRequest;
    const subCategoryId = this.form.controls.subCategoryId.value;
    const date = this.form.controls.date;
    if (!subCategoryId || !date.value || date.invalid) {
      this.indicator.set(null);
      return;
    }
    try {
      const indicator = await this.entryApi.indicator(subCategoryId, toDateString(date.value));
      if (request === this.indicatorRequest) {
        this.indicator.set(indicator);
      }
    } catch {
      if (request === this.indicatorRequest) {
        this.indicator.set(null);
      }
    }
  }

  // 422 (date outside every FY) and 400 come back as ProblemDetails with C# property names.
  private showError(error: unknown): void {
    if (error instanceof HttpErrorResponse && (error.status === 400 || error.status === 422)) {
      const controls = this.form.controls;
      const fields: Record<string, AbstractControl> = {
        SubCategoryId: controls.subCategoryId,
        Amount: controls.amount,
        BankAccountId: controls.bankAccountId,
        Date: controls.date,
        Note: controls.note,
      };
      const errors = (error.error?.errors ?? {}) as Record<string, string[]>;
      const known = Object.entries(errors).filter(([field]) => fields[field]);
      for (const [field, messages] of known) {
        fields[field].setErrors({ server: messages[0] });
        fields[field].markAsTouched();
      }
      if (known.length > 0) {
        return;
      }
    }
    this.saveError.set('The entry could not be saved. Please try again.');
  }

  private dateOrNull(date: string | null | undefined): Date | null {
    return date ? fromDateString(date) : null;
  }
}
