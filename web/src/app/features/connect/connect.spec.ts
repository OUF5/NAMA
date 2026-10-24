import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { AuthService } from '../../core/auth';
import { AuthorizeRequest, ConnectApi, LEAVE_TO } from './connect-api';
import { Connect } from './connect';

// MCP-01: the page Claude Code opens to ask for read-only access. The request is in the address.
describe('Connect (MCP-01)', () => {
  const query = {
    response_type: 'code',
    client_id: 'nama_abc',
    redirect_uri: 'http://localhost:33418/callback',
    code_challenge: 'E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM',
    code_challenge_method: 'S256',
    state: 'state-123',
    scope: 'nama.read',
    resource: 'http://localhost:4200/api/mcp',
  };
  const request: AuthorizeRequest = {
    responseType: 'code',
    clientId: 'nama_abc',
    redirectUri: 'http://localhost:33418/callback',
    codeChallenge: 'E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM',
    codeChallengeMethod: 'S256',
    state: 'state-123',
    scope: 'nama.read',
    resource: 'http://localhost:4200/api/mcp',
  };

  let user: ReturnType<typeof signal<{ email: string } | null>>;
  let signIn: ReturnType<typeof vi.fn>;
  // Mocks (vi.fn) typed as the API's methods.
  let check: ConnectApi['check'];
  let authorize: ConnectApi['authorize'];
  let leaveTo: ReturnType<typeof vi.fn>;

  function open(signedIn = true): HTMLElement {
    user = signal(signedIn ? { email: 'owner@example.com' } : null);
    signIn = vi.fn(async () => user.set({ email: 'owner@example.com' }));
    leaveTo = vi.fn();
    TestBed.configureTestingModule({
      imports: [Connect],
      providers: [
        { provide: AuthService, useValue: { user, ready: vi.fn(async () => undefined), signIn } },
        // Calls the current mocks, so a test can swap one.
        {
          provide: ConnectApi,
          useValue: { check: (r: AuthorizeRequest) => check(r), authorize: (r: AuthorizeRequest, allow: boolean) => authorize(r, allow) },
        },
        { provide: LEAVE_TO, useValue: leaveTo },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
      ],
    });
    const fixture = TestBed.createComponent(Connect);
    fixture.autoDetectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    check = vi.fn(async () => ({ clientName: 'Claude Code (nama)', redirectHost: 'localhost:33418', scope: 'nama.read' }));
    authorize = vi.fn(async () => ({ redirectTo: 'http://localhost:33418/callback?code=abc&state=state-123' }));
  });

  // The page awaits the auth state, then the API: a few turns of the event loop.
  async function settle(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) {
      await new Promise((resolve) => setTimeout(resolve));
    }
  }

  function text(element: Element | null): string {
    return (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function button(root: HTMLElement, label: string): HTMLButtonElement {
    return Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) => text(b) === label)!;
  }

  it('checks the request and shows the app, the access and where it sends you', async () => {
    const root = open();
    await settle();

    expect(check).toHaveBeenCalledWith(request);
    expect(text(root.querySelector('h1'))).toBe('Connect Claude Code (nama)?');
    expect(Array.from(root.querySelectorAll('.facts li')).map(text)).toEqual([
      'Read-only: it can see your budget, but not change it.',
      'Your data as owner@example.com',
      'Sends you back to localhost:33418',
    ]);
    // The MCP spec's warning for an app on this computer.
    expect(text(root.querySelector('.warn'))).toBe('Allow only if you started this yourself from Claude Code on this computer.');
    // Allow is the one filled button (UX-04).
    expect(Array.from(root.querySelectorAll('button[mat-flat-button]')).map(text)).toEqual(['Allow']);
  });

  it('gives no local warning for claude.ai', async () => {
    check = vi.fn(async () => ({ clientName: 'Claude', redirectHost: 'claude.ai', scope: 'nama.read' }));
    const root = open();
    await settle();

    expect(root.querySelector('.warn')).toBeNull();
  });

  it('sends you back to the app with the code on Allow', async () => {
    const root = open();
    await settle();

    button(root, 'Allow').click();
    await settle();

    expect(authorize).toHaveBeenCalledWith(request, true);
    expect(leaveTo).toHaveBeenCalledWith('http://localhost:33418/callback?code=abc&state=state-123');
    expect(text(root.querySelector('h1'))).toBe('Back to Claude Code (nama)');
  });

  it('sends you back without access on Cancel', async () => {
    authorize = vi.fn(async () => ({ redirectTo: 'http://localhost:33418/callback?error=access_denied&state=state-123' }));
    const root = open();
    await settle();

    button(root, 'Cancel').click();
    await settle();

    expect(authorize).toHaveBeenCalledWith(request, false);
    expect(leaveTo).toHaveBeenCalledWith('http://localhost:33418/callback?error=access_denied&state=state-123');
  });

  it('signs in first, keeping the request', async () => {
    const root = open(false);
    await settle();
    expect(check).not.toHaveBeenCalled();

    root.querySelector<HTMLButtonElement>('button.google')!.click();
    await settle();

    expect(signIn).toHaveBeenCalledTimes(1);
    expect(check).toHaveBeenCalledWith(request);
    expect(text(root.querySelector('h1'))).toBe('Connect Claude Code (nama)?');
  });

  // MCP-02: shown on the page; the browser goes nowhere.
  it('shows why a request is not valid and sends you nowhere', async () => {
    check = vi.fn(async () => {
      throw new HttpErrorResponse({ status: 400, error: { detail: 'This app is not registered with NAMA. Connect it again from the app.' } });
    });
    const root = open();
    await settle();

    expect(text(root.querySelector('h1'))).toBe('This request is not valid');
    expect(text(root.querySelector('.caption'))).toBe('This app is not registered with NAMA. Connect it again from the app.');
    expect(root.querySelector('button')).toBeNull();
    expect(leaveTo).not.toHaveBeenCalled();
  });

  it('offers to try again when NAMA cannot be reached', async () => {
    check = vi.fn(async () => {
      throw new HttpErrorResponse({ status: 0 });
    });
    const root = open();
    await settle();

    expect(text(root.querySelector('.error'))).toBe('NAMA could not be reached. Please try again.');
    check = vi.fn(async () => ({ clientName: 'Claude Code (nama)', redirectHost: 'localhost:33418', scope: 'nama.read' }));
    button(root, 'Try again').click();
    await settle();

    expect(text(root.querySelector('h1'))).toBe('Connect Claude Code (nama)?');
    expect(root.querySelector('.error')).toBeNull();
  });
});
