import { Component, input } from '@angular/core';
import { ProjectBar } from './project-lines';

// PRJ-02, PRJ-03: progress against the active target (or a milestone's amount), in the status color.
// docs/DESIGN.md "Projects".
@Component({
  selector: 'app-project-bar',
  template: `
    <div class="bar" role="img" [attr.aria-label]="bar().label">
      <span
        class="fill"
        [class.ok]="bar().tone === 'ok'"
        [class.danger]="bar().tone === 'danger'"
        [style.width.%]="bar().fill"
      ></span>
    </div>
  `,
  styles: `
    .bar {
      overflow: hidden;
      height: 8px;
      border-radius: 4px;
      background: var(--track);
    }

    .fill {
      display: block;
      height: 100%;
      border-radius: 4px;
      background: var(--text-2);
    }

    .fill.ok {
      background: var(--ok);
    }

    .fill.danger {
      background: var(--danger);
    }
  `,
})
export class ProjectBarView {
  readonly bar = input.required<ProjectBar>();
}
