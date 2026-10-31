import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormArray, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { debounceTime, merge } from 'rxjs';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { amountValidator, formatMoney, minorToInput, parseAmount, toMinor } from '../../shared/money';
import { PeriodInfo, formatDate, formatDay, fromDateString, toDateString } from '../../shared/month-label';
import { NAME_MAX_LENGTH, nameValidator, notInListValidator } from '../../shared/names';
import { NewProject, Project, ProjectMonths, ProjectsApi, ProjectsResponse, TARGET_LABELS, TargetLevel } from './projects-api';

export interface ProjectDialogData {
  // null adds a new project.
  project: Project | null;
  data: ProjectsResponse;
  currency: string;
}

type Mode = 'edit' | 'months' | 'done' | 'delete';

const LEVELS: TargetLevel[] = ['best', 'likely', 'worst'];

// The months and the recommended amount are asked again once typing stops.
export const MONTHS_DELAY_MS = 300;

// An empty field is fine; anything typed is an amount (R-MON-02).
function optionalAmount(allowZero = false): ValidatorFn {
  const amount = amountValidator(allowZero);
  return (control: AbstractControl): ValidationErrors | null => ((control.value ?? '').trim() ? amount(control) : null);
}

function amountInput(amount: number | null | undefined): string {
  return amount === null || amount === undefined ? '' : minorToInput(toMinor(amount));
}

// A month with nothing planned shows empty, as in Plan (PLN-01).
function monthInput(amount: number): string {
  return amount ? minorToInput(toMinor(amount)) : '';
}

function monthControl(value: string): FormControl<string> {
  return new FormControl(value, { nonNullable: true, validators: optionalAmount(true) });
}

// Minor units of a month input: empty is 0, null when the text is not a valid amount.
function minorOf(text: string): number | null {
  return text.trim() === '' ? 0 : parseAmount(text, true);
}

// R-PRJ-02: at least one target, and the active one is set.
function targetsValidator(group: AbstractControl): ValidationErrors | null {
  const value = group.value as Record<TargetLevel | 'activeTarget', string>;
  if (LEVELS.every((level) => !(value[level] ?? '').trim())) {
    return { noTarget: true };
  }
  return (value[value.activeTarget as TargetLevel] ?? '').trim() ? null : { activeNotSet: true };
}

function milestoneGroup(milestone?: { id: string; label: string; date: string; amount: number }) {
  return new FormGroup({
    id: new FormControl<string | null>(milestone?.id ?? null),
    label: new FormControl(milestone?.label ?? '', { nonNullable: true, validators: nameValidator() }),
    date: new FormControl<Date | null>(milestone ? fromDateString(milestone.date) : null, Validators.required),
    amount: new FormControl(amountInput(milestone?.amount), { nonNullable: true, validators: amountValidator() }),
  });
}

// PRJ-04 (owner note 30, owner's flow of 31 Oct 2026): Name, Deadline, Bank account and Targets; once
// the deadline, the account and Likely are known, the recommended amount a month fills the monthly
// amount (R-PRJ-12) until you change it. "Plan by month" shows every month to the deadline, as Plan's
// sub-category dialog, so one month can differ. Edit also has Pause or Resume, Mark done or Reopen,
// and Delete (R-PRJ-01). Plan by month, Mark done and Delete replace the form inside this dialog
// (UX-04: dialogs are never stacked). Closes with the new GET /api/projects.
@Component({
  selector: 'app-project-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
  ],
  providers: [OUTLINE_FORM_FIELDS, provideNativeDateAdapter()],
  templateUrl: './project-dialog.html',
  styleUrl: './project-dialog.scss',
})
export class ProjectDialog {
  private readonly api = inject(ProjectsApi);
  private readonly dialogRef = inject<MatDialogRef<ProjectDialog, ProjectsResponse>>(MatDialogRef);
  protected readonly data = inject<ProjectDialogData>(MAT_DIALOG_DATA);

  protected readonly project = this.data.project;
  protected readonly nameMax = NAME_MAX_LENGTH;
  protected readonly levels = LEVELS;
  protected readonly targetLabels = TARGET_LABELS;
  protected readonly formatDay = formatDay;
  protected readonly mode = signal<Mode>('edit');
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  private readonly taken = this.data.data.projects.filter((item) => item.id !== this.project?.id).map((item) => item.name);
  private readonly target = (level: TargetLevel) => amountInput(this.project?.targets.find((item) => item.level === level)?.amount);

  // R-PRJ-05: the months from the start month to the deadline, each with its amount.
  protected readonly periods = signal<PeriodInfo[]>(this.project?.plan.map((month) => month.period) ?? []);
  private readonly currentKey = signal(this.data.data.currentMonth?.key ?? '');
  // R-PRJ-12, in minor units.
  private readonly recommended = signal<number | null>(null);
  protected readonly amounts = new FormArray((this.project?.plan ?? []).map((month) => monthControl(monthInput(month.amount))));
  // [formGroup] makes (ngSubmit) work on Plan by month and stops the browser's own submit.
  protected readonly monthsForm = new FormGroup({ amounts: this.amounts });
  protected readonly sameAmount = new FormControl('', { nonNullable: true, validators: amountValidator(true) });
  // A new project follows the recommendation until you type an amount; an edit keeps its months.
  private edited = !!this.project;

  protected readonly form = new FormGroup(
    {
      name: new FormControl(this.project?.name ?? '', {
        nonNullable: true,
        validators: [nameValidator(), notInListValidator(() => this.taken)],
      }),
      deadline: new FormControl<Date | null>(this.project ? fromDateString(this.project.deadline) : null, Validators.required),
      bankAccountId: new FormControl(this.project?.bankAccountId ?? '', { nonNullable: true, validators: Validators.required }),
      best: new FormControl(this.target('best'), { nonNullable: true, validators: optionalAmount() }),
      likely: new FormControl(this.target('likely'), { nonNullable: true, validators: optionalAmount() }),
      worst: new FormControl(this.target('worst'), { nonNullable: true, validators: optionalAmount() }),
      activeTarget: new FormControl<TargetLevel>(this.project?.activeTarget ?? 'likely', { nonNullable: true }),
      monthly: new FormControl('', { nonNullable: true, validators: optionalAmount(true) }),
      milestones: new FormArray((this.project?.milestones ?? []).map((milestone) => milestoneGroup(milestone))),
    },
    { validators: targetsValidator },
  );

  // Mark done (R-PRJ-01): a day from the start month to today.
  protected readonly doneOn = new FormControl<Date | null>(fromDateString(this.data.data.today), Validators.required);
  protected readonly doneMin = this.project?.months[0] ? fromDateString(this.project.months[0].period.startDate) : null;
  protected readonly doneMax = fromDateString(this.data.data.today);

  private readonly value = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  private readonly amountValues = toSignal(this.amounts.valueChanges, { initialValue: this.amounts.getRawValue() });

  // R-PRJ-03: the active accounts (and the archived one it already counts); one counted by another
  // project that is not Done is shown but cannot be picked.
  protected readonly accounts = this.data.data.accounts
    .filter((account) => !account.archived || this.project?.bankAccountId === account.id)
    .map((account) => {
      const holder = account.projectId && account.projectId !== this.project?.id ? this.data.data.projects.find((item) => item.id === account.projectId) : null;
      return {
        id: account.id,
        name: account.name + (account.archived ? ' (archived)' : ''),
        balance: account.balance === null ? 'No balance yet' : formatMoney(toMinor(account.balance), this.data.currency),
        takenBy: this.project?.status === 'done' ? null : (holder?.name ?? null),
      };
    });

  // The months from this month on, which the monthly amount and the recommendation are about.
  private readonly ahead = computed(() => {
    const current = this.currentKey();
    return this.periods()
      .map((period, index) => ({ period, index }))
      .filter((item) => item.period.key >= current);
  });

  // The one amount of every month from this month on, or null when they differ (or one is not valid).
  private readonly sameAhead = computed(() => {
    const values = this.amountValues();
    const minors = this.ahead().map((item) => minorOf(values[item.index] ?? ''));
    return minors.length && minors.every((minor) => minor !== null && minor === minors[0]) ? minors[0] : null;
  });

  protected readonly varies = computed(() => this.ahead().length > 1 && this.sameAhead() === null);

  // Every month's amount added up, or null while one is not valid.
  private readonly totalMinor = computed(() => {
    const minors = this.amountValues().map((text) => minorOf(text ?? ''));
    return minors.some((minor) => minor === null) ? null : (minors as number[]).reduce((sum, minor) => sum + minor, 0);
  });

  protected readonly total = computed(() => {
    const total = this.totalMinor();
    return total === null ? '' : this.money(total);
  });

  // "11 months · SAR 79,827 in all"
  protected readonly planHint = computed(() => {
    const count = this.periods().length;
    if (!count) {
      return this.value().deadline ? 'No month is left before the deadline.' : 'Pick the deadline to plan the months.';
    }
    const months = `${count} ${count === 1 ? 'month' : 'months'}`;
    return this.total() ? `${months} · ${this.total()} in all` : months;
  });

  // R-PRJ-12: what the recommendation says, and the amount "Use" puts in when it differs.
  protected readonly recommendation = computed(() => {
    const value = this.value();
    const ahead = this.ahead();
    const recommended = this.recommended();
    if (!value.deadline) {
      return { text: 'Pick the deadline, the bank account and the Likely target to see a recommended amount.', use: null };
    }
    if (!this.periods().length) {
      return { text: '', use: null };
    }
    if (!ahead.length) {
      return { text: 'The deadline has passed: no month is left to plan.', use: null };
    }
    if (!(value.likely ?? '').trim() || this.form.controls.likely.invalid) {
      return { text: 'Set the Likely target to see a recommended amount.', use: null };
    }
    if (!value.bankAccountId) {
      return { text: 'Pick the bank account to see a recommended amount.', use: null };
    }
    if (recommended === null) {
      return { text: "Set this account's balance on Banks to see a recommended amount.", use: null };
    }
    if (recommended === 0) {
      return { text: 'The account already holds the Likely target: nothing more is needed.', use: null };
    }
    const months = ahead.length === 1 ? `in ${ahead[0].period.label}` : `for ${ahead.length} months (${ahead[0].period.label} to ${ahead.at(-1)!.period.label})`;
    return {
      text: `Recommended: ${this.money(recommended)} a month ${months} to reach Likely.`,
      use: this.sameAhead() === recommended ? null : this.money(recommended),
    };
  });

  // Plan by month: "In all by then" beside each month.
  protected readonly runningTotals = computed(() => {
    const values = this.amountValues();
    let sum = 0;
    let valid = true;
    return this.periods().map((_, index) => {
      const minor = minorOf(values[index] ?? '');
      if (minor === null) {
        valid = false;
      } else {
        sum += minor;
      }
      return valid ? this.money(sum) : '';
    });
  });

  protected readonly sameLabel = computed(() => {
    const ahead = this.ahead();
    return ahead.length === this.periods().length || !ahead.length ? 'Same amount for all months' : `Same amount from ${ahead[0].period.label} on`;
  });

  protected readonly doneOnText = this.project?.doneOn ? formatDate(this.project.doneOn) : '';

  protected readonly summary = this.project
    ? `${this.project.name} · ${formatMoney(toMinor(this.project.target), this.data.currency)} by ${formatDate(this.project.deadline)}`
    : '';

  private monthsRequest = 0;
  private loading: Promise<void> = Promise.resolve();

  constructor() {
    this.showSameAhead();
    const controls = this.form.controls;
    merge(controls.deadline.valueChanges, controls.bankAccountId.valueChanges, controls.likely.valueChanges)
      .pipe(debounceTime(MONTHS_DELAY_MS), takeUntilDestroyed())
      .subscribe(() => (this.loading = this.loadMonths()));
    if (this.project) {
      this.loading = this.loadMonths();
    }
  }

  protected get milestones() {
    return this.form.controls.milestones;
  }

  // R-PRJ-05: typing the monthly amount sets every month from this month on.
  protected monthlyTyped(): void {
    this.edited = true;
    this.applyAhead(this.form.controls.monthly);
  }

  protected useRecommended(): void {
    const recommended = this.recommended();
    if (recommended !== null) {
      this.form.controls.monthly.setValue(minorToInput(recommended));
      this.applyAhead(this.form.controls.monthly);
    }
  }

  // PLN-02 "Same amount for all months", for the months from this month on.
  protected applyToAll(): void {
    if (this.sameAmount.invalid) {
      this.sameAmount.markAsTouched();
      return;
    }
    this.edited = true;
    this.applyAhead(this.sameAmount);
  }

  // PLN-02 "Copy from previous month".
  protected copyPrevious(index: number): void {
    this.edited = true;
    this.amounts.at(index).setValue(this.amounts.at(index - 1).value);
  }

  protected monthEdited(): void {
    this.edited = true;
  }

  protected addMilestone(): void {
    this.milestones.push(milestoneGroup());
  }

  protected removeMilestone(index: number): void {
    this.milestones.removeAt(index);
  }

  protected setMode(mode: Mode): void {
    this.error.set('');
    if (mode === 'months') {
      this.sameAmount.setValue(this.form.controls.monthly.value);
    }
    if (mode === 'edit' && this.mode() === 'months') {
      this.showSameAhead();
    }
    this.mode.set(mode);
  }

  protected async save(): Promise<void> {
    await this.loading;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.setMode('edit');
      return;
    }
    if (this.amounts.invalid) {
      this.amounts.markAllAsTouched();
      this.setMode('months');
      return;
    }
    const value = this.form.getRawValue();
    const amount = (text: string) => {
      const minor = parseAmount(text);
      return minor === null ? null : minor / 100;
    };
    const project: NewProject = {
      name: value.name.trim(),
      deadline: toDateString(value.deadline!),
      best: amount(value.best),
      likely: amount(value.likely),
      worst: amount(value.worst),
      activeTarget: value.activeTarget,
      bankAccountId: value.bankAccountId,
      monthly: this.periods()
        .map((period, index) => ({ periodKey: period.key, amount: (minorOf(this.amounts.at(index).value) ?? 0) / 100 }))
        .filter((month) => month.amount > 0),
      milestones: value.milestones.map((milestone) => ({
        id: milestone.id,
        label: milestone.label.trim(),
        date: toDateString(milestone.date!),
        amount: parseAmount(milestone.amount)! / 100,
      })),
    };
    await this.run(
      () => (this.project ? this.api.update(this.project.id, project) : this.api.add(project)),
      'The project could not be saved. Please try again.',
    );
  }

  protected pause(): Promise<void> {
    return this.run(() => this.api.setStatus(this.project!.id, { status: 'paused' }), 'The project could not be paused. Please try again.');
  }

  protected resume(): Promise<void> {
    return this.run(() => this.api.setStatus(this.project!.id, { status: 'active' }), 'The project could not be resumed. Please try again.');
  }

  protected reopen(): Promise<void> {
    return this.run(() => this.api.setStatus(this.project!.id, { status: 'active' }), 'The project could not be reopened. Please try again.');
  }

  protected async markDone(): Promise<void> {
    if (this.doneOn.invalid) {
      this.doneOn.markAsTouched();
      return;
    }
    await this.run(
      () => this.api.setStatus(this.project!.id, { status: 'done', doneOn: toDateString(this.doneOn.value!) }),
      'The project could not be marked done. Please try again.',
    );
  }

  protected remove(): Promise<void> {
    return this.run(() => this.api.remove(this.project!.id), 'The project could not be deleted. Please try again.');
  }

  // The months from the start month to the deadline and the recommendation (GET /api/projects/months).
  // Only the latest answer counts, so a slow reply never overwrites a newer choice.
  private async loadMonths(): Promise<void> {
    const request = ++this.monthsRequest;
    const controls = this.form.controls;
    const deadline = controls.deadline.value;
    if (!deadline || controls.deadline.invalid) {
      this.applyMonths({ months: [], currentKey: this.currentKey(), recommended: null });
      return;
    }
    const likely = controls.likely.valid ? parseAmount(controls.likely.value) : null;
    try {
      const answer = await this.api.months({
        deadline: toDateString(deadline),
        bankAccountId: controls.bankAccountId.value || null,
        likely: likely === null ? null : likely / 100,
        projectId: this.project?.id ?? null,
      });
      if (request === this.monthsRequest) {
        this.applyMonths(answer);
      }
    } catch {
      if (request === this.monthsRequest) {
        this.recommended.set(null);
      }
    }
  }

  // Months already typed keep their amount; new months from this month on get the monthly amount.
  private applyMonths(answer: ProjectMonths): void {
    const typed = new Map(this.periods().map((period, index) => [period.key, this.amounts.at(index).value]));
    const fill = this.form.controls.monthly.valid ? this.form.controls.monthly.value : '';
    this.amounts.clear({ emitEvent: false });
    for (const period of answer.months) {
      this.amounts.push(monthControl(typed.get(period.key) ?? (period.key >= answer.currentKey ? fill : '')), { emitEvent: false });
    }
    this.periods.set(answer.months);
    this.currentKey.set(answer.currentKey);
    this.recommended.set(answer.recommended === null ? null : toMinor(answer.recommended));
    this.amounts.updateValueAndValidity();
    if (!this.edited && answer.recommended !== null) {
      this.useRecommended();
    }
  }

  private applyAhead(control: FormControl<string>): void {
    if (control.invalid) {
      return;
    }
    for (const item of this.ahead()) {
      this.amounts.at(item.index).setValue(control.value.trim());
    }
  }

  // The monthly amount shows the months' one amount, or stays empty when they differ.
  private showSameAhead(): void {
    const same = this.sameAhead();
    this.form.controls.monthly.setValue(same === null || same === 0 ? '' : minorToInput(same));
  }

  private money(minor: number): string {
    return formatMoney(minor, this.data.currency);
  }

  // 409 (an account another project counts, R-PRJ-03) says which; 400 marks the field it names.
  private async run(call: () => Promise<ProjectsResponse>, fallback: string): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      this.dialogRef.close(await call());
    } catch (error: unknown) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        this.error.set(error.error?.detail ?? 'This account is already counted by another project.');
      } else if (error instanceof HttpErrorResponse && error.status === 404) {
        this.error.set('This project no longer exists. Please reload the page.');
      } else if (error instanceof HttpErrorResponse && error.status === 400) {
        const errors = (error.error?.errors ?? {}) as Record<string, string[]>;
        if (errors['Name']) {
          this.form.controls.name.setErrors({ duplicate: true });
        }
        this.error.set(Object.values(errors)[0]?.[0] ?? fallback);
      } else {
        this.error.set(fallback);
      }
    } finally {
      this.busy.set(false);
    }
  }
}
