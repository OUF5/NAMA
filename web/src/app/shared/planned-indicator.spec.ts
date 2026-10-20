import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlannedIndicator } from './planned-indicator';
import { planStatus, planStatusLabel, spendStatus } from './plan-status';

const sep = { key: '2026-09', label: 'Sep 2026', startDate: '2026-09-27', endDate: '2026-10-26' };

describe('planned indicator (ENT-01)', () => {
  it('is green below 80%, amber from 80% to 100% and red above 100%', () => {
    expect(planStatus(150_000, 0)).toBe('ok');
    expect(planStatus(150_000, 119_999)).toBe('ok'); // 79.99%
    expect(planStatus(150_000, 120_000)).toBe('warn'); // 80%
    expect(planStatus(150_000, 150_000)).toBe('warn'); // 100%
    expect(planStatus(150_000, 150_001)).toBe('danger'); // 100.0007%
  });

  // Months (M7): nothing at all has no status; spending without a plan is over plan (R-PLN-01).
  it('gives no status when nothing is planned or spent, and red for spending without a plan', () => {
    expect(spendStatus(0, 0)).toBeNull();
    expect(spendStatus(0, 3_000)).toBe('danger');
    expect(spendStatus(150_000, 0)).toBe('ok');
    expect(spendStatus(150_000, 120_000)).toBe('warn');
  });

  it('labels every status with text', () => {
    expect(planStatusLabel('ok', 150_000, 0, 'SAR')).toBe('Within plan');
    expect(planStatusLabel('warn', 150_000, 150_000, 'SAR')).toBe('Near limit');
    expect(planStatusLabel('danger', 150_000, 160_050, 'SAR')).toBe('Over by SAR 100.50');
    expect(planStatusLabel('danger', 0, 3_000, 'SAR')).toBe('Over by SAR 30');
  });

  async function render(planned: number, spent: number, pending = 0, carriedIn = 0): Promise<ComponentFixture<PlannedIndicator>> {
    const fixture = TestBed.createComponent(PlannedIndicator);
    fixture.componentRef.setInput('period', sep);
    fixture.componentRef.setInput('carriedIn', carriedIn);
    fixture.componentRef.setInput('planned', planned);
    fixture.componentRef.setInput('spent', spent);
    fixture.componentRef.setInput('pending', pending);
    fixture.componentRef.setInput('currency', 'SAR');
    await fixture.whenStable();
    return fixture;
  }

  function text(fixture: ComponentFixture<PlannedIndicator>, selector: string): string | undefined {
    return (fixture.nativeElement as HTMLElement).querySelector(selector)?.textContent?.trim();
  }

  it('shows the month with its dates, planned, spent so far and remaining', async () => {
    const fixture = await render(150_000, 119_999);
    const numbers = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('dd')).map((dd) => dd.textContent);

    expect(text(fixture, '.head .caption')).toBe('Sep 2026 · 27 Sep to 26 Oct');
    expect(numbers).toEqual(['SAR 1,500', 'SAR 1,199.99', 'SAR 300.01']);
    expect(text(fixture, '.chip')).toBe('Within plan');
    expect(text(fixture, '.after')).toBeUndefined();
  });

  it('previews the month after this entry while an amount is typed', async () => {
    const fixture = await render(150_000, 119_999, 1);

    expect(text(fixture, '.chip')).toBe('Near limit');
    expect(text(fixture, '.after')).toBe('After this entry: SAR 300 left');
  });

  it('says how much the plan is over by, with a text label', async () => {
    const fixture = await render(150_000, 140_000, 20_000);

    expect(text(fixture, '.chip')).toBe('Over by SAR 100');
    expect(text(fixture, '.after')).toBe('After this entry: over by SAR 100');
    expect((fixture.nativeElement as HTMLElement).querySelector('mat-progress-bar')?.classList).toContain('danger');
  });

  it('says "No plan for this month" when nothing is planned', async () => {
    const fixture = await render(0, 10_000, 5_000);

    expect(text(fixture, '.no-plan')).toBe('No plan for this month');
    expect(text(fixture, '.no-plan + .caption')).toBe('Spent so far SAR 100, SAR 150 after this entry');
    expect(text(fixture, '.chip')).toBeUndefined();
    expect((fixture.nativeElement as HTMLElement).querySelector('mat-progress-bar')).toBeNull();
  });

  // R-PLN-05: what was left last month carries in; the bar and the chip count it.
  it('counts what was left last month', async () => {
    const fixture = await render(150_000, 170_000, 0, 30_000);
    const numbers = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('dd')).map((dd) => dd.textContent);

    expect(numbers).toEqual(['SAR 1,500', 'SAR 1,700', 'SAR 100']);
    expect(text(fixture, '.carried')).toBe('Includes SAR 300 left last month · SAR 1,800 available');
    expect(text(fixture, '.chip')).toBe('Near limit');
  });

  // R-PLN-05: an overspend carries in too, and is over plan until a plan covers it.
  it('counts an overspend carried in, even without a plan', async () => {
    const fixture = await render(0, 0, 0, -20_000);

    expect(text(fixture, '.no-plan')).toBeUndefined();
    expect(text(fixture, '.carried')).toBe('Less SAR 200 overspent last month · SAR -200 available');
    expect(text(fixture, '.chip')).toBe('Over by SAR 200');
  });
});
