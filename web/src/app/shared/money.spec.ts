import { FormControl } from '@angular/forms';
import { amountValidator, formatAmount, formatMoney, minorToInput, parseAmount, toMinor } from './money';

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
});
