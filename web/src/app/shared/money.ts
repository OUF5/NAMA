import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

// R-MON-03: chosen in the wizard, formatting only. The same list as Money.Currencies in the API.
export const CURRENCIES = ['SAR', 'QAR', 'AED', 'USD', 'EUR', 'GBP'] as const;

const MAX_MINOR = 1_000_000_000; // 10,000,000.00 (R-MON-02)
const AMOUNT = /^\d+(\.\d{1,2})?$/;

// Inline check of R-MON-02 for typed text: at most 2 decimals and at most 10,000,000,
// greater than 0 unless zero is allowed (the wizard salary, SET-01). The API checks again.
// Returns minor units (125.50 → 12550), or null when the text is not a valid amount.
export function parseAmount(text: string | null | undefined, allowZero = false): number | null {
  const value = (text ?? '').trim();
  if (!AMOUNT.test(value)) {
    return null;
  }
  const [whole, fraction = ''] = value.split('.');
  const minor = Number(whole) * 100 + Number(fraction.padEnd(2, '0'));
  if (minor > MAX_MINOR || (!allowZero && minor === 0)) {
    return null;
  }
  return minor;
}

export function amountValidator(allowZero = false): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null =>
    parseAmount(control.value, allowZero) === null ? { amount: true } : null;
}

// docs/DESIGN.md: "SAR 1,240", never more than 2 decimals.
export function formatMoney(minor: number, currency: string): string {
  const decimals = minor % 100 === 0 ? 0 : 2;
  const number = (minor / 100).toLocaleString('en-US', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });
  return `${currency} ${number}`;
}
