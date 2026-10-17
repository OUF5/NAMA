import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

// PRD 4.7: names are trimmed, 1 to 40 characters, and unique ignoring case within their group.
// The API checks the same rules again.
export const NAME_MAX_LENGTH = 40;

export function isValidName(name: string | null | undefined): boolean {
  const trimmed = (name ?? '').trim();
  return trimmed.length >= 1 && trimmed.length <= NAME_MAX_LENGTH;
}

export function sameName(a: string, b: string): boolean {
  return a.trim().toLowerCase() === b.trim().toLowerCase();
}

export function nameValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => (isValidName(control.value) ? null : { name: true });
}

// For the "add a name" input: a name the list already has (ignoring case) is refused.
export function notInListValidator(list: () => readonly string[]): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = (control.value ?? '') as string;
    return value.trim() && list().some((name) => sameName(name, value)) ? { duplicate: true } : null;
  };
}
