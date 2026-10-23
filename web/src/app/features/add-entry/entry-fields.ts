import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ErrorStateMatcher, provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { merge } from 'rxjs';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { amountValidator, formatMoney, parseAmount, toMinor } from '../../shared/money';
import { formatDate, fromDateString, toDateString } from '../../shared/month-label';
import { PlannedIndicator } from '../../shared/planned-indicator';
import { Category } from '../plan/plan-api';
import { BankAccount, EntryApi, Indicator } from './entry-api';

// PRD 4.7: notes are optional, at most 200 characters.
export const NOTE_MAX_LENGTH = 200;

// Errors show on fields that were left after typing in them, or after Save. Material's default also
// shows them once the form was submitted, which turned the cleared amount red right after every save
// (ENT-02 quick repeat). Tapping an empty field and leaving it shows nothing (owner's request, after
// M9); Save on an invalid form and server errors mark the fields (showAllErrors, applyServerErrors).
export class TouchedErrorStateMatcher implements ErrorStateMatcher {
  isErrorState(control: AbstractControl | null): boolean {
    return !!control && control.invalid && control.touched && control.dirty;
  }
}

// Save on an invalid form: every field shows its error.
export function showAllErrors(form: EntryForm): void {
  form.markAllAsTouched();
  Object.values(form.controls).forEach((control) => control.markAsDirty());
}

// The entry form of Add (PRD 5.4) and of the Entries edit dialog (TBL-02). Fields in the PRD 5.4 order.
export function createEntryForm() {
  return new FormGroup({
    categoryId: new FormControl('', { nonNullable: true, validators: Validators.required }),
    subCategoryId: new FormControl('', { nonNullable: true, validators: Validators.required }),
    amount: new FormControl('', { nonNullable: true, validators: amountValidator() }),
    bankAccountId: new FormControl('', { nonNullable: true, validators: Validators.required }),
    date: new FormControl<Date | null>(null, Validators.required),
    note: new FormControl('', { nonNullable: true, validators: Validators.maxLength(NOTE_MAX_LENGTH) }),
  });
}

export type EntryForm = ReturnType<typeof createEntryForm>;

// An edited entry keeps its sub-category and account even when they are archived (M6).
export interface KeptItems {
  subCategoryId: string;
  bankAccountId: string;
}

// Archived items are never offered (PRD 4.6), except the ones an edited entry already has.
// Only categories with a sub-category can take an entry.
export function entryCategoryOptions(categories: Category[], keep: KeptItems | null = null): Category[] {
  return categories
    .map((category) => ({
      ...category,
      subCategories: category.subCategories.filter(
        (sub) => sub.id === keep?.subCategoryId || (!sub.archived && !category.archived),
      ),
    }))
    .filter((category) => category.subCategories.length > 0);
}

export function entryAccountOptions(accounts: BankAccount[], keep: KeptItems | null = null): BankAccount[] {
  return accounts.filter((account) => !account.archived || account.id === keep?.bankAccountId);
}

// 422 (date outside every FY) and 400 come back as ProblemDetails with C# property names.
// Returns false when the error belongs to no field, so the caller shows a general message.
export function applyServerErrors(form: EntryForm, error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse) || (error.status !== 400 && error.status !== 422)) {
    return false;
  }
  const controls = form.controls;
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
    fields[field].markAsDirty();
  }
  return known.length > 0;
}

// The fields and the planned indicator (ENT-01). The page owns the form and its Save.
@Component({
  selector: 'app-entry-fields',
  imports: [
    ReactiveFormsModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    RouterLink,
    PlannedIndicator,
  ],
  host: { '[class.split]': 'split()' },
  providers: [
    OUTLINE_FORM_FIELDS,
    provideNativeDateAdapter(),
    { provide: ErrorStateMatcher, useClass: TouchedErrorStateMatcher },
  ],
  templateUrl: './entry-fields.html',
  styleUrl: './entry-fields.scss',
})
export class EntryFields implements OnInit {
  private readonly entryApi = inject(EntryApi);
  private readonly destroyRef = inject(DestroyRef);

  readonly form = input.required<EntryForm>();
  readonly categories = input.required<Category[]>();
  readonly accounts = input.required<BankAccount[]>();
  readonly currency = input.required<string>();
  // FY-03, R-STA-03: the picker only offers dates inside the stored FYs (YYYY-MM-DD).
  readonly firstDate = input<string | null>(null);
  readonly lastDate = input<string | null>(null);
  readonly keep = input<KeptItems | null>(null);
  // ENT-01 "after this entry". Off while editing: "spent so far" already includes the entry.
  readonly preview = input(true);
  // The Add page layout (owner's request, after M9): a larger amount, and the indicator in its own
  // panel (beside the fields on wide screens) with a placeholder until a sub-category is chosen.
  // The edit dialog keeps the compact layout.
  readonly split = input(false);
  // Owner note 22, Add only: R-BAL-03 balances today in minor units (null without an opening
  // balance) with their opening dates (the top bar's summary), and today (YYYY-MM-DD).
  readonly balances = input<Record<string, { balance: number | null; openingDate: string | null }> | null>(null);
  readonly today = input<string | null>(null);

  protected readonly noteMax = NOTE_MAX_LENGTH;
  protected readonly toMinor = toMinor;
  protected readonly indicator = signal<Indicator | null>(null);

  protected readonly minDate = computed(() => this.toDate(this.firstDate()));
  protected readonly maxDate = computed(() => this.toDate(this.lastDate()));
  protected readonly dateRange = computed(() => {
    const first = this.firstDate();
    const last = this.lastDate();
    return first && last ? `${formatDate(first)} to ${formatDate(last)}` : '';
  });

  protected readonly categoryOptions = computed(() => entryCategoryOptions(this.categories(), this.keep()));
  protected readonly accountOptions = computed(() => entryAccountOptions(this.accounts(), this.keep()));

  private readonly categoryId = signal('');
  protected readonly subCategoryOptions = computed(
    () => this.categoryOptions().find((category) => category.id === this.categoryId())?.subCategories ?? [],
  );

  private readonly amountText = signal('');
  protected readonly pending = computed(() => (this.preview() ? (parseAmount(this.amountText()) ?? 0) : 0));

  private readonly noteText = signal('');
  protected readonly noteLength = computed(() => this.noteText().length);

  private readonly accountId = signal('');
  private readonly dateText = signal<string | null>(null);

  // Owner note 22: the chosen account's balance today and after this expense, a preview only (exact
  // minor units); the saved balance comes from the API. R-BAL-03 counts an expense from its date, and
  // not before the account's opening date (that money is already in the opening balance).
  protected readonly account = computed(() => {
    const balances = this.balances();
    const account = this.accounts().find((item) => item.id === this.accountId());
    if (!balances || !account) {
      return null;
    }
    const { balance, openingDate } = balances[account.id] ?? { balance: null, openingDate: null };
    const date = this.dateText();
    const amount = parseAmount(this.amountText()) ?? 0;
    const counts = balance !== null && amount > 0 && !!date && !!openingDate && date >= openingDate;
    const today = this.today();
    return {
      name: account.name,
      now: balance === null ? null : this.money(balance),
      after: counts ? this.money(balance! - amount) : null,
      negative: counts && balance! - amount < 0,
      note:
        balance === null
          ? null
          : date && openingDate && date < openingDate
            ? `Before ${account.name}'s opening balance on ${formatDate(openingDate)}: its balance stays the same.`
            : counts && today && date! > today
              ? `Counted from ${formatDate(date!)}.`
              : null,
    };
  });

  private indicatorRequest = 0;

  ngOnInit(): void {
    const controls = this.form().controls;
    this.categoryId.set(controls.categoryId.value);
    this.amountText.set(controls.amount.value);
    this.noteText.set(controls.note.value);
    // A new category starts with no sub-category.
    controls.categoryId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => {
      this.categoryId.set(id);
      controls.subCategoryId.setValue('');
    });
    controls.amount.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((amount) => this.amountText.set(amount));
    controls.note.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((note) => this.noteText.set(note));
    this.accountId.set(controls.bankAccountId.value);
    this.dateText.set(this.dateString(controls.date.value));
    controls.bankAccountId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => this.accountId.set(id));
    controls.date.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((date) => this.dateText.set(this.dateString(date)));
    // ENT-01: as soon as a sub-category is chosen, and again when the date moves to another month.
    merge(controls.subCategoryId.valueChanges, controls.date.valueChanges)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => void this.refreshIndicator());
    void this.refreshIndicator();
  }

  // Only the latest answer is shown, so a slow reply never overwrites a newer choice.
  async refreshIndicator(): Promise<void> {
    const request = ++this.indicatorRequest;
    const subCategoryId = this.form().controls.subCategoryId.value;
    const date = this.form().controls.date;
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

  private toDate(date: string | null): Date | null {
    return date ? fromDateString(date) : null;
  }

  private dateString(date: Date | null): string | null {
    return date instanceof Date && !isNaN(date.getTime()) ? toDateString(date) : null;
  }

  private money(minor: number): string {
    return formatMoney(minor, this.currency());
  }
}
