import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Info } from './info';

// UX-09 (owner note 37): the ⓘ's panel says what the words mean, from the glossary, and closes on a
// tap outside, on Escape or on the ⓘ again.
@Component({
  imports: [Info],
  template: `
    <h1>Title <app-info title="Year view" [terms]="['available', 'balance']" [notes]="notes" /></h1>
    <p class="outside">Elsewhere</p>
  `,
})
class Host {
  notes = ['Tap a cell to see its entries.'];
}

// A panel of notes only (Settings' sections): no list.
@Component({
  imports: [Info],
  template: `<h2>Invite links <app-info title="Invite links" [notes]="['Each link works once and expires after 7 days.']" /></h2>`,
})
class NotesHost {}

describe('Info (UX-09)', () => {
  let fixture: ComponentFixture<Host>;

  beforeEach(async () => {
    fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
  });

  function button(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('app-info button');
  }

  function panel(): HTMLElement | null {
    return document.querySelector('.info-panel');
  }

  async function click(el: HTMLElement): Promise<void> {
    el.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true }));
    el.click();
    await fixture.whenStable();
  }

  function words(el: Element | null): string {
    return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  }

  it('opens a panel with the glossary bullets and the notes', async () => {
    expect(button().getAttribute('aria-label')).toBe('Year view: what it means');
    expect(button().getAttribute('aria-expanded')).toBe('false');
    expect(panel()).toBeNull();

    await click(button());

    expect(button().getAttribute('aria-expanded')).toBe('true');
    const open = panel()!;
    expect(open.getAttribute('role')).toBe('dialog');
    expect(button().getAttribute('aria-controls')).toBe(open.id);
    expect(open.getAttribute('aria-labelledby')).toBe(`${open.id}-title`);
    expect(words(open.querySelector('.info-title'))).toBe('Year view');
    expect(Array.from(open.querySelectorAll('li')).map(words)).toEqual([
      "Available: the month's plan plus what was left (or overspent) in the FY's earlier months.",
      'Balance: the money in your accounts at the end of the month, projected from today on.',
    ]);
    expect(Array.from(open.querySelectorAll('.note')).map(words)).toEqual(['Tap a cell to see its entries.']);
  });

  it('closes on the ⓘ again', async () => {
    await click(button());
    await click(button());
    expect(panel()).toBeNull();
    expect(button().getAttribute('aria-expanded')).toBe('false');
  });

  it('stays open on a tap inside and closes on a tap outside', async () => {
    await click(button());
    await click(panel()!.querySelector('li')!);
    expect(panel()).not.toBeNull();

    await click(fixture.nativeElement.querySelector('.outside'));
    expect(panel()).toBeNull();
  });

  it('shows notes only when it names no terms', async () => {
    const notes = TestBed.createComponent(NotesHost);
    await notes.whenStable();
    (notes.nativeElement.querySelector('app-info button') as HTMLButtonElement).click();
    await notes.whenStable();
    const open = Array.from(document.querySelectorAll('.info-panel')).at(-1)!;
    expect(open.querySelector('ul')).toBeNull();
    expect(words(open.querySelector('.note'))).toBe('Each link works once and expires after 7 days.');
  });

  it('closes on Escape and gives the focus back to the ⓘ', async () => {
    await click(button());
    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await fixture.whenStable();
    expect(panel()).toBeNull();
    expect(document.activeElement).toBe(button());
  });
});
