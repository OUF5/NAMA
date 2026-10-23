import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FormGroup } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { CalendarDialog } from './calendar-dialog';
import { Calendar } from './settings-api';

const calendar: Calendar = {
  fiscalStartMonth: 1,
  paydayDay: 25,
  monthNaming: 'end',
  monthKeyOffset: 1,
  takesEffectFrom: '2027-01-25',
  nextFiscalYear: { id: '2027-01', label: 'FY 2027', startDate: '2027-01-25', endDate: '2028-01-24', isTransition: false, periods: [] },
};

// STG-02 with R-CAL-11 (owner note 19): the month names are chosen with the start month and payday.
describe('CalendarDialog (STG-02)', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  beforeEach(() => {
    close.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: calendar },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('saves the month naming and says it applies right away', async () => {
    const fixture = TestBed.createComponent(CalendarDialog);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('the salary pays for the month ahead');
    expect(root.textContent).toContain('Month names change right away for every month. Dates and amounts stay the same.');

    const form = (fixture.componentInstance as unknown as { form: FormGroup }).form;
    form.patchValue({ monthNaming: 'payday' });
    await fixture.whenStable();
    expect(root.textContent).toContain('Paid on 25 Oct, the month is October.');
    (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();

    const put = http.expectOne('/api/settings/calendar');
    expect(put.request.body).toEqual({ fiscalStartMonth: 1, paydayDay: 25, monthNaming: 'payday' });
    const saved = { ...calendar, monthNaming: 'payday', monthKeyOffset: 0 };
    put.flush(saved);
    await vi.waitFor(() => expect(close).toHaveBeenCalledWith(saved));
  });
});
