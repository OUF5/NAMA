import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthService } from './auth';
import { Me, MeService } from './me';

// PRD 7.7: waits for the Firebase auth state. Signed out → /sign-in.
export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  await auth.ready();
  return auth.user() ? true : router.createUrlTree(['/sign-in']);
};

// AUTH-02: for /setup. Loads /api/me, so a reload of /setup also checks the allowlist.
export const allowedGuard: CanActivateFn = async () => {
  const result = await loadMe(inject(AuthService), inject(MeService), inject(Router));
  return isMe(result) ? true : result;
};

// PRD 7.7, AUTH-05: until setup is complete, every page sends you to /setup.
export const setupGuard: CanActivateFn = async () => {
  const router = inject(Router);
  const result = await loadMe(inject(AuthService), inject(MeService), router);
  if (!isMe(result)) {
    return result;
  }
  return result.setupCompleted ? true : router.createUrlTree(['/setup']);
};

// Waits for the auth state itself, because guards of one route run at the same time.
// On an API error it stops (false): the auth interceptor already went to sign-in or no-access.
async function loadMe(auth: AuthService, me: MeService, router: Router): Promise<Me | UrlTree | false> {
  await auth.ready();
  if (!auth.user()) {
    return router.createUrlTree(['/sign-in']);
  }
  try {
    return await me.load();
  } catch {
    return false;
  }
}

function isMe(result: Me | UrlTree | false): result is Me {
  return result !== false && !(result instanceof UrlTree);
}
