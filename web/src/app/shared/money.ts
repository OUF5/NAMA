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
  return `${currency} ${formatAmount(minor)}`;
}

// The number alone, for "SAR 1,240 of 2,000" (docs/DESIGN.md category rows).
export function formatAmount(minor: number): string {
  const decimals = minor % 100 === 0 ? 0 : 2;
  return (minor / 100).toLocaleString('en-US', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });
}

// R-BAL-01: an opening balance may be 0 or negative (an overdraft or a card), at most 10,000,000
// either way. Returns minor units, or null when the text is not a valid balance.
export function parseBalance(text: string | null | undefined): number | null {
  const value = (text ?? '').trim();
  const negative = value.startsWith('-');
  const minor = parseAmount(negative ? value.slice(1) : value, true);
  return minor === null ? null : negative ? -minor : minor;
}

export function balanceValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => (parseBalance(control.value) === null ? { balance: true } : null);
}

// R-BAL-02: a share of income, 0 to 100 with at most 2 decimals, in basis points (80.5% is 8050).
export function parsePercent(text: string | null | undefined): number | null {
  const value = (text ?? '').trim();
  if (!AMOUNT.test(value)) {
    return null;
  }
  const [whole, fraction = ''] = value.split('.');
  const basisPoints = Number(whole) * 100 + Number(fraction.padEnd(2, '0'));
  return basisPoints > 10_000 ? null : basisPoints;
}

export function percentValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => (parsePercent(control.value) === null ? { percent: true } : null);
}

// API percents are decimals (80, 33.33).
export function toBasisPoints(percent: number): number {
  return Math.round(percent * 100);
}

// "80%", "33.33%"
export function formatPercent(basisPoints: number): string {
  const decimals = basisPoints % 100 === 0 ? 0 : 2;
  return `${(basisPoints / 100).toFixed(decimals)}%`;
}

// R-BAL-02, the same rule as BalanceCalculator.Distribute in the API, for the split dialog's preview
// only: each share rounded down, and the minor units left go to the largest share (the first account
// id on a tie). The saved amounts come from the API.
export function distribute(amountMinor: number, shares: Record<string, number>): Record<string, number> {
  const parts = Object.entries(shares).filter(([, share]) => share > 0);
  if (parts.length === 0) {
    return {};
  }
  const result: Record<string, number> = {};
  for (const [id, share] of parts) {
    result[id] = Math.floor((amountMinor * share) / 10_000);
  }
  const [largest] = [...parts].sort(([a, x], [b, y]) => y - x || (a < b ? -1 : a > b ? 1 : 0))[0];
  result[largest] += amountMinor - Object.values(result).reduce((sum, part) => sum + part, 0);
  return result;
}

// API amounts are decimals with at most 2 decimals (R-MON-01); the browser works in minor units.
export function toMinor(amount: number): number {
  return Math.round(amount * 100);
}

// For an input: 150050 → "1500.50", 150000 → "1500".
export function minorToInput(minor: number): string {
  const whole = Math.trunc(minor / 100);
  const cents = minor % 100;
  return cents === 0 ? String(whole) : `${whole}.${String(cents).padStart(2, '0')}`;
}
