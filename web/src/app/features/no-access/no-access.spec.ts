import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth';
import { NoAccess } from './no-access';

// AUTH-02: someone who is not an admin and has not joined with a link is told how to get in.
describe('No access (AUTH-02)', () => {
  function open(): HTMLElement {
    TestBed.configureTestingModule({
      imports: [NoAccess],
      providers: [{ provide: AuthService, useValue: { user: signal({ email: 'stranger@example.com' }), signOut: vi.fn() } }],
    });
    const fixture = TestBed.createComponent(NoAccess);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('says who is not allowed and to ask for an invite link', () => {
    const root = open();

    const lines = [...root.querySelectorAll('p')].map((p) => p.textContent!.trim());
    expect(lines).toEqual(['stranger@example.com is not allowed to use NAMA.', 'Ask the person who shared NAMA for an invite link.']);
    expect(root.querySelector('button')!.textContent!.trim()).toBe('Use another account');
  });
});
