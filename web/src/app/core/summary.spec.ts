import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { Me, MeService } from './me';
import { Summary, SummaryService, summaryInterceptor } from './summary';

// UX-08: the top bar's numbers load again after every change, once a change settles.
describe('SummaryService', () => {
  const summary: Summary = { balance: 100, complete: true, accounts: [], month: null };
  const me = signal<Me | null>(null);
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    me.set({ setupCompleted: true } as Me);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([summaryInterceptor])),
        provideHttpClientTesting(),
        { provide: MeService, useValue: { me } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  it('loads again once after changes, never after reads', async () => {
    const client = TestBed.inject(HttpClient);
    const service = TestBed.inject(SummaryService);

    void firstValueFrom(client.get('/api/banks'));
    http.expectOne('/api/banks').flush({});
    void firstValueFrom(client.post('/api/entries', {}));
    http.expectOne('/api/entries').flush({});
    void firstValueFrom(client.put('/api/plans/x', {}));
    http.expectOne('/api/plans/x').flush({});
    await vi.advanceTimersByTimeAsync(300);

    http.expectOne('/api/summary').flush(summary);
    await vi.advanceTimersByTimeAsync(0);
    expect(service.summary()).toEqual(summary);
  });

  it('does not load after a failed change, or before setup', async () => {
    const client = TestBed.inject(HttpClient);

    void firstValueFrom(client.post('/api/entries', {})).catch(() => undefined);
    http.expectOne('/api/entries').flush({}, { status: 400, statusText: 'Bad Request' });
    await vi.advanceTimersByTimeAsync(300);
    http.expectNone('/api/summary');

    me.set({ setupCompleted: false } as Me);
    void firstValueFrom(client.post('/api/invites/accept', {}));
    http.expectOne('/api/invites/accept').flush({});
    await vi.advanceTimersByTimeAsync(300);
    http.expectNone('/api/summary');
  });
});
