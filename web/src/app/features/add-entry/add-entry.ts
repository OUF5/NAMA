import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { merge } from 'rxjs';
import { MeService } from '../../core/me';
import { SummaryService } from '../../core/summary';
import { incomeMonths } from '../../shared/income-months';
import { formatMoney, parseAmount, toMinor } from '../../shared/money';
import { fromDateString, toDateString } from '../../shared/month-label';
import { BanksApi } from '../banks/banks-api';
import {
  TransferFields,
  applyTransferServerErrors,
  createTransferForm,
  showAllTransferErrors,
} from '../banks/transfer-fields';
import { Category, PlanApi } from '../plan/plan-api';
import { IncomeItemFields, createIncomeItemForm, incomeItemChange, incomeItemContext } from '../settings/income-item-fields';
import { Income, SettingsApi } from '../settings/settings-api';
import { BankAccount, EntryApi } from './entry-api';
import {
  EntryFields,
  applyServerErrors,
  createEntryForm,
  entryAccountOptions,
  entryCategoryOptions,
  showAllErrors,
} from './entry-fields';

type Mode = 'expense' | 'transfer' | 'income';

// PRD 5.4 Add (ENT-01 to ENT-03, TRF-01, INC-01): an expense, a transfer between two of your accounts
// (?type=transfer) or additional income (?type=income, owner note 17). Expense is the default, so
// logging a spend stays one tap away. Page load is 2 calls (NFR-01): categories and bank accounts;
// Income loads its month pickers (GET /api/income) the first time it is shown. /api/me is already loaded by setupGuard and brings the
// last used account, today and the FY range; the transfer's balance preview uses the top bar's
// summary (no extra call). The API assigns the month and FY from the date (R-CAL-09). The expense
// fields are shared with the Entries edit dialog (TBL-02), the transfer fields with the Banks one, and
// the income fields are the Settings dialog's (the same box, owner's request).
@Component({
  selector: 'app-add-entry',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatButtonToggleModule,
    MatIconModule,
    EntryFields,
    TransferFields,
    IncomeItemFields,
  ],
  templateUrl: './add-entry.html',
  styleUrl: './add-entry.scss',
})
export class AddEntry {
  private readonly planApi = inject(PlanApi);
  private readonly entryApi = inject(EntryApi);
  private readonly banksApi = inject(BanksApi);
  private readonly settingsApi = inject(SettingsApi);
  private readonly meService = inject(MeService);
  private readonly summaryService = inject(SummaryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fields = viewChild(EntryFields);

  protected readonly loadError = signal('');
  protected readonly loaded = signal(false);
  protected readonly categories = signal<Category[]>([]);
  protected readonly accounts = signal<BankAccount[]>([]);
  protected readonly busy = signal(false);
  protected readonly saveError = signal('');
  // ENT-02 "Saved": shown in the Save bar until the next amount is typed.
  protected readonly saved = signal('');

  // TRF-01, INC-01: what this page adds, from the URL.
  protected readonly mode = signal<Mode>('expense');
  protected readonly title = computed(() =>
    this.mode() === 'transfer' ? 'Add transfer' : this.mode() === 'income' ? 'Add income' : 'Add expense',
  );

  protected readonly form = createEntryForm();
  protected readonly transferForm = createTransferForm();
  protected readonly incomeForm = createIncomeItemForm(null, null, null);
  // INC-01: GET /api/income (the month pickers, Plan's income, the accounts), null until loaded.
  private readonly income = signal<Income | null>(null);
  // The preview's balances are the top bar's, so they follow every save.
  protected readonly incomeContext = computed(() => {
    const income = this.income();
    return income ? incomeItemContext(income, this.summaryService.summary(), this.me()?.today ?? null, null) : null;
  });
  protected readonly incomeLoadError = signal('');

  protected readonly me = this.meService.me;
  protected readonly currency = computed(() => this.me()?.profile?.currency ?? 'SAR');
  protected readonly hasCategories = computed(() => entryCategoryOptions(this.categories()).length > 0);
  protected readonly hasAccounts = computed(() => entryAccountOptions(this.accounts()).length > 0);
  // A transfer needs two active accounts.
  protected readonly canTransfer = computed(() => entryAccountOptions(this.accounts()).length > 1);

  // R-BAL-03 balances today, in minor units, for the transfer preview (the top bar's summary).
  protected readonly balances = computed(() =>
    Object.fromEntries(
      (this.summaryService.summary()?.accounts ?? []).map((account) => [
        account.id,
        account.balance === null ? null : toMinor(account.balance),
      ]),
    ),
  );

  constructor() {
    // The "Saved" line stays until the next entry starts.
    merge(
      this.form.controls.amount.valueChanges,
      this.transferForm.controls.amount.valueChanges,
      this.incomeForm.controls.amount.valueChanges,
    )
      .pipe(takeUntilDestroyed())
      .subscribe((amount) => {
        if (amount) {
          this.saved.set('');
        }
      });
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const type = params.get('type');
      const mode: Mode = type === 'transfer' || type === 'income' ? type : 'expense';
      if (mode !== this.mode()) {
        this.saved.set('');
        this.saveError.set('');
      }
      this.mode.set(mode);
      if (mode === 'income') {
        void this.loadIncomeMonths();
      }
    });
    void this.load();
  }

  // Expense, Transfer or Income; the URL keeps it, so Banks can link to a transfer.
  protected setMode(mode: Mode): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { type: mode === 'expense' ? null : mode },
      replaceUrl: true,
    });
  }

  // INC-01: the same item as Settings' "Add income" (R-INC-03 to R-INC-05), listed there afterwards.
  // Label and amount clear; the type, the months, what it counts in and the account stay. Plan's
  // income in the preview comes from GET /api/income again.
  protected async saveIncome(): Promise<void> {
    const change = incomeItemChange(this.incomeForm);
    if (!change) {
      return;
    }
    this.busy.set(true);
    this.saveError.set('');
    this.saved.set('');
    try {
      this.income.set(await this.settingsApi.addIncome(change));
      this.incomeForm.controls.label.reset('');
      this.incomeForm.controls.amount.reset('');
      const account = this.accounts().find((option) => option.id === change.bankAccountId)?.name;
      const months = incomeMonths({
        fromPeriodKey: change.fromPeriodKey,
        toPeriodKey: change.type === 'once' ? change.fromPeriodKey : change.toPeriodKey,
      });
      this.saved.set(
        [`Added ${change.label}`, formatMoney(toMinor(change.amount), this.currency()), months, account ? `into ${account}` : '']
          .filter(Boolean)
          .join(' · '),
      );
    } catch (error: unknown) {
      this.saveError.set(
        error instanceof HttpErrorResponse && error.status === 400
          ? 'Some values were not accepted. Please check them.'
          : 'The income could not be saved. Please try again.',
      );
    } finally {
      this.busy.set(false);
    }
  }

  // Once: a new item starts in the current month, as in Settings (else the first stored month).
  private async loadIncomeMonths(): Promise<void> {
    if (this.income()) {
      return;
    }
    this.incomeLoadError.set('');
    try {
      const income = await this.settingsApi.income();
      if (!this.incomeForm.controls.from.value) {
        this.incomeForm.controls.from.setValue(income.currentPeriodKey ?? income.fiscalYears[0]?.periods[0]?.key ?? '');
      }
      this.income.set(income);
    } catch {
      this.incomeLoadError.set('The income months could not be loaded. Please reload the page.');
    }
  }

  // TRF-01: amount and note clear; the accounts and the date stay, as for an expense (ENT-02).
  protected async saveTransfer(): Promise<void> {
    if (this.transferForm.invalid) {
      showAllTransferErrors(this.transferForm);
      return;
    }
    const value = this.transferForm.getRawValue();
    this.busy.set(true);
    this.saveError.set('');
    this.saved.set('');
    try {
      const transfer = await this.banksApi.addTransfer({
        fromBankAccountId: value.fromBankAccountId,
        toBankAccountId: value.toBankAccountId,
        date: toDateString(value.date!),
        amount: parseAmount(value.amount)! / 100,
        note: value.note.trim(),
      });
      this.transferForm.controls.amount.reset('');
      this.transferForm.controls.note.reset('');
      const name = (id: string) => this.accounts().find((account) => account.id === id)?.name ?? '';
      this.saved.set(
        `Moved ${formatMoney(toMinor(transfer.amount), this.currency())} from ${name(transfer.fromBankAccountId)} to ${name(transfer.toBankAccountId)}`,
      );
    } catch (error: unknown) {
      if (!applyTransferServerErrors(this.transferForm, error)) {
        this.saveError.set('The transfer could not be saved. Please try again.');
      }
    } finally {
      this.busy.set(false);
    }
  }

  // ENT-02: amount and note clear; category, sub-category, account and date stay for quick repeats.
  protected async save(): Promise<void> {
    if (this.form.invalid) {
      showAllErrors(this.form);
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
      const sub = this.categories()
        .flatMap((category) => category.subCategories)
        .find((option) => option.id === entry.subCategoryId);
      this.saved.set(`Saved ${formatMoney(toMinor(entry.amount), this.currency())} to ${sub?.name ?? 'the sub-category'}`);
      void this.fields()?.refreshIndicator();
    } catch (error: unknown) {
      if (!applyServerErrors(this.form, error)) {
        this.saveError.set('The entry could not be saved. Please try again.');
      }
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
      const active = entryAccountOptions(accounts);
      const last = active.find((account) => account.id === me.profile?.lastBankAccountId);
      const from = (last ?? active[0])?.id ?? '';
      this.form.controls.bankAccountId.setValue(from);
      this.incomeForm.controls.bankAccountId.setValue(from);
      // A transfer starts from the same account, to the next active one.
      this.transferForm.controls.fromBankAccountId.setValue(from);
      this.transferForm.controls.toBankAccountId.setValue(active.find((account) => account.id !== from)?.id ?? '');

      // Today, when it is inside a stored FY. After the last FY ends (rollover is M8) a date is picked.
      if (me.today && me.firstDate && me.lastDate && me.firstDate <= me.today && me.today <= me.lastDate) {
        this.form.controls.date.setValue(fromDateString(me.today));
        this.transferForm.controls.date.setValue(fromDateString(me.today));
      }
      this.loaded.set(true);
    } catch {
      this.loadError.set('The form could not be loaded. Please reload the page.');
    }
  }
}
