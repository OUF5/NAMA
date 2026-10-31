import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ErrorStateMatcher, provideNativeDateAdapter } from '@angular/material/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { amountValidator, formatMoney, minorToInput, parseAmount, toMinor } from '../../shared/money';
import { formatDate, fromDateString, toDateString } from '../../shared/month-label';
import { BankAccount } from '../add-entry/entry-api';
import { NOTE_MAX_LENGTH, TouchedErrorStateMatcher } from '../add-entry/entry-fields';
import { Project } from '../projects/projects-api';

// R-TRF-01: the two accounts must differ. Set on To, so its field shows the message.
function differentFrom(control: AbstractControl): ValidationErrors | null {
  const from = control.parent?.get('fromBankAccountId')?.value;
  return control.value && control.value === from ? { sameAccount: true } : null;
}

// TRF-01: the transfer form of Add and of the Banks edit dialog. From, To, Amount, Date, Note.
export function createTransferForm() {
  return new FormGroup({
    fromBankAccountId: new FormControl('', { nonNullable: true, validators: Validators.required }),
    toBankAccountId: new FormControl('', { nonNullable: true, validators: [Validators.required, differentFrom] }),
    amount: new FormControl('', { nonNullable: true, validators: amountValidator() }),
    date: new FormControl<Date | null>(null, Validators.required),
    note: new FormControl('', { nonNullable: true, validators: Validators.maxLength(NOTE_MAX_LENGTH) }),
  });
}

export type TransferForm = ReturnType<typeof createTransferForm>;

export function showAllTransferErrors(form: TransferForm): void {
  form.markAllAsTouched();
  Object.values(form.controls).forEach((control) => control.markAsDirty());
}

// 400 and 422 (a date outside every FY) come back as ProblemDetails with C# property names.
// Returns false when the error belongs to no field, so the caller shows a general message.
export function applyTransferServerErrors(form: TransferForm, error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse) || (error.status !== 400 && error.status !== 422)) {
    return false;
  }
  const controls = form.controls;
  const fields: Record<string, AbstractControl> = {
    FromBankAccountId: controls.fromBankAccountId,
    ToBankAccountId: controls.toBankAccountId,
    Amount: controls.amount,
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

// The fields and, on Add (split), a panel with both balances before and after the transfer. The page
// owns the form and its Save. Archived accounts are never offered, except the ones an edited transfer
// already has (as entries, M6).
@Component({
  selector: 'app-transfer-fields',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
  ],
  host: { '[class.split]': 'split()' },
  providers: [
    OUTLINE_FORM_FIELDS,
    provideNativeDateAdapter(),
    { provide: ErrorStateMatcher, useClass: TouchedErrorStateMatcher },
  ],
  templateUrl: './transfer-fields.html',
  styleUrls: ['../add-entry/entry-fields.scss', './transfer-fields.scss'],
})
export class TransferFields implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  readonly form = input.required<TransferForm>();
  readonly accounts = input.required<BankAccount[]>();
  readonly currency = input.required<string>();
  readonly firstDate = input<string | null>(null);
  readonly lastDate = input<string | null>(null);
  // An edited transfer keeps its accounts even when they are archived.
  readonly keep = input<string[]>([]);
  // R-BAL-03 balances today in minor units (null when an account has none yet), for the preview.
  readonly balances = input<Record<string, number | null>>({});
  // Add: the balances panel beside the fields. The edit dialog has no panel: the balances already
  // include the saved transfer.
  readonly split = input(false);
  // PRJ-06 (Add only): GET /api/projects, or null while not loaded (then no project shows).
  readonly projects = input<Project[] | null>(null);

  protected readonly noteMax = NOTE_MAX_LENGTH;

  protected readonly options = computed(() =>
    this.accounts().filter((account) => !account.archived || this.keep().includes(account.id)),
  );
  protected readonly minDate = computed(() => (this.firstDate() ? fromDateString(this.firstDate()!) : null));
  protected readonly maxDate = computed(() => (this.lastDate() ? fromDateString(this.lastDate()!) : null));
  protected readonly dateRange = computed(() =>
    this.firstDate() && this.lastDate() ? `${formatDate(this.firstDate()!)} to ${formatDate(this.lastDate()!)}` : '',
  );

  private readonly from = signal('');
  private readonly to = signal('');
  private readonly dateText = signal<string | null>(null);
  private readonly amountText = signal('');
  private readonly noteText = signal('');
  protected readonly noteLength = computed(() => this.noteText().length);

  // "SNB · SAR 4,100 → SAR 3,600": what moves, as a preview only (exact minor units); the saved
  // balances come from the API.
  protected readonly preview = computed(() => {
    const from = this.options().find((account) => account.id === this.from());
    const to = this.options().find((account) => account.id === this.to());
    if (!from || !to || from.id === to.id) {
      return null;
    }
    const amount = parseAmount(this.amountText()) ?? 0;
    const line = (account: BankAccount, change: number) => {
      const balance = this.balances()[account.id] ?? null;
      return {
        name: account.name,
        now: balance === null ? null : this.money(balance),
        after: balance === null || amount === 0 ? null : this.money(balance + change),
        negative: balance !== null && balance + change < 0,
      };
    };
    return { from: line(from, -amount), to: line(to, amount), amount: amount > 0 ? this.money(amount) : null };
  });

  // PRJ-06: the project that counts the To account (R-PRJ-03: one at most that is not Done). Shown
  // only, never saved: what reaches the account counts for the project anyway (R-PRJ-06).
  protected readonly project = new FormControl('', { nonNullable: true });
  protected readonly toProjects = computed(() =>
    (this.projects() ?? []).filter((project) => project.status !== 'done' && project.bankAccountId === this.to()),
  );
  protected readonly fromProject = computed(
    () => (this.projects() ?? []).find((project) => project.status !== 'done' && project.bankAccountId === this.from()) ?? null,
  );
  private readonly projectId = signal('');

  // PRJ-06: the project's month of the transfer's date: what is planned, what is in so far, and after
  // this transfer (R-PRJ-05, R-PRJ-06). A month to come has nothing in yet.
  protected readonly projectMonth = computed(() => {
    const project = this.toProjects().find((item) => item.id === this.projectId());
    const date = this.dateText();
    if (!project || !date) {
      return null;
    }
    const month = project.plan.find((item) => item.period.startDate <= date && date <= item.period.endDate);
    if (!month) {
      const first = project.plan[0]?.period.label;
      const last = project.plan.at(-1)?.period.label;
      return { name: project.name, label: null, outside: first ? `The date is outside its months (${first} to ${last}).` : 'It has no months.' };
    }
    const row = project.months.find((item) => item.period.key === month.period.key);
    const planned = toMinor(month.amount);
    const putIn = row ? (row.actual === null ? null : toMinor(row.actual)) : 0;
    const amount = parseAmount(this.amountText()) ?? 0;
    const after = putIn === null ? null : putIn + amount;
    return {
      name: project.name,
      label: month.period.label,
      outside: null,
      planned: this.money(planned),
      putIn: putIn === null ? 'Unknown' : this.money(putIn),
      known: putIn !== null,
      after: after !== null && amount > 0 ? this.money(after) : null,
      still: after === null ? null : after < planned ? this.money(planned - after) : null,
      over: after !== null && after > planned ? this.money(after - planned) : null,
    };
  });

  constructor() {
    // PRJ-06: a To account with a project picks it, and an empty amount becomes what is still to put
    // in that month: the transfer is the month's saving.
    effect(() => {
      const options = this.toProjects();
      untracked(() => this.pickProject(options));
    });
  }

  ngOnInit(): void {
    const controls = this.form().controls;
    this.from.set(controls.fromBankAccountId.value);
    this.to.set(controls.toBankAccountId.value);
    this.dateText.set(this.dateString(controls.date.value));
    controls.date.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((date) => this.dateText.set(this.dateString(date)));
    this.project.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => this.projectId.set(id));
    this.amountText.set(controls.amount.value);
    this.noteText.set(controls.note.value);
    controls.fromBankAccountId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => {
      this.from.set(id);
      controls.toBankAccountId.updateValueAndValidity({ emitEvent: false });
    });
    controls.toBankAccountId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => this.to.set(id));
    controls.amount.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((amount) => this.amountText.set(amount));
    controls.note.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((note) => this.noteText.set(note));
  }

  // From and To change places.
  protected swap(): void {
    const controls = this.form().controls;
    const from = controls.fromBankAccountId.value;
    controls.fromBankAccountId.setValue(controls.toBankAccountId.value);
    controls.toBankAccountId.setValue(from);
  }

  private pickProject(options: Project[]): void {
    const project = options[0];
    this.project.setValue(project?.id ?? '');
    this.projectId.set(project?.id ?? '');
    const amount = this.form().controls.amount;
    const date = this.dateText();
    const month = date ? project?.plan.find((item) => item.period.startDate <= date && date <= item.period.endDate) : undefined;
    if (!month || amount.value.trim()) {
      return;
    }
    const actual = project.months.find((item) => item.period.key === month.period.key)?.actual ?? 0;
    const still = toMinor(month.amount) - toMinor(actual);
    if (still > 0) {
      amount.setValue(minorToInput(still));
    }
  }

  private dateString(date: Date | null): string | null {
    return date instanceof Date && !isNaN(date.getTime()) ? toDateString(date) : null;
  }

  private money(minor: number): string {
    return formatMoney(minor, this.currency());
  }
}
