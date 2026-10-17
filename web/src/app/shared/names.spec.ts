import { FormControl } from '@angular/forms';
import { isValidName, nameValidator, notInListValidator } from './names';

describe('names (PRD 4.7)', () => {
  it('are trimmed and 1 to 40 characters', () => {
    expect(isValidName(' Food ')).toBe(true);
    expect(isValidName('a'.repeat(40))).toBe(true);
    expect(isValidName('a'.repeat(41))).toBe(false);
    expect(isValidName('   ')).toBe(false);
    expect(isValidName(null)).toBe(false);
    expect(new FormControl(' ', nameValidator()).errors).toEqual({ name: true });
  });

  it('are unique ignoring case', () => {
    const list = ['Food', 'SNB'];
    expect(new FormControl(' snb ', notInListValidator(() => list)).errors).toEqual({ duplicate: true });
    expect(new FormControl('Rent', notInListValidator(() => list)).errors).toBeNull();
    expect(new FormControl('', notInListValidator(() => list)).errors).toBeNull();
  });
});
