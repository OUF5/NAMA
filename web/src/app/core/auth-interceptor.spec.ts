import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './auth';
import { authInterceptor } from './auth-interceptor';

// Lets the interceptor's token promise resolve before looking at the request.
const settle = () => new Promise((resolve) => setTimeout(resolve));

describe('authInterceptor (AUTH-03, PRD 7.6)', () => {
  const auth = { idToken: vi.fn(), signOut: vi.fn() };
  const router = { navigate: vi.fn() };
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    vi.clearAllMocks();
    auth.idToken.mockResolvedValue('token-1');
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
        { provide: Router, useValue: router },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('adds the ID token to /api requests', async () => {
    const response = firstValueFrom(http.get('/api/me'));
    await settle();
    const request = backend.expectOne('/api/me');
    expect(request.request.headers.get('Authorization')).toBe('Bearer token-1');
    request.flush({});
    await response;
  });

  it('leaves other requests alone', async () => {
    const response = firstValueFrom(http.get('/favicon.ico'));
    const request = backend.expectOne('/favicon.ico');
    expect(request.request.headers.has('Authorization')).toBe(false);
    expect(auth.idToken).not.toHaveBeenCalled();
    request.flush({});
    await response;
  });

  it('signs out on 401', async () => {
    const response = firstValueFrom(http.get('/api/me'));
    await settle();
    backend.expectOne('/api/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    await expect(response).rejects.toMatchObject({ status: 401 });
    expect(auth.signOut).toHaveBeenCalledOnce();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('shows the no-access page on 403', async () => {
    const response = firstValueFrom(http.get('/api/me'));
    await settle();
    backend.expectOne('/api/me').flush(null, { status: 403, statusText: 'Forbidden' });
    await expect(response).rejects.toMatchObject({ status: 403 });
    expect(router.navigate).toHaveBeenCalledWith(['/no-access']);
    expect(auth.signOut).not.toHaveBeenCalled();
  });
});
