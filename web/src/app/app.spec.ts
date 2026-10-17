import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { Me, MeService } from './core/me';

describe('App', () => {
  const me = signal<Me | null>(null);

  beforeEach(async () => {
    me.set({ setupCompleted: true, profile: null, currentFyId: null, currentPeriodKey: null });
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), { provide: MeService, useValue: { me } }],
    }).compileComponents();
  });

  it('should show NAMA in the top bar', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.brand')?.textContent).toContain('NAMA');
  });

  it('should show the five navigation items (UX-01)', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    const labels = Array.from(compiled.querySelectorAll('.nav-item span')).map((el) => el.textContent?.trim());
    expect(labels).toEqual(['Dashboard', 'Months', 'Add', 'Entries', 'Plan']);
  });

  it('should hide navigation and settings when signed out or not allowed', async () => {
    me.set(null);
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.brand')?.textContent).toContain('NAMA');
    expect(compiled.querySelector('.nav')).toBeNull();
    expect(compiled.querySelector('[aria-label="Settings"]')).toBeNull();
  });

  it('should hide navigation and settings until setup is complete', async () => {
    me.set({ setupCompleted: false, profile: null, currentFyId: null, currentPeriodKey: null });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.nav')).toBeNull();
    expect(compiled.querySelector('[aria-label="Settings"]')).toBeNull();
  });
});
