import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { AuthService } from './auth';
import { allowedGuard, authGuard, setupGuard } from './guards';
import { MeService } from './me';

describe('guards (PRD 7.7, AUTH-05)', () => {
  const user = signal<object | null>(null);
  const auth = { ready: vi.fn(), user };
  const me = { load: vi.fn() };

  beforeEach(() => {
    vi.clearAllMocks();
    user.set(null);
    auth.ready.mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: MeService, useValue: me },
      ],
    });
  });

  const run = (guard: typeof authGuard) =>
    TestBed.runInInjectionContext(() => guard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot));

  const urlOf = (result: unknown) => TestBed.inject(Router).serializeUrl(result as UrlTree);

  it('authGuard sends you to /sign-in when signed out', async () => {
    expect(urlOf(await run(authGuard))).toBe('/sign-in');
  });

  it('authGuard lets you in when signed in', async () => {
    user.set({ uid: 'user-1' });
    expect(await run(authGuard)).toBe(true);
  });

  it('setupGuard sends you to /sign-in without calling the API when signed out', async () => {
    expect(urlOf(await run(setupGuard))).toBe('/sign-in');
    expect(me.load).not.toHaveBeenCalled();
  });

  it('setupGuard sends you to /setup until setup is complete', async () => {
    user.set({ uid: 'user-1' });
    me.load.mockResolvedValue({ setupCompleted: false });
    expect(urlOf(await run(setupGuard))).toBe('/setup');
  });

  it('setupGuard lets you in once setup is complete', async () => {
    user.set({ uid: 'user-1' });
    me.load.mockResolvedValue({ setupCompleted: true });
    expect(await run(setupGuard)).toBe(true);
  });

  it('allowedGuard loads /api/me, so a reload of /setup checks the allowlist', async () => {
    user.set({ uid: 'user-1' });
    me.load.mockResolvedValue({ setupCompleted: false });
    expect(await run(allowedGuard)).toBe(true);
    expect(me.load).toHaveBeenCalledOnce();
  });

  it('allowedGuard sends you to the Dashboard once setup is complete (AUTH-05)', async () => {
    user.set({ uid: 'user-1' });
    me.load.mockResolvedValue({ setupCompleted: true });
    expect(urlOf(await run(allowedGuard))).toBe('/');
  });

  it('allowedGuard sends you to /sign-in without calling the API when signed out', async () => {
    expect(urlOf(await run(allowedGuard))).toBe('/sign-in');
    expect(me.load).not.toHaveBeenCalled();
  });

  it('allowedGuard stops on an API error (the interceptor navigates)', async () => {
    user.set({ uid: 'user-1' });
    me.load.mockRejectedValue(new Error('403'));
    expect(await run(allowedGuard)).toBe(false);
  });

  it('setupGuard stops on an API error (the interceptor navigates)', async () => {
    user.set({ uid: 'user-1' });
    me.load.mockRejectedValue(new Error('403'));
    expect(await run(setupGuard)).toBe(false);
  });
});
