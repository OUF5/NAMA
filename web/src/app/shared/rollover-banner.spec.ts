import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Me, MeService } from '../core/me';
import { Rollover, RolloverBanner } from './rollover-banner';

// FY-01, FY-02: FY 2026 runs 27 Jan 2026 to 26 Jan 2027; its last month starts 27 Dec 2026.
const rollover: Rollover = {
  fyId: '2026-01',
  label: 'FY 2026',
  endDate: '2027-01-26',
  nextLabel: 'FY 2027',
  nextStartDate: '2027-01-27',
};

function me(today: string): Me {
  return {
    setupCompleted: true,
    isAdmin: false,
    profile: null,
    currentFyId: '2026-01',
    currentPeriodKey: '2026-12',
    currentPeriod: null,
    today,
    firstDate: '2026-01-27',
    lastDate: '2027-01-26',
  };
}

describe('RolloverBanner (FY-01)', () => {
  let http: HttpTestingController;
  let meService: { me: ReturnType<typeof signal<Me | null>>; set: ReturnType<typeof vi.fn> };

  function create(today: string): ComponentFixture<RolloverBanner> {
    meService = { me: signal<Me | null>(me(today)), set: vi.fn() };
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MeService, useValue: meService }],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(RolloverBanner);
    fixture.componentRef.setInput('rollover', rollover);
    fixture.detectChanges();
    return fixture;
  }

  function text(fixture: ComponentFixture<RolloverBanner>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ').trim() ?? '';
  }

  function startButton(fixture: ComponentFixture<RolloverBanner>): HTMLButtonElement {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Start FY 2027'),
    )!;
  }

  afterEach(() => http.verify());

  it('says the FY ends during its last month, and ended after it', () => {
    expect(text(create('2026-12-27'))).toContain('FY 2026 ends on 26 Jan 2027.');
    TestBed.resetTestingModule();
    expect(text(create('2027-01-27'))).toContain('FY 2026 ended on 26 Jan 2027.');
  });

  // FY-02: the checkbox is on by default; the new /api/me goes into MeService.
  it('starts the next FY with the plan copy on by default', async () => {
    const fixture = create('2026-12-27');
    const started = vi.fn();
    fixture.componentInstance.started.subscribe(started);
    expect((fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[type=checkbox]')!.checked).toBe(true);

    startButton(fixture).click();
    const post = http.expectOne('/api/fiscal-years/next');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ copyPlans: true });
    const response = { fiscalYear: { id: '2027-01', label: 'FY 2027' }, copiedPlans: 2, me: me('2026-12-27') };
    post.flush(response);
    await fixture.whenStable();

    expect(meService.set).toHaveBeenCalledWith(response.me);
    expect(started).toHaveBeenCalledWith(response);
  });

  it('sends copyPlans false when the box is cleared, and shows an error when it fails', async () => {
    const fixture = create('2026-12-27');
    (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[type=checkbox]')!.click();
    fixture.detectChanges();

    startButton(fixture).click();
    const post = http.expectOne('/api/fiscal-years/next');
    expect(post.request.body).toEqual({ copyPlans: false });
    post.flush(null, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('FY 2027 could not be started.');
    expect(meService.set).not.toHaveBeenCalled();
  });
});
