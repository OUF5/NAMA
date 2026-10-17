import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { MeService } from '../../core/me';
import { formatMoney, parseAmount, toMinor } from '../../shared/money';
import { fromDateString, toDateString } from '../../shared/month-label';
import { Category, PlanApi } from '../plan/plan-api';
import { BankAccount, EntryApi } from './entry-api';
import { EntryFields, applyServerErrors, createEntryForm, entryAccountOptions, entryCategoryOptions } from './entry-fields';

// PRD 5.4 Add entry (ENT-01 to ENT-03). Page load is 2 calls (NFR-01): categories and bank accounts.
// /api/me is already loaded by setupGuard and brings the last used account, today and the FY range.
// The API assigns the month and FY from the date (R-CAL-09). The fields are shared with the
// Entries edit dialog (TBL-02).
@Component({
  selector: 'app-add-entry',
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatIconModule, EntryFields],
  templateUrl: './add-entry.html',
  styleUrl: './add-entry.scss',
})
export class AddEntry {
  private readonly planApi = inject(PlanApi);
  private readonly entryApi = inject(EntryApi);
  private readonly meService = inject(MeService);
  private readonly fields = viewChild(EntryFields);

  protected readonly loadError = signal('');
  protected readonly loaded = signal(false);
  protected readonly categories = signal<Category[]>([]);
  protected readonly accounts = signal<BankAccount[]>([]);
  protected readonly busy = signal(false);
  protected readonly saveError = signal('');
  // ENT-02 "Saved": shown in the Save bar until the next amount is typed.
  protected readonly saved = signal('');

  protected readonly form = createEntryForm();

  protected readonly me = this.meService.me;
  protected readonly currency = computed(() => this.me()?.profile?.currency ?? 'SAR');
  protected readonly hasCategories = computed(() => entryCategoryOptions(this.categories()).length > 0);
  protected readonly hasAccounts = computed(() => entryAccountOptions(this.accounts()).length > 0);

  constructor() {
    // The "Saved" line stays until the next entry starts.
    this.form.controls.amount.valueChanges.pipe(takeUntilDestroyed()).subscribe((amount) => {
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
}
