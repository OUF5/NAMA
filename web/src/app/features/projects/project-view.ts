import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom, map } from 'rxjs';
import { MeService } from '../../core/me';
import { dialogSize } from '../../shared/dialog';
import { Info } from '../../shared/info';
import { formatDate } from '../../shared/month-label';
import { ProjectBarView } from './project-bar';
import { ProjectDialog, ProjectDialogData } from './project-dialog';
import { projectDetails } from './project-lines';
import { ProjectsApi, ProjectsResponse } from './projects-api';

const PHONE = '(max-width: 767.98px)';

// PRD 5.14 PRJ-03 (owner note 30): one project, so you see at once where you are, where you will be by
// the deadline, and what to change. The numbers come from GET /api/projects, as on the list.
@Component({
  selector: 'app-project-view',
  imports: [RouterLink, MatButtonModule, MatIconModule, Info, ProjectBarView],
  templateUrl: './project-view.html',
  styleUrl: './projects.scss',
})
export class ProjectView {
  private readonly api = inject(ProjectsApi);
  private readonly meService = inject(MeService);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(BreakpointObserver);
  private readonly router = inject(Router);

  private readonly id = toSignal(inject(ActivatedRoute).paramMap.pipe(map((params) => params.get('id') ?? '')), { initialValue: '' });

  protected readonly data = signal<ProjectsResponse | null>(null);
  protected readonly loadError = signal('');
  protected readonly status = signal('');
  protected readonly how = signal(false);
  protected readonly currency = computed(() => this.meService.me()?.profile?.currency ?? 'SAR');
  protected readonly formatDate = formatDate;

  protected readonly project = computed(() => this.data()?.projects.find((project) => project.id === this.id()) ?? null);

  protected readonly view = computed(() => {
    const project = this.project();
    const data = this.data();
    return project && data ? projectDetails(project, data.accounts, this.currency()) : null;
  });

  constructor() {
    void this.load();
  }

  protected async edit(): Promise<void> {
    const data = this.data();
    const project = this.project();
    if (!data || !project) {
      return;
    }
    this.status.set('');
    const ref = this.dialog.open<ProjectDialog, ProjectDialogData, ProjectsResponse>(ProjectDialog, {
      ...dialogSize(this.breakpoints.isMatched(PHONE)),
      data: { project, data, currency: this.currency() },
    });
    const response = await firstValueFrom(ref.afterClosed());
    if (!response) {
      return;
    }
    if (!response.projects.some((item) => item.id === project.id)) {
      await this.router.navigateByUrl('/projects');
      return;
    }
    this.data.set(response);
    this.status.set('Project saved.');
  }

  private async load(): Promise<void> {
    try {
      await this.meService.load();
      this.data.set(await this.api.projects());
    } catch {
      this.loadError.set('The project could not be loaded. Please reload the page.');
    }
  }
}
