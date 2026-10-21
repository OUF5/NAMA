import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentType } from '@angular/cdk/portal';
import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { firstValueFrom } from 'rxjs';
import { dialogSize } from '../../shared/dialog';
import { NameDialog, NameDialogData } from './name-dialog';
import { Category, PlanApi, SubCategory } from './plan-api';

const PHONE = '(max-width: 767.98px)';

// Active items first; archived ones fold away at the end of their list.
function split<T extends { archived: boolean }>(items: T[]): { active: T[]; archived: T[] } {
  return {
    active: items.filter((item) => !item.archived),
    archived: items.filter((item) => item.archived),
  };
}

// A change on this tab: the whole new list of categories and what to say about it.
export interface CategoriesChange {
  categories: Category[];
  message: string;
}

// Plan's Categories tab (owner note 9; STG-05 and PLN-05, moved from Settings): add, rename,
// archive and restore categories and sub-categories. No delete (PRD 4.6). One row per category opens
// its sub-categories; archived ones fold away at the end. Archive and restore are reversible, so they
// need no confirmation (UX-04). A new sub-category asks for its planned amounts next (PLN-01).
@Component({
  selector: 'app-plan-categories',
  imports: [NgTemplateOutlet, MatButtonModule, MatIconModule],
  templateUrl: './plan-categories.html',
  styleUrl: './plan-categories.scss',
})
export class PlanCategories {
  private readonly api = inject(PlanApi);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(BreakpointObserver);

  readonly categories = input.required<Category[]>();
  readonly changed = output<CategoriesChange>();
  readonly subAdded = output<{ sub: SubCategory; categoryName: string }>();

  protected readonly busy = signal(false);
  private readonly opened = signal<ReadonlySet<string>>(new Set());

  protected readonly lists = computed(() => {
    const { active, archived } = split(this.categories());
    const withSubs = (list: Category[]) =>
      list.map((category) => ({ ...category, subs: split(category.subCategories) }));
    return { active: withSubs(active), archived: withSubs(archived) };
  });

  protected isOpen(key: string): boolean {
    return this.opened().has(key);
  }

  protected toggle(key: string): void {
    this.opened.update((keys) => {
      const next = new Set(keys);
      if (!next.delete(key)) {
        next.add(key);
      }
      return next;
    });
  }

  protected subCount(count: number): string {
    return count === 0
      ? 'No sub-categories'
      : `${count} ${count === 1 ? 'sub-category' : 'sub-categories'}`;
  }

  // PLN-05. A new category opens, ready for its first sub-category.
  protected async addCategory(): Promise<void> {
    const category = await this.nameDialog<Category>({
      title: 'Add category',
      label: 'Category name',
      name: '',
      taken: this.categories().map((c) => c.name),
      duplicateMessage: 'This category already exists.',
      save: (name) => this.api.addCategory(name),
    });
    if (category) {
      this.opened.update((keys) => new Set(keys).add(`category-${category.id}`));
      this.changed.emit({
        categories: [...this.categories(), category],
        message: `Added ${category.name}.`,
      });
    }
  }

  protected async renameCategory(category: Category): Promise<void> {
    const renamed = await this.nameDialog<Category>({
      title: 'Rename category',
      label: 'Category name',
      name: category.name,
      taken: this.categories()
        .filter((c) => c.id !== category.id)
        .map((c) => c.name),
      duplicateMessage: 'This category already exists.',
      save: (name) => this.api.updateCategory(category.id, name, category.archived),
    });
    if (renamed) {
      this.replaceCategory(renamed, `Renamed to ${renamed.name}.`);
    }
  }

  protected async toggleCategory(category: Category): Promise<void> {
    await this.change(
      () => this.api.updateCategory(category.id, category.name, !category.archived),
      (updated) =>
        this.replaceCategory(
          updated,
          `${updated.name} ${updated.archived ? 'archived' : 'restored'}.`,
        ),
    );
  }

  // PLN-01: then straight to its planned amounts.
  protected async addSubCategory(category: Category): Promise<void> {
    const sub = await this.nameDialog<SubCategory>({
      title: `Add sub-category to ${category.name}`,
      label: 'Sub-category name',
      name: '',
      taken: category.subCategories.map((s) => s.name),
      duplicateMessage: `${category.name} already has this sub-category.`,
      save: (name) => this.api.addSubCategory(category.id, name),
    });
    if (sub) {
      this.opened.update((keys) => new Set(keys).add(`category-${category.id}`));
      this.replaceSub(sub, `Added ${sub.name}.`);
      this.subAdded.emit({ sub, categoryName: category.name });
    }
  }

  protected async renameSub(category: Category, sub: SubCategory): Promise<void> {
    const renamed = await this.nameDialog<SubCategory>({
      title: 'Rename sub-category',
      label: 'Sub-category name',
      name: sub.name,
      taken: category.subCategories.filter((s) => s.id !== sub.id).map((s) => s.name),
      duplicateMessage: `${category.name} already has this sub-category.`,
      save: (name) => this.api.updateSubCategory(sub.id, name, sub.archived),
    });
    if (renamed) {
      this.replaceSub(renamed, `Renamed to ${renamed.name}.`);
    }
  }

  protected async toggleSub(sub: SubCategory): Promise<void> {
    await this.change(
      () => this.api.updateSubCategory(sub.id, sub.name, !sub.archived),
      (updated) =>
        this.replaceSub(updated, `${updated.name} ${updated.archived ? 'archived' : 'restored'}.`),
    );
  }

  // An archive or restore straight from the list.
  private async change<T>(call: () => Promise<T>, done: (result: T) => void): Promise<void> {
    this.busy.set(true);
    try {
      done(await call());
    } catch {
      this.changed.emit({
        categories: this.categories(),
        message: 'It could not be saved. Please try again.',
      });
    } finally {
      this.busy.set(false);
    }
  }

  private replaceCategory(category: Category, message: string): void {
    this.changed.emit({
      categories: this.categories().map((c) => (c.id === category.id ? category : c)),
      message,
    });
  }

  // Replaces the sub-category, or adds it last in its category.
  private replaceSub(sub: SubCategory, message: string): void {
    this.changed.emit({
      categories: this.categories().map((c) =>
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
    });
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
