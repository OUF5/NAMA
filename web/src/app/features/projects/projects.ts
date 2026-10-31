import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { MeService } from '../../core/me';
import { dialogSize } from '../../shared/dialog';
import { ProjectBarView } from './project-bar';
import { ProjectDialog, ProjectDialogData } from './project-dialog';
import { deadlineLine, progressLine, projectBar, verdict } from './project-lines';
import { Project, ProjectsApi, ProjectsResponse, stateChip } from './projects-api';

const PHONE = '(max-width: 767.98px)';

// PRD 5.14 PRJ-02 (owner note 30): every project with its status, progress and gap on one screen,
// and nothing else (owner, 31 Oct 2026). One call (GET /api/projects); the dialog answers with it.
@Component({
  selector: 'app-projects',
  imports: [RouterLink, MatButtonModule, MatIconModule, ProjectBarView],
  templateUrl: './projects.html',
  styleUrl: './projects.scss',
})
export class Projects {
  private readonly api = inject(ProjectsApi);
  private readonly meService = inject(MeService);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(BreakpointObserver);

  protected readonly data = signal<ProjectsResponse | null>(null);
  protected readonly loadError = signal('');
  protected readonly status = signal('');
  protected readonly showDone = signal(false);
  protected readonly currency = computed(() => this.meService.me()?.profile?.currency ?? 'SAR');

  constructor() {
    void this.load();
  }

  protected readonly cards = computed(() => {
    const data = this.data();
    if (!data) {
      return { open: [], done: [] };
    }
    const card = (project: Project) => ({
      project,
      chip: stateChip(project.state),
      bar: projectBar(project, this.currency()),
      progress: progressLine(project, this.currency()),
      verdict: verdict(project, this.currency()),
      deadline: deadlineLine(project),
    });
    return {
      open: data.projects.filter((project) => project.status !== 'done').map(card),
      done: data.projects.filter((project) => project.status === 'done').map(card),
    };
  });

  protected async add(): Promise<void> {
    const data = this.data();
    if (!data) {
      return;
    }
    this.status.set('');
    const ref = this.dialog.open<ProjectDialog, ProjectDialogData, ProjectsResponse>(ProjectDialog, {
      ...dialogSize(this.breakpoints.isMatched(PHONE)),
      data: { project: null, data, currency: this.currency() },
    });
    const response = await firstValueFrom(ref.afterClosed());
    if (response) {
      this.data.set(response);
      this.status.set('Project added.');
    }
  }

  private async load(): Promise<void> {
    try {
      await this.meService.load();
      this.data.set(await this.api.projects());
    } catch {
      this.loadError.set('Projects could not be loaded. Please reload the page.');
    }
  }
}
