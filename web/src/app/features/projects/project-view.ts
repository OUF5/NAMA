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
import { formatMoney, toMinor } from '../../shared/money';
import { formatDate } from '../../shared/month-label';
import { ProjectBarView } from './project-bar';
import { ProjectDialog, ProjectDialogData } from './project-dialog';
import { behindText, deadlineLine, milestoneBar, progressLine, projectBar, verdict } from './project-lines';
import { ProjectsApi, ProjectsResponse, TARGET_LABELS, stateChip } from './projects-api';

const PHONE = '(max-width: 767.98px)';

// PRD 5.14 PRJ-03 (owner note 30): one project, so you see at once where you are, where you will be by
// the deadline, and what to change. The numbers come from GET /api/projects, as on the list.
@Component({
  selector: 'app-project-view',
  imports: [RouterLink, MatButtonModule, MatIconModule, ProjectBarView],
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
    if (!project || !data) {
      return null;
    }
    const money = (amount: number) => this.money(amount);
    const accountName = (id: string) => data.accounts.find((account) => account.id === id)?.name ?? 'An account';
    const done = project.status === 'done';
    const thisMonth = project.months.at(-1);
    const current = !done && thisMonth && project.currentMonth && thisMonth.period.key === project.currentMonth.key ? thisMonth : null;
    const monthsToCome = project.monthsToCome;
    return {
      project,
      chip: stateChip(project.state),
      bar: projectBar(project, this.currency()),
      progressLine: progressLine(project, this.currency()),
      verdict: verdict(project, this.currency()),
      deadline: deadlineLine(project),
      // Now: the money in its account (R-PRJ-03, R-PRJ-04).
      now: project.progress === null ? '–' : money(project.progress),
      nowParts: `In ${accountName(project.bankAccountId)}`,
      // By the deadline (R-PRJ-07).
      projected: project.projected === null ? '–' : money(project.projected),
      projectedCaption: done
        ? `As it was on ${formatDate(project.asOf)}`
        : project.deadlinePassed
          ? 'The deadline has passed'
          : project.status === 'paused'
            ? 'Nothing more while paused'
            : `With ${money(project.stillToPutIn + monthsToCome.reduce((sum, month) => sum + month.amount, 0))} more as planned`,
      stillToSave: project.stillToSave === null ? '–' : money(project.stillToSave),
      // "How?" (R-PRJ-07): progress + still to put in this month + months to come = projected.
      how:
        project.projected === null
          ? null
          : [
              { op: '', label: 'Progress now', value: money(project.progress ?? 0) },
              { op: '+', label: 'Still to put in this month', value: money(project.stillToPutIn) },
              {
                op: '+',
                label: monthsToCome.length
                  ? `${monthsToCome.length} ${monthsToCome.length === 1 ? 'month' : 'months'} to come (${monthsToCome[0].period.label} to ${monthsToCome.at(-1)!.period.label})`
                  : 'No months to come before the deadline',
                value: money(monthsToCome.reduce((sum, month) => sum + month.amount, 0)),
              },
            ],
      targets: project.targets.map((target) => ({
        label: TARGET_LABELS[target.level],
        amount: money(target.amount),
        active: target.active,
        covered: target.covered,
        gap:
          target.covered === false
            ? `Short ${money(target.amount - (project.projected ?? project.progress ?? 0))}`
            : target.covered
              ? 'Covered'
              : '–',
      })),
      // This month (R-PRJ-06), while it is in progress.
      thisMonth: current
        ? {
            month: current.period.label,
            planned: money(current.planned),
            putIn: current.actual === null ? '–' : money(current.actual),
            stillToPutIn: money(project.stillToPutIn),
            before: behindText(project.behindBefore, this.currency()),
          }
        : null,
      // R-PRJ-08: each milestone as a bar of the money against its amount, and what it means.
      milestones: project.milestones.map((milestone) => {
        const difference = milestone.difference;
        const short = difference !== null && difference < 0;
        return {
          id: milestone.id,
          label: milestone.label,
          date: formatDate(milestone.date),
          amount: money(milestone.amount),
          bar: milestoneBar(project, milestone, this.currency()),
          status:
            difference === null
              ? { text: done ? '–' : 'Unknown', tone: 'neutral' as const }
              : milestone.passed
                ? short
                  ? { text: `Missed by ${money(-difference)}`, tone: 'danger' as const }
                  : { text: 'Met', tone: 'ok' as const }
                : short
                  ? { text: `Short ${money(-difference)}`, tone: 'danger' as const }
                  : { text: 'On track', tone: 'ok' as const },
          caption: milestone.passed
            ? milestone.money === null
              ? 'Its account was not known that day'
              : `${money(milestone.money)} in its account that day`
            : [
                project.money === null ? null : `${money(project.money)} now`,
                milestone.money === null ? null : `${money(milestone.money)} projected that day`,
              ]
                .filter((part) => part !== null)
                .join(' · '),
        };
      }),
      // By month (R-PRJ-05, R-PRJ-06): every month of the plan, oldest first; months to come show
      // only what is planned.
      months: project.plan.map((month) => {
        const row = project.months.find((item) => item.period.key === month.period.key);
        return {
          key: month.period.key,
          label: month.period.label,
          planned: money(month.amount),
          actual: !row || row.actual === null ? '–' : money(row.actual),
          behind: !row || row.actual === null ? { text: '–', tone: 'neutral' as const } : behindText(row.behind, this.currency()),
          current: row !== undefined && row === current,
          toCome: !row,
        };
      }),
    };
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

  private money(amount: number): string {
    return formatMoney(toMinor(amount), this.currency());
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
