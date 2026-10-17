import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth';

// AUTH-03: every /api request carries the Firebase ID token (PRD 7.6 step 2).
// A 401 signs out and shows sign-in; a 403 shows the "Access not allowed" page (step 6).
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/')) {
    return next(req);
  }
  const auth = inject(AuthService);
  const router = inject(Router);

  return from(auth.idToken()).pipe(
    switchMap((token) =>
      next(token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req),
    ),
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        void auth.signOut();
      } else if (error instanceof HttpErrorResponse && error.status === 403) {
        void router.navigate(['/no-access']);
      }
      return throwError(() => error);
    }),
  );
};
