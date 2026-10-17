import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormArray,
  FormControl,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MeService } from '../../core/me';
import { OUTLINE_FORM_FIELDS } from '../../shared/form-field';
import { amountValidator, formatMoney, minorToInput, parseAmount, toMinor } from '../../shared/money';
import { PeriodInfo, formatDate, formatDay } from '../../shared/month-label';
import { NAME_MAX_LENGTH, isValidName, notInListValidator } from '../../shared/names';
import { Category, PlanApi, PlansResponse, SubCategory } from './plan-api';

// Owner's choice (M4): an empty month or 0 means no plan for that month (R-PLN-01).
function optionalAmount(): ValidatorFn {
  const amount = amountValidator(true);
  return (control: AbstractControl): ValidationErrors | null =>
    ((control.value ?? '') as string).trim() === '' ? null : amount(control);
}

// PRD 5.3 Plan: sub-categories and their month amounts (PLN-01 to PLN-06). All sums come from
// the API (R-PLN-02, R-PLN-03); the page only formats them.
@Component({
  selector: 'app-plan',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule],
  providers: [OUTLINE_FORM_FIELDS],
  templateUrl: './plan.html',
  styleUrl: './plan.scss',
})
export class Plan {
  private readonly api = inject(PlanApi);
  private readonly meService = inject(MeService);

  protected readonly nameMax = NAME_MAX_LENGTH;
  protected readonly formatDate = formatDate;
  protected readonly formatDay = formatDay;

  protected readonly loadError = signal('');
  protected readonly data = signal<PlansResponse | null>(null);
  protected readonly categories = signal<Category[]>([]);
  protected readonly busy = signal(false);
  protected readonly status = signal('');

  private readonly currency = computed(() => this.meService.me()?.profile?.currency ?? 'SAR');

  protected readonly periods = computed<PeriodInfo[]>(() => this.data()?.fiscalYear.periods ?? []);

  // Archive comes with Settings (M8); archived ones are left out here.
  protected readonly activeCategories = computed(() =>
    this.categories()
      .filter((category) => !category.archived)
      .map((category) => ({ ...category, subCategories: category.subCategories.filter((sub) => !sub.archived) })),
  );

  // PLN-06
  protected readonly hasSubCategories = computed(() =>
    this.activeCategories().some((category) => category.subCategories.length > 0),
  );

  // PLN-03: the months with their totals.
  protected readonly totalRows = computed(() => {
    const totals = new Map((this.data()?.totals ?? []).map((row) => [row.periodKey, row]));
    return this.periods().map((period) => ({ period, totals: totals.get(period.key) }));
  });

  // The month editor (PLN-01, PLN-02, PLN-04): one sub-category at a time.
  protected readonly editing = signal<SubCategory | null>(null);
  protected readonly months = new FormArray<FormControl<string>>([]);
  protected readonly sameAmount = new FormControl('', { nonNullable: true, validators: amountValidator(true) });
  protected readonly saveError = signal('');

  protected readonly newCategory = new FormControl('', {
    nonNullable: true,
    validators: notInListValidator(() => this.categories().map((category) => category.name)),
  });
  private readonly newSubCategories = new Map<string, FormControl<string>>();

  constructor() {
    void this.load();
  }

  protected money(amount: number | undefined): string {
    return formatMoney(toMinor(amount ?? 0), this.currency());
  }

  protected subTotal(subCategoryId: string): number {
    return this.data()?.plans.find((plan) => plan.subCategoryId === subCategoryId)?.total ?? 0;
  }

  protected categoryTotal(categoryId: string): number {
    return this.data()?.categoryTotals[categoryId] ?? 0;
  }

  // The "add a sub-category" input of one category.
  protected newSubCategory(category: Category): FormControl<string> {
    let control = this.newSubCategories.get(category.id);
    if (!control) {
      control = new FormControl('', {
        nonNullable: true,
        validators: notInListValidator(() => this.subNames(category.id)),
      });
      this.newSubCategories.set(category.id, control);
    }
    return control;
  }

  protected openEditor(sub: SubCategory): void {
    const amounts = this.data()?.plans.find((plan) => plan.subCategoryId === sub.id)?.amounts ?? {};
    this.months.clear();
    for (const period of this.periods()) {
      const amount = amounts[period.key];
      this.months.push(
        new FormControl(amount ? minorToInput(toMinor(amount)) : '', { nonNullable: true, validators: optionalAmount() }),
      );
    }
    this.sameAmount.reset('');
    this.saveError.set('');
    this.status.set('');
    this.editing.set(sub);
  }

  protected cancel(): void {
    this.editing.set(null);
  }

  // PLN-02 "Same amount for all months".
  protected applyToAll(): void {
    if (this.sameAmount.invalid) {
      this.sameAmount.markAsTouched();
      return;
    }
    const value = this.sameAmount.value.trim();
    for (const control of this.months.controls) {
      control.setValue(value);
    }
  }

  // PLN-02 "Copy from previous month".
  protected copyPrevious(index: number): void {
    this.months.at(index).setValue(this.months.at(index - 1).value);
  }

  // PLN-04: any month, closed months included. Empty months are left out, which clears them.
  protected async save(): Promise<void> {
    const sub = this.editing();
    const fyId = this.data()?.fiscalYear.id;
    if (!sub || !fyId) {
      return;
    }
    if (this.months.invalid) {
      this.months.markAllAsTouched();
      return;
    }
    const amounts: Record<string, number> = {};
    this.periods().forEach((period, i) => {
      const minor = parseAmount(this.months.at(i).value, true);
      if (minor !== null) {
        amounts[period.key] = minor / 100;
      }
    });
    this.busy.set(true);
    this.saveError.set('');
    try {
      this.data.set(await this.api.savePlan(fyId, sub.id, amounts));
      this.editing.set(null);
      this.status.set(`Saved ${sub.name}.`);
    } catch {
      this.saveError.set('The amounts could not be saved. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }

  // PLN-01: add a sub-category, then go straight to its month amounts.
  protected async addSubCategory(category: Category): Promise<void> {
    const control = this.newSubCategory(category);
    const name = this.checkName(control);
    if (name === null) {
      return;
    }
    this.busy.set(true);
    try {
      const sub = await this.api.addSubCategory(category.id, name);
      this.categories.update((list) =>
        list.map((c) => (c.id === category.id ? { ...c, subCategories: [...c.subCategories, sub] } : c)),
      );
      control.reset('');
      this.openEditor(sub);
    } catch (error: unknown) {
      this.nameRefused(control, error);
    } finally {
      this.busy.set(false);
    }
  }

  // PLN-05: categories can be added after setup.
  protected async addCategory(): Promise<void> {
    const name = this.checkName(this.newCategory);
    if (name === null) {
      return;
    }
    this.busy.set(true);
    try {
      const category = await this.api.addCategory(name);
      this.categories.update((list) => [...list, category]);
      this.newCategory.reset('');
    } catch (error: unknown) {
      this.nameRefused(this.newCategory, error);
    } finally {
      this.busy.set(false);
    }
  }

  // The FY that contains today, or the latest one after it ends (FY rollover is M8).
  private async load(): Promise<void> {
    try {
      const me = await this.meService.load();
      const fyId = me.currentFyId ?? (await this.api.fiscalYears()).at(-1)?.id;
      if (!fyId) {
        this.loadError.set('There is no fiscal year yet.');
        return;
      }
      const [categories, data] = await Promise.all([this.api.categories(), this.api.plans(fyId)]);
      this.categories.set(categories);
      this.data.set(data);
    } catch {
      this.loadError.set('Plan could not be loaded. Please reload the page.');
    }
  }

  private subNames(categoryId: string): string[] {
    return this.categories().find((category) => category.id === categoryId)?.subCategories.map((sub) => sub.name) ?? [];
  }

  // PRD 4.7: the trimmed name when it is valid and new, otherwise null with the error shown.
  private checkName(control: FormControl<string>): string | null {
    const name = control.value.trim();
    if (!isValidName(name)) {
      control.setErrors({ name: true });
    }
    if (control.invalid) {
      control.markAsTouched();
      return null;
    }
    return name;
  }

  private nameRefused(control: FormControl<string>, error: unknown): void {
    control.setErrors(error instanceof HttpErrorResponse && error.status === 400 ? { duplicate: true } : { failed: true });
    control.markAsTouched();
  }
}
