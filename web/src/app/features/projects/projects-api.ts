import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PeriodInfo } from '../../shared/month-label';

// PRD 5.14 Projects and 7.5 (owner note 30). Amounts are decimals in JSON (R-MON-01); dates are
// YYYY-MM-DD. Null amounts are unknown until the balances are (BNK-05).

export type ProjectStatus = 'active' | 'paused' | 'done';
export type TargetLevel = 'best' | 'likely' | 'worst';
// R-PRJ-10
export type ProjectState = 'onTrack' | 'offTrack' | 'reached' | 'paused' | 'done' | 'unknown';

// R-PRJ-02: covered is whether the projection reaches it by the deadline (by now once nothing is projected).
export interface ProjectTarget {
  level: TargetLevel;
  amount: number;
  active: boolean;
  covered: boolean | null;
}

// R-PRJ-06: actual is null when unknown; behind below 0 is ahead.
export interface ProjectMonth {
  period: PeriodInfo;
  planned: number;
  actual: number | null;
  behind: number;
}

// R-PRJ-05, R-PRJ-07: a month with its planned amount.
export interface ProjectMonthToCome {
  period: PeriodInfo;
  amount: number;
}

// R-PRJ-08: money is real once passed, else projected; difference at 0 or more is met or on track.
export interface ProjectMilestone {
  id: string;
  label: string;
  date: string;
  amount: number;
  passed: boolean;
  money: number | null;
  difference: number | null;
}

// Everything after state is as of asOf (a Done project's day, R-PRJ-01).
export interface Project {
  id: string;
  name: string;
  deadline: string;
  status: ProjectStatus;
  doneOn: string | null;
  startPeriodKey: string;
  targets: ProjectTarget[];
  activeTarget: TargetLevel;
  bankAccountId: string;
  monthlyAmount: number;
  // Every month from the start month to the deadline, with its amount (R-PRJ-05).
  plan: ProjectMonthToCome[];
  state: ProjectState;
  asOf: string;
  currentMonth: PeriodInfo | null;
  money: number | null;
  progress: number | null;
  target: number;
  stillToSave: number | null;
  percent: number | null;
  months: ProjectMonth[];
  behindBefore: number;
  stillToPutIn: number;
  monthsToCome: ProjectMonthToCome[];
  deadlinePassed: boolean;
  projected: number | null;
  short: number | null;
  extraAMonth: number | null;
  monthsLeft: number;
  milestones: ProjectMilestone[];
}

// The dialog's account picker (PRJ-04): projectId is the project that is not Done counting it.
export interface ProjectAccount {
  id: string;
  name: string;
  archived: boolean;
  balance: number | null;
  projectId: string | null;
}

export interface ProjectsResponse {
  today: string;
  currentMonth: PeriodInfo | null;
  projects: Project[];
  accounts: ProjectAccount[];
}

export interface NewMilestone {
  id: string | null;
  label: string;
  date: string;
  amount: number;
}

// monthly: each month's amount from the start month to the deadline; a month left out is 0 (R-PRJ-05).
export interface NewProject {
  name: string;
  deadline: string;
  best: number | null;
  likely: number | null;
  worst: number | null;
  activeTarget: TargetLevel;
  bankAccountId: string;
  monthly: { periodKey: string; amount: number }[];
  milestones: NewMilestone[];
}

// R-PRJ-01: active resumes or reopens; done needs doneOn.
export interface StatusChange {
  status: ProjectStatus;
  doneOn?: string;
}

// PRJ-04: the dialog's months (from the start month, this month for a new project, to the deadline)
// and the recommended amount a month (R-PRJ-12), null until it can be worked out.
export interface ProjectMonths {
  months: PeriodInfo[];
  currentKey: string;
  recommended: number | null;
}

export interface MonthsQuery {
  deadline: string;
  bankAccountId: string | null;
  likely: number | null;
  projectId: string | null;
}

// Every change answers like GET /api/projects (NFR-01).
@Injectable({ providedIn: 'root' })
export class ProjectsApi {
  private readonly http = inject(HttpClient);

  projects(): Promise<ProjectsResponse> {
    return firstValueFrom(this.http.get<ProjectsResponse>('/api/projects'));
  }

  months(query: MonthsQuery): Promise<ProjectMonths> {
    const params: Record<string, string> = { deadline: query.deadline };
    if (query.bankAccountId) {
      params['bankAccountId'] = query.bankAccountId;
    }
    if (query.likely !== null) {
      params['likely'] = String(query.likely);
    }
    if (query.projectId) {
      params['projectId'] = query.projectId;
    }
    return firstValueFrom(this.http.get<ProjectMonths>('/api/projects/months', { params }));
  }

  add(project: NewProject): Promise<ProjectsResponse> {
    return firstValueFrom(this.http.post<ProjectsResponse>('/api/projects', project));
  }

  update(id: string, project: NewProject): Promise<ProjectsResponse> {
    return firstValueFrom(this.http.put<ProjectsResponse>(`/api/projects/${id}`, project));
  }

  setStatus(id: string, change: StatusChange): Promise<ProjectsResponse> {
    return firstValueFrom(this.http.put<ProjectsResponse>(`/api/projects/${id}/status`, change));
  }

  remove(id: string): Promise<ProjectsResponse> {
    return firstValueFrom(this.http.delete<ProjectsResponse>(`/api/projects/${id}`));
  }
}

export const TARGET_LABELS: Record<TargetLevel, string> = { best: 'Best', likely: 'Likely', worst: 'Worst' };

// R-PRJ-10: the status chip's words and its tone (docs/DESIGN.md status colors).
export function stateChip(state: ProjectState): { label: string; tone: 'ok' | 'danger' | 'neutral' } {
  switch (state) {
    case 'onTrack':
      return { label: 'On track', tone: 'ok' };
    case 'reached':
      return { label: 'Reached', tone: 'ok' };
    case 'offTrack':
      return { label: 'Off track', tone: 'danger' };
    case 'paused':
      return { label: 'Paused', tone: 'neutral' };
    case 'done':
      return { label: 'Done', tone: 'neutral' };
    default:
      return { label: 'Balance not set up', tone: 'neutral' };
  }
}
