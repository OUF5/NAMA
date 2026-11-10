import { CdkConnectedOverlay, CdkOverlayOrigin, ConnectedPosition } from '@angular/cdk/overlay';
import { Component, ElementRef, computed, input, signal, viewChild } from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { GLOSSARY, GlossaryKey } from './glossary';

let nextId = 0;

// Under the button, its start edge first; above it when there is no room below.
const POSITIONS: ConnectedPosition[] = [
  { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top', offsetY: 4 },
  { originX: 'end', originY: 'bottom', overlayX: 'end', overlayY: 'top', offsetY: 4 },
  { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom', offsetY: -4 },
];

// UX-09 (owner note 37), docs/DESIGN.md "What the words mean": an ⓘ after a title opens a small
// panel with one bullet per term the view shows (shared/glossary.ts) and a note or two. A tap
// outside, Escape or the ⓘ again closes it. The CDK overlay is Material's own, so no new library.
@Component({
  selector: 'app-info',
  imports: [CdkConnectedOverlay, CdkOverlayOrigin, MatIconButton, MatIcon],
  template: `
    <button
      #button
      mat-icon-button
      type="button"
      class="info-button"
      cdkOverlayOrigin
      #origin="cdkOverlayOrigin"
      [attr.aria-label]="title() + ': what it means'"
      [attr.aria-expanded]="open()"
      [attr.aria-controls]="open() ? id : null"
      (click)="open.set(!open())"
    >
      <mat-icon>info</mat-icon>
    </button>
    <ng-template
      cdkConnectedOverlay
      [cdkConnectedOverlayOrigin]="origin"
      [cdkConnectedOverlayOpen]="open()"
      [cdkConnectedOverlayPositions]="positions"
      [cdkConnectedOverlayPush]="true"
      [cdkConnectedOverlayViewportMargin]="16"
      (overlayOutsideClick)="outsideClick($event)"
      (overlayKeydown)="keydown($event)"
      (detach)="open.set(false)"
    >
      <div class="info-panel" role="dialog" [id]="id" [attr.aria-labelledby]="id + '-title'">
        <p class="info-title" [id]="id + '-title'">{{ title() }}</p>
        @if (entries().length) {
          <ul>
            @for (entry of entries(); track entry.term) {
              <li><span class="term">{{ entry.term }}:</span> {{ entry.meaning }}</li>
            }
          </ul>
        }
        @for (note of notes(); track note) {
          <p class="note">{{ note }}</p>
        }
      </div>
    </ng-template>
  `,
  styles: `
    :host {
      display: inline-flex;
      vertical-align: middle;
    }

    // The 44px touch target overflows the title's line instead of making it taller.
    .info-button {
      margin: -10px 0 -10px -6px;
      color: var(--text-muted);

      &:hover,
      &[aria-expanded='true'] {
        color: var(--text);
      }
    }

    .info-panel {
      box-sizing: border-box;
      width: min(360px, calc(100vw - 32px));
      padding: 16px;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 8px;
      box-shadow: var(--shadow-overlay);
      font-size: 14px;
      line-height: 1.5;
      color: var(--text-2);
    }

    .info-title {
      margin: 0 0 8px;
      font-weight: 600;
      color: var(--text);
    }

    ul {
      margin: 0;
      padding-left: 18px;
    }

    li + li {
      margin-top: 6px;
    }

    .term {
      font-weight: 500;
      color: var(--text);
    }

    // Notes after the bullets as captions; a panel of notes only reads them as its body.
    .note {
      margin: 12px 0 0;
      font-size: 13px;
      color: var(--text-muted);
    }

    .info-title + .note {
      margin-top: 0;
      font-size: 14px;
      color: var(--text-2);
    }

    .note + .note {
      margin-top: 6px;
    }
  `,
})
export class Info {
  // The view's name, e.g. "Year view"; the button's label says "Year view: what it means".
  readonly title = input.required<string>();
  readonly terms = input<readonly GlossaryKey[]>([]);
  readonly notes = input<readonly string[]>([]);

  protected readonly open = signal(false);
  protected readonly id = `info-${nextId++}`;
  protected readonly positions = POSITIONS;
  protected readonly entries = computed(() => this.terms().map((key) => GLOSSARY[key]));

  private readonly button = viewChild.required<string, ElementRef<HTMLElement>>('button', { read: ElementRef });

  // A tap on the ⓘ itself is outside the panel too; its own click toggles it.
  protected outsideClick(event: MouseEvent): void {
    if (!this.button().nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  protected keydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.open.set(false);
      this.button().nativeElement.focus();
    }
  }
}
