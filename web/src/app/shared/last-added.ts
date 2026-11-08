import { Component, input } from '@angular/core';

// ENT-05 (owner note 35): one line of what was added last.
export interface LastRow {
  caption: string;
  detail: string;
  amount: string;
}

// ENT-05: "Last in Food › Groceries" over its date and account, with the amount on the right, in Add's
// panels (docs/DESIGN.md "Add form"). The panels make the rows; with none it takes no room.
@Component({
  selector: 'app-last-added',
  template: `
    @if (rows().length) {
      <div class="last" aria-live="polite">
        @for (row of rows(); track row.caption) {
          <div class="line">
            <span class="who">
              <span class="caption" [title]="row.caption">{{ row.caption }}</span>
              <span class="detail" [title]="row.detail">{{ row.detail }}</span>
            </span>
            <span class="money">{{ row.amount }}</span>
          </div>
        }
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
      width: 100%;
      text-align: left;
    }

    .last {
      margin-top: 12px;
      padding-top: 8px;
      border-top: 1px solid var(--divider);
    }

    .line {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      min-height: 44px;
    }

    .who {
      display: flex;
      flex-direction: column;
      min-width: 0;
    }

    .caption,
    .detail {
      overflow: hidden;
      white-space: nowrap;
      text-overflow: ellipsis;
    }

    .detail {
      font-size: 13px;
      color: var(--text-2);
    }

    .money {
      white-space: nowrap;
    }
  `,
})
export class LastAdded {
  readonly rows = input.required<LastRow[]>();
}
