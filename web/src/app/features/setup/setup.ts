import { BreakpointObserver } from '@angular/cdk/layout';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormArray,
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatStepperModule } from '@angular/material/stepper';
import { Router } from '@angular/router';
import { catchError, debounceTime, firstValueFrom, map, merge, of, startWith, switchMap } from 'rxjs';
import { AuthService } from '../../core/auth';
import { Me, MeService } from '../../core/me';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { incomeMonths, untilNotBeforeFrom } from '../../shared/income-months';
import { CURRENCIES, amountValidator, formatMoney, parseAmount } from '../../shared/money';
import { MONTH_NAMES, PeriodInfo, formatDate, formatDay, periodWithDates } from '../../shared/month-label';
import { NAME_MAX_LENGTH, isValidName, nameValidator, notInListValidator, sameName } from '../../shared/names';
import { SetupDraft, clearDraft, loadDraft, saveDraft } from './setup-draft';

// GET /api/setup/preview: step 2's live preview, computed by FiscalCalendar in the API. The FY's
// months give step 3's income month pickers.
interface Preview {
  fiscalYear: { id: string; label: string; startDate: string; endDate: string; periods: PeriodInfo[] };
  currentPeriod: PeriodInfo;
}

type IncomeType = 'once' | 'monthly';
type IncomeItemValue = SetupDraft['additional'][number];

// PRD 5.2 step 5: optional suggestions you can tap.
const SUGGESTED_CATEGORIES = [
  'Housing', 'Food', 'Transport', 'Bills & Utilities', 'Health',
  'Family', 'Education', 'Entertainment', 'Savings', 'Other',
];

function atLeastOne(control: AbstractControl): ValidationErrors | null {
  return (control.value as unknown[]).length > 0 ? null : { required: true };
}

// PRD 5.2: the 6-step setup wizard. Nothing is saved until Finish (SET-02).
@Component({
  selector: 'app-setup',
  imports: [
    ReactiveFormsModule,
    MatStepperModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatChipsModule,
    MatIconModule,
  ],
  providers: [OUTLINE_FORM_FIELDS],
  templateUrl: './setup.html',
  styleUrl: './setup.scss',
})
export class Setup {
  private readonly auth = inject(AuthService);
  private readonly meService = inject(MeService);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly uid = this.auth.user()?.uid ?? '';

  protected readonly monthNames = MONTH_NAMES;
  protected readonly currencies = CURRENCIES;
  protected readonly nameMax = NAME_MAX_LENGTH;
  protected readonly formatDate = formatDate;
  protected readonly formatDay = formatDay;
  protected readonly periodWithDates = periodWithDates;

  // SET-04: the browser's time zone, saved with the profile.
  protected readonly timezone = Intl.DateTimeFormat().resolvedOptions().timeZone;

  // Phones (under 768 px) get a vertical stepper, because 6 steps do not fit side by side.
  protected readonly vertical = toSignal(
    inject(BreakpointObserver).observe('(max-width: 767.98px)').pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  // Step 1: Project.
  protected readonly project = this.fb.group({
    projectName: ['', nameValidator()],
    displayName: ['', nameValidator()],
    currency: ['SAR', Validators.required],
  });

  // Step 2: Fiscal year. No defaults, so both are chosen on purpose (SET-01).
  protected readonly calendar = new FormGroup({
    fiscalStartMonth: new FormControl<number | null>(null, Validators.required),
    paydayDay: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(1),
      Validators.max(31),
      Validators.pattern(/^\d+$/),
    ]),
  });

  // Step 3: Income. A salary of 0 is allowed (SET-01).
  protected readonly income = this.fb.group({
    salary: ['', amountValidator(true)],
    additional: this.fb.array<FormGroup>([]),
  });

  // Steps 4 and 5: at least one bank account and one category.
  protected readonly bankAccounts = this.fb.array<FormControl<string>>([], atLeastOne);
  protected readonly categories = this.fb.array<FormControl<string>>([], atLeastOne);
  protected readonly newAccount = this.fb.control('', notInListValidator(() => this.bankAccounts.getRawValue()));
  protected readonly newCategory = this.fb.control('', notInListValidator(() => this.categories.getRawValue()));
  protected readonly accountsTried = signal(false);
  protected readonly categoriesTried = signal(false);

  protected readonly suggestions = SUGGESTED_CATEGORIES;

  protected readonly busy = signal(false);
  protected readonly error = signal('');

  private readonly incomeValue = toSignal(this.income.valueChanges, { initialValue: this.income.getRawValue() });
  private readonly projectValue = toSignal(this.project.valueChanges, { initialValue: this.project.getRawValue() });
  private readonly categoriesValue = toSignal(this.categories.valueChanges, { initialValue: [] as string[] });

  protected readonly currency = computed(() => this.projectValue().currency ?? 'SAR');

  // Step 2 live preview, for example "FY 2026: 27 Jan 2026 to 26 Jan 2027".
  protected readonly preview = toSignal(
    this.calendar.valueChanges.pipe(
      startWith(this.calendar.value),
      debounceTime(250),
      switchMap(() => {
        if (this.calendar.invalid) {
          return of(null);
        }
        const params = new HttpParams()
          .set('fiscalStartMonth', String(this.calendar.value.fiscalStartMonth))
          .set('paydayDay', String(this.calendar.value.paydayDay))
          .set('timezone', this.timezone);
        return this.http.get<Preview>('/api/setup/preview', { params }).pipe(catchError(() => of(null)));
      }),
    ),
    { initialValue: null },
  );

  // Step 3's income month pickers: the months of the first FY (R-INC-03).
  protected readonly months = computed(() => this.preview()?.fiscalYear.periods ?? []);

  private readonly incomeItems = computed(() => (this.incomeValue().additional ?? []) as IncomeItemValue[]);

  // R-INC-01 for the current month, as Settings shows it (STG-03): the salary plus every item
  // active in that month. A preview only; the saved totals come from IncomeCalculator.
  protected readonly currentIncomeMinor = computed(() => {
    const key = this.preview()?.currentPeriod.key;
    return this.incomeItems()
      .filter((item) => key && this.isActiveIn(item, key))
      .reduce((sum, item) => sum + (parseAmount(item.amount) ?? 0), parseAmount(this.incomeValue().salary, true) ?? 0);
  });

  // Step 6: "Bonus · SAR 500.25 · Mar 2026".
  protected readonly incomeLines = computed(() =>
    this.incomeItems().map((item) => {
      const amount = parseAmount(item.amount);
      const months = item.from ? incomeMonths({ fromPeriodKey: item.from, toPeriodKey: item.type === 'once' ? item.from : item.to || null }) : '';
      return [item.label.trim(), amount === null ? '' : this.money(amount), months].filter(Boolean).join(' · ');
    }),
  );

  protected readonly unusedSuggestions = computed(() =>
    this.suggestions.filter((name) => !this.categoriesValue().some((existing) => sameName(existing, name))),
  );

  constructor() {
    const draft = loadDraft(this.uid);
    if (draft) {
      this.restore(draft);
    } else {
      this.project.controls.displayName.setValue((this.auth.user()?.displayName ?? '').slice(0, NAME_MAX_LENGTH));
    }

    // A new FY start month or payday gives other months: a month that is no longer offered goes
    // back to its default, and an until month to "No end". Items added before the preview
    // arrived, or restored from a draft, get their default here too.
    effect(() => {
      const preview = this.preview();
      if (!preview) {
        return;
      }
      const keys = new Set(preview.fiscalYear.periods.map((period) => period.key));
      for (const item of this.additional.controls) {
        const { type, from, to } = item.getRawValue() as IncomeItemValue;
        if (!keys.has(from)) {
          item.controls['from'].setValue(this.defaultFrom(type));
        }
        if (to && !keys.has(to)) {
          item.controls['to'].setValue('');
        }
      }
    });

    // SET-03: keep the typed values in browser storage.
    merge(this.project.valueChanges, this.calendar.valueChanges, this.income.valueChanges,
      this.bankAccounts.valueChanges, this.categories.valueChanges)
      .pipe(debounceTime(300), takeUntilDestroyed())
      .subscribe(() => saveDraft(this.uid, this.draft()));
  }

  protected get additional() {
    return this.income.controls.additional;
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.currency());
  }

  protected monthName(month: number | null | undefined): string {
    return month ? MONTH_NAMES[month - 1] : '';
  }

  protected addIncomeItem(): void {
    this.additional.push(this.incomeItem('', '', 'monthly', this.defaultFrom('monthly'), ''));
  }

  protected itemType(index: number): IncomeType | undefined {
    return this.incomeItems()[index]?.type;
  }

  protected removeIncomeItem(index: number): void {
    this.additional.removeAt(index);
  }

  protected addAccount(): void {
    if (this.addName(this.newAccount, this.bankAccounts)) {
      this.accountsTried.set(false);
    }
  }

  protected addCategory(): void {
    if (this.addName(this.newCategory, this.categories)) {
      this.categoriesTried.set(false);
    }
  }

  protected addSuggestion(name: string): void {
    this.categories.push(this.fb.control(name));
  }

  protected removeAt(list: FormArray<FormControl<string>>, index: number): void {
    list.removeAt(index);
  }

  protected async signOut(): Promise<void> {
    await this.auth.signOut();
  }

  // SET-01, SET-02: one call; the API saves everything in one batch.
  protected async finish(): Promise<void> {
    if (this.project.invalid || this.calendar.invalid || this.income.invalid || this.bankAccounts.invalid || this.categories.invalid) {
      this.error.set('Some steps are not complete yet.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      const me = await firstValueFrom(this.http.post<Me>('/api/setup', this.request()));
      clearDraft(this.uid);
      this.meService.set(me);
      await this.router.navigate(['/plan']);
    } catch (error: unknown) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        // Already set up (for example in another tab): reload /api/me and go to the Dashboard.
        clearDraft(this.uid);
        this.meService.clear();
        await this.router.navigate(['/']);
        return;
      }
      this.error.set(
        error instanceof HttpErrorResponse && error.status === 400
          ? 'Some values were not accepted. Please check the steps.'
          : 'Setup could not be saved. Please try again.',
      );
    } finally {
      this.busy.set(false);
    }
  }

  private request() {
    const project = this.project.getRawValue();
    const calendar = this.calendar.getRawValue();
    const income = this.income.getRawValue();
    return {
      projectName: project.projectName.trim(),
      displayName: project.displayName.trim(),
      currency: project.currency,
      timezone: this.timezone,
      fiscalStartMonth: calendar.fiscalStartMonth,
      paydayDay: Number(calendar.paydayDay),
      salary: parseAmount(income.salary, true)! / 100,
      additionalIncome: (income.additional as SetupDraft['additional']).map((item) => ({
        label: item.label.trim(),
        amount: parseAmount(item.amount)! / 100,
        type: item.type,
        fromPeriodKey: item.from,
        toPeriodKey: item.type === 'monthly' && item.to ? item.to : null,
      })),
      bankAccounts: this.bankAccounts.getRawValue(),
      categories: this.categories.getRawValue(),
    };
  }

  // Adds the trimmed name when it is valid and new (PRD 4.7). Returns whether it was added.
  private addName(input: FormControl<string>, list: FormArray<FormControl<string>>): boolean {
    const name = input.value.trim();
    if (!isValidName(name)) {
      input.setErrors({ name: true });
    }
    if (input.invalid) {
      input.markAsTouched();
      return false;
    }
    list.push(this.fb.control(name));
    input.reset('');
    return true;
  }

  // R-INC-03: Once is one month; Monthly runs from a month, optionally until a month.
  private incomeItem(label: string, amount: string, type: IncomeType, from: string, to: string): FormGroup {
    const group = this.fb.group(
      {
        label: [label, nameValidator()],
        amount: [amount, amountValidator()],
        type: [type, Validators.required],
        from: [from, Validators.required],
        to: [to],
      },
      { validators: untilNotBeforeFrom },
    );
    // A month still at the other type's default follows the type.
    group.controls.type.valueChanges.subscribe((next) => {
      if (group.controls.from.value === this.defaultFrom(next === 'once' ? 'monthly' : 'once')) {
        group.controls.from.setValue(this.defaultFrom(next));
      }
    });
    return group;
  }

  // Once: the current month. Monthly: the FY's first month, like the salary (owner's choice, M3).
  // Empty until the preview is there.
  private defaultFrom(type: IncomeType): string {
    const preview = this.preview();
    if (!preview) {
      return '';
    }
    return type === 'once' ? preview.currentPeriod.key : preview.fiscalYear.periods[0].key;
  }

  private isActiveIn(item: IncomeItemValue, key: string): boolean {
    if (item.type === 'once') {
      return item.from === key;
    }
    // Month keys are YYYY-MM, so string order is calendar order.
    return !!item.from && item.from <= key && (!item.to || key <= item.to);
  }

  private draft(): SetupDraft {
    const project = this.project.getRawValue();
    const calendar = this.calendar.getRawValue();
    const income = this.income.getRawValue();
    return {
      ...project,
      fiscalStartMonth: calendar.fiscalStartMonth,
      paydayDay: calendar.paydayDay,
      salary: income.salary,
      additional: income.additional as SetupDraft['additional'],
      bankAccounts: this.bankAccounts.getRawValue(),
      categories: this.categories.getRawValue(),
    };
  }

  private restore(draft: Partial<SetupDraft>): void {
    this.project.patchValue({
      projectName: draft.projectName ?? '',
      displayName: draft.displayName ?? '',
      currency: CURRENCIES.find((code) => code === draft.currency) ?? 'SAR',
    });
    this.calendar.patchValue({
      fiscalStartMonth: draft.fiscalStartMonth ?? null,
      paydayDay: draft.paydayDay ?? null,
    });
    this.income.controls.salary.setValue(draft.salary ?? '');
    for (const item of draft.additional ?? []) {
      this.additional.push(
        this.incomeItem(item.label ?? '', item.amount ?? '', item.type === 'once' ? 'once' : 'monthly', item.from ?? '', item.to ?? ''),
      );
    }
    for (const name of draft.bankAccounts ?? []) {
      this.bankAccounts.push(this.fb.control(name));
    }
    for (const name of draft.categories ?? []) {
      this.categories.push(this.fb.control(name));
    }
  }
}
