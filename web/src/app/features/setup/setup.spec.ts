import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth';
import { Setup } from './setup';

describe('Setup wizard (PRD 5.2)', () => {
  const auth = { user: signal({ uid: 'user-1', displayName: 'Owner' }), signOut: vi.fn() };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [Setup],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: auth }],
    });
  });

  // The Add buttons once sat in a plain <form>, which the browser submitted: the page reloaded
  // and the account was not added.
  it('adds a bank account with the Add button and with Enter, without submitting a form', async () => {
    const fixture = TestBed.createComponent(Setup);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const input = Array.from(root.querySelectorAll('input')).find(
      (el) => el.closest('mat-form-field')?.textContent?.includes('Account name'),
    )!;
    const addButton = input.closest('.add-row')!.querySelector('button')!;

    expect(input.closest('form')).toBeNull();
    expect(addButton.type).toBe('button');

    input.value = ' SNB ';
    input.dispatchEvent(new Event('input'));
    addButton.click();
    input.value = 'Al Rajhi';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await fixture.whenStable();

    const names = Array.from(root.querySelectorAll('.names li > span')).map((el) => el.textContent);
    expect(names).toEqual(['SNB', 'Al Rajhi']);
  });

  it('prefills your name from Google', async () => {
    const fixture = TestBed.createComponent(Setup);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const input = Array.from(root.querySelectorAll('input')).find(
      (el) => el.closest('mat-form-field')?.textContent?.includes('Your name'),
    )!;
    expect(input.value).toBe('Owner');
  });
});
