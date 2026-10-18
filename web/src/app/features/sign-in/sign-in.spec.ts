import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth';
import { MeService } from '../../core/me';
import { SignIn } from './sign-in';

// AUTH-01 sign-in page with the owner's M9 design: the NAMA mark, the background artwork and the
// "Built by Ouf." credit. AUTH-06: the same page on /invite/<token> joins with the link.
describe('Sign in (AUTH-01)', () => {
  let signIn: ReturnType<typeof vi.fn>;
  let acceptInvite: ReturnType<typeof vi.fn>;

  function open(token: string | null = null): HTMLElement {
    signIn = vi.fn();
    acceptInvite = vi.fn();
    TestBed.configureTestingModule({
      imports: [SignIn],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { signIn } },
        { provide: MeService, useValue: { acceptInvite } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(token ? { token } : {}) } } },
      ],
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

  it('shows the invite caption on an invite link', () => {
    const root = open('abc123');

    expect(root.querySelector('.caption')!.textContent!.trim()).toBe("You're invited to NAMA. Sign in with Google to join.");
    expect(root.querySelector('button.google')!.textContent!.trim()).toBe('Sign in with Google');
  });

  it('signs in, joins with the link and opens the app without the link in the history', async () => {
    const root = open('abc123');
    signIn.mockResolvedValue(undefined);
    acceptInvite.mockResolvedValue({ setupCompleted: false });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    root.querySelector<HTMLButtonElement>('button.google')!.click();
    await settle();

    expect(acceptInvite).toHaveBeenCalledWith('abc123');
    expect(navigate).toHaveBeenCalledWith('/', { replaceUrl: true });
  });

  it('does not join when sign-in does not finish', async () => {
    const root = open('abc123');
    signIn.mockRejectedValue(new Error('popup closed'));

    root.querySelector<HTMLButtonElement>('button.google')!.click();
    await settle();

    expect(acceptInvite).not.toHaveBeenCalled();
    expect(root.querySelector('[role="alert"]')!.textContent!.trim()).toBe('Sign-in did not finish. Please try again.');
  });

  for (const [status, message] of [
    [404, 'This invite link was already used or revoked. Ask the person who shared NAMA for a new one.'],
    [410, 'This invite link has expired. Ask the person who shared NAMA for a new one.'],
  ] as const) {
    it(`says why a link no longer works (${status})`, async () => {
      const root = open('abc123');
      signIn.mockResolvedValue(undefined);
      acceptInvite.mockRejectedValue(new HttpErrorResponse({ status }));
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

      root.querySelector<HTMLButtonElement>('button.google')!.click();
      await settle();

      expect(navigate).not.toHaveBeenCalled();
      expect(root.querySelector('[role="alert"]')!.textContent!.trim()).toBe(message);
    });
  }
});
