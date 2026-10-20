import { FormControl } from '@angular/forms';
import {
  amountValidator,
  balanceValidator,
  distribute,
  formatAmount,
  formatMoney,
  formatPercent,
  minorToInput,
  parseAmount,
  parseBalance,
  parsePercent,
  percentValidator,
  toBasisPoints,
  toMinor,
} from './money';

describe('money (R-MON-01, R-MON-02)', () => {
  it('parses typed amounts into minor units', () => {
    expect(parseAmount('125.50')).toBe(12550);
    expect(parseAmount(' 125.5 ')).toBe(12550);
    expect(parseAmount('15000')).toBe(1_500_000);
    expect(parseAmount('10000000')).toBe(1_000_000_000);
  });

  it('refuses more than 2 decimals, more than 10,000,000 and anything that is not a number', () => {
    for (const text of ['1.005', '10000000.01', '-1', '1,000', 'abc', '', '.5', '1.']) {
      expect(parseAmount(text)).toBeNull();
    }
  });

  it('refuses 0 unless zero is allowed (the wizard salary, SET-01)', () => {
    expect(parseAmount('0')).toBeNull();
    expect(parseAmount('0', true)).toBe(0);
    expect(parseAmount('0.00', true)).toBe(0);
  });

  it('validates form controls', () => {
    expect(new FormControl('0', amountValidator()).errors).toEqual({ amount: true });
    expect(new FormControl('0', amountValidator(true)).errors).toBeNull();
  });

  it('formats with the currency code and at most 2 decimals (docs/DESIGN.md)', () => {
    expect(formatMoney(124_000, 'SAR')).toBe('SAR 1,240');
    expect(formatMoney(12_550, 'SAR')).toBe('SAR 125.50');
    expect(formatMoney(0, 'USD')).toBe('USD 0');
    expect(formatMoney(-25_075, 'SAR')).toBe('SAR -250.75');
  });

  it('formats the number alone for "SAR 1,240 of 2,000"', () => {
    expect(formatAmount(200_000)).toBe('2,000');
    expect(formatAmount(150_050)).toBe('1,500.50');
    expect(formatAmount(0)).toBe('0');
  });

  it('turns API decimals into minor units and minor units into input text', () => {
    expect(toMinor(1500.5)).toBe(150_050);
    expect(toMinor(0.29)).toBe(29);
    expect(toMinor(10_000_000)).toBe(1_000_000_000);
    expect(minorToInput(150_050)).toBe('1500.50');
    expect(minorToInput(150_000)).toBe('1500');
    expect(minorToInput(5)).toBe('0.05');
    expect(parseAmount(minorToInput(123_456_789))).toBe(123_456_789);
  });

  // R-BAL-01: an opening balance can be 0 or below 0 (an overdraft or a card).
  it('parses a balance, below 0 too', () => {
    expect(parseBalance('5000')).toBe(500_000);
    expect(parseBalance('0')).toBe(0);
    expect(parseBalance(' -1200.5 ')).toBe(-120_050);
    expect(parseBalance('-10000000')).toBe(-1_000_000_000);
    for (const text of ['', '-', '--1', '1.005', '-10000000.01', '1,000']) {
      expect(parseBalance(text)).toBeNull();
    }
    expect(new FormControl('-1', balanceValidator()).errors).toBeNull();
    expect(new FormControl('x', balanceValidator()).errors).toEqual({ balance: true });
  });

  // R-BAL-02: a share is 0 to 100 with at most 2 decimals, in basis points.
  it('parses a share of income in basis points', () => {
    expect(parsePercent('80')).toBe(8_000);
    expect(parsePercent('33.33')).toBe(3_333);
    expect(parsePercent('0')).toBe(0);
    expect(parsePercent('100')).toBe(10_000);
    for (const text of ['100.01', '-1', '1.005', '', 'x']) {
      expect(parsePercent(text)).toBeNull();
    }
    expect(new FormControl('101', percentValidator()).errors).toEqual({ percent: true });
    expect(toBasisPoints(66.67)).toBe(6_667);
    expect(formatPercent(8_000)).toBe('80%');
    expect(formatPercent(3_333)).toBe('33.33%');
  });

  // The API's rule (BalanceCalculator.Distribute): rounded down, the rest to the largest share, the
  // first id on a tie. The same examples as BalanceCalculatorTests.
  it('splits income exactly as the API does', () => {
    expect(distribute(1_700_000, { main: 8_000, savings: 2_000 })).toEqual({ main: 1_360_000, savings: 340_000 });
    expect(distribute(1_700_001, { main: 8_000, savings: 2_000 })).toEqual({ main: 1_360_001, savings: 340_000 });
    expect(distribute(100, { a: 3_333, b: 3_333, c: 3_334 })).toEqual({ a: 33, b: 33, c: 34 });
    expect(distribute(1, { b: 5_000, a: 5_000 })).toEqual({ a: 1, b: 0 });
    expect(distribute(500, { main: 10_000, savings: 0 })).toEqual({ main: 500 });
    expect(distribute(500, {})).toEqual({});
  });
});
