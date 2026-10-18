import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth';
import { SignIn } from './sign-in';

// AUTH-01 sign-in page with the owner's M9 design: the NAMA mark, the background artwork and the
// "Built by Ouf." credit.
describe('Sign in (AUTH-01)', () => {
  let signIn: ReturnType<typeof vi.fn>;

  function open(): HTMLElement {
    signIn = vi.fn();
    TestBed.configureTestingModule({
      imports: [SignIn],
      providers: [provideRouter([]), { provide: AuthService, useValue: { signIn } }],
    });
    const fixture = TestBed.createComponent(SignIn);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  }

  it('shows the mark, the name, the Google button, the artwork and the credit', () => {
    const root = open();

    expect(root.querySelector('.card .logo')!.getAttribute('aria-hidden')).toBe('true');
    expect(root.querySelector('h1')!.textContent!.trim()).toBe('NAMA');
    expect(root.querySelector('button.google')!.textContent!.trim()).toBe('Sign in with Google');
    const art = root.querySelector<HTMLImageElement>('img.art')!;
    expect(art.getAttribute('src')).toBe('sign-in-bg.svg');
    expect(art.getAttribute('alt')).toBe('');
    expect(root.querySelector('.credit')!.textContent!.trim()).toBe('Built by Ouf.');
  });

  it('signs in with Google and opens the app', async () => {
    const root = open();
    signIn.mockResolvedValue(undefined);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    root.querySelector<HTMLButtonElement>('button.google')!.click();
    await settle();

    expect(signIn).toHaveBeenCalledTimes(1);
    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('says so when sign-in does not finish', async () => {
    const root = open();
    signIn.mockRejectedValue(new Error('popup closed'));

    root.querySelector<HTMLButtonElement>('button.google')!.click();
    await settle();

    expect(root.querySelector('[role="alert"]')!.textContent!.trim()).toBe(
      'Sign-in did not finish. Please try again.',
    );
  });
});
