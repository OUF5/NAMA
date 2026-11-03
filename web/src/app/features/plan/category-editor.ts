import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentType } from '@angular/cdk/portal';
import { Injectable, inject, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { dialogSize } from '../../shared/dialog';
import { NameDialog, NameDialogData } from './name-dialog';
import { Category, PlanApi, SubCategory } from './plan-api';

const PHONE = '(max-width: 767.98px)';

// A change to the list: the whole new list of categories, what to say about it, and what was added.
export interface CategoriesChange {
  categories: Category[];
  message: string;
  added?: Category | SubCategory;
}

// Add, rename, archive and restore categories and sub-categories (STG-05, PLN-05), on Plan's Planning
// list since owner note 31 (a Categories tab of its own before). No delete (PRD 4.6). Archive and
// restore are reversible, so they need no confirmation (UX-04). Each call returns the change, or
// nothing when the dialog was closed.
@Injectable()
export class CategoryEditor {
  private readonly api = inject(PlanApi);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(BreakpointObserver);

  // An archive or restore is on its way.
  readonly busy = signal(false);

  async addCategory(categories: Category[]): Promise<CategoriesChange | undefined> {
    const category = await this.nameDialog<Category>({
      title: 'Add category',
      label: 'Category name',
      name: '',
      taken: categories.map((c) => c.name),
      duplicateMessage: 'This category already exists.',
      save: (name) => this.api.addCategory(name),
    });
    return category && { categories: [...categories, category], message: `Added ${category.name}.`, added: category };
  }

  async renameCategory(categories: Category[], category: Category): Promise<CategoriesChange | undefined> {
    const renamed = await this.nameDialog<Category>({
      title: 'Rename category',
      label: 'Category name',
      name: category.name,
      taken: categories.filter((c) => c.id !== category.id).map((c) => c.name),
      duplicateMessage: 'This category already exists.',
      save: (name) => this.api.updateCategory(category.id, name, category.archived),
    });
    return renamed && replaceCategory(categories, renamed, `Renamed to ${renamed.name}.`);
  }

  toggleCategory(categories: Category[], category: Category): Promise<CategoriesChange> {
    return this.change(
      categories,
      () => this.api.updateCategory(category.id, category.name, !category.archived),
      (updated) => replaceCategory(categories, updated, `${updated.name} ${updated.archived ? 'archived' : 'restored'}.`),
    );
  }

  // PLN-01: the caller opens its planned amounts next.
  async addSubCategory(categories: Category[], category: Category): Promise<CategoriesChange | undefined> {
    const sub = await this.nameDialog<SubCategory>({
      title: `Add sub-category to ${category.name}`,
      label: 'Sub-category name',
      name: '',
      taken: category.subCategories.map((s) => s.name),
      duplicateMessage: `${category.name} already has this sub-category.`,
      save: (name) => this.api.addSubCategory(category.id, name),
    });
    return sub && { ...replaceSub(categories, sub, `Added ${sub.name}.`), added: sub };
  }

  async renameSub(categories: Category[], category: Category, sub: SubCategory): Promise<CategoriesChange | undefined> {
    const renamed = await this.nameDialog<SubCategory>({
      title: 'Rename sub-category',
      label: 'Sub-category name',
      name: sub.name,
      taken: category.subCategories.filter((s) => s.id !== sub.id).map((s) => s.name),
      duplicateMessage: `${category.name} already has this sub-category.`,
      save: (name) => this.api.updateSubCategory(sub.id, name, sub.archived),
    });
    return renamed && replaceSub(categories, renamed, `Renamed to ${renamed.name}.`);
  }

  toggleSub(categories: Category[], sub: SubCategory): Promise<CategoriesChange> {
    return this.change(
      categories,
      () => this.api.updateSubCategory(sub.id, sub.name, !sub.archived),
      (updated) => replaceSub(categories, updated, `${updated.name} ${updated.archived ? 'archived' : 'restored'}.`),
    );
  }

  // An archive or restore straight from the list.
  private async change<T>(
    categories: Category[],
    call: () => Promise<T>,
    done: (result: T) => CategoriesChange,
  ): Promise<CategoriesChange> {
    this.busy.set(true);
    try {
      return done(await call());
    } catch {
      return { categories, message: 'It could not be saved. Please try again.' };
    } finally {
      this.busy.set(false);
    }
  }

  // One dialog at a time (UX-04); full screen on phones.
  private async nameDialog<T>(data: NameDialogData<T>): Promise<T | undefined> {
    const component = NameDialog as ComponentType<NameDialog<T>>;
    const ref = this.dialog.open<NameDialog<T>, NameDialogData<T>, T>(component, {
      ...dialogSize(this.breakpoints.isMatched(PHONE), '480px'),
      data,
    });
    return firstValueFrom(ref.afterClosed());
  }
}

function replaceCategory(categories: Category[], category: Category, message: string): CategoriesChange {
  return { categories: categories.map((c) => (c.id === category.id ? category : c)), message };
}

// Replaces the sub-category, or adds it last in its category.
function replaceSub(categories: Category[], sub: SubCategory, message: string): CategoriesChange {
  return {
    categories: categories.map((c) =>
      c.id !== sub.categoryId
        ? c
        : {
            ...c,
            subCategories: c.subCategories.some((s) => s.id === sub.id)
              ? c.subCategories.map((s) => (s.id === sub.id ? sub : s))
              : [...c.subCategories, sub],
          },
    ),
    message,
  };
}
