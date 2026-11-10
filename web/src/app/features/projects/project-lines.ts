import { formatAmount, formatMoney, toMinor } from '../../shared/money';
import { formatDate } from '../../shared/month-label';
import { Project, ProjectAccount, ProjectMilestone, TARGET_LABELS, stateChip } from './projects-api';

// PRD 5.14 (owner note 30): what a project says, the same on the list (PRJ-02), on its page (PRJ-03)
// and on Dashboard (DSH-08). Amounts arrive as decimals and are shown from minor units (R-MON-01).

export type Tone = 'ok' | 'danger' | 'neutral';

// The bar: progress against the active target, in the status tone. fill is a percent of the width.
export interface ProjectBar {
  fill: number;
  tone: Tone;
  label: string;
}

function percent(part: number, whole: number): number {
  return whole <= 0 ? 0 : Math.max(0, Math.min(100, (part / whole) * 100));
}

export function projectBar(project: Project, currency: string): ProjectBar {
  const target = toMinor(project.target);
  const progress = project.progress === null ? 0 : toMinor(project.progress);
  return {
    fill: percent(progress, target),
    tone: stateChip(project.state).tone,
    label:
      project.progress === null
        ? 'Progress unknown'
        : `${formatMoney(progress, currency)} of ${formatMoney(target, currency)}`,
  };
}

// R-PRJ-08: a milestone's bar is the money (on its day once passed, today before) against its amount.
export function milestoneBar(project: Project, milestone: ProjectMilestone, currency: string): ProjectBar {
  const money = milestone.passed ? milestone.money : project.money;
  const amount = toMinor(milestone.amount);
  const tone: Tone = milestone.difference === null ? 'neutral' : milestone.difference < 0 ? 'danger' : 'ok';
  return {
    fill: money === null ? 0 : percent(toMinor(money), amount),
    tone,
    label: money === null ? 'Money unknown' : `${formatMoney(toMinor(money), currency)} of ${formatMoney(amount, currency)}`,
  };
}

// "SAR 65,375 of 144,000 · 45% · Likely"
export function progressLine(project: Project, currency: string): string {
  const target = `${formatAmount(toMinor(project.target))} · ${TARGET_LABELS[project.activeTarget]}`;
  if (project.progress === null) {
    return `Target ${currency} ${target}`;
  }
  return `${formatMoney(toMinor(project.progress), currency)} of ${formatAmount(toMinor(project.target))} · ${project.percent ?? 0}% · ${TARGET_LABELS[project.activeTarget]}`;
}

// What the numbers mean, in one line (R-PRJ-07, R-PRJ-08, R-PRJ-10).
export function verdict(project: Project, currency: string): { text: string; tone: Tone } {
  const money = (amount: number) => formatMoney(toMinor(amount), currency);
  switch (project.state) {
    case 'unknown':
      return { text: 'Set the balance of its account on Banks to see where it stands.', tone: 'neutral' };
    case 'done':
      return { text: `Done on ${formatDate(project.doneOn ?? project.asOf)}`, tone: 'neutral' };
    case 'paused':
      return { text: 'Paused: nothing more is planned for it until you resume it.', tone: 'neutral' };
    case 'reached':
      return { text: 'Reached: the target is saved.', tone: 'ok' };
  }
  if (project.deadlinePassed) {
    return { text: `The deadline has passed, ${money(project.stillToSave ?? 0)} short.`, tone: 'danger' };
  }
  if ((project.short ?? 0) > 0) {
    const extra = project.extraAMonth ?? 0;
    return project.monthsLeft > 1
      ? { text: `Short ${money(project.short!)}: put in ${money(extra)} more a month.`, tone: 'danger' }
      : { text: `Short ${money(project.short!)}: put it in this month.`, tone: 'danger' };
  }
  const milestone = project.milestones.find((item) => !item.passed && (item.difference ?? 0) < 0);
  if (milestone) {
    return {
      text: `${milestone.label} on ${formatDate(milestone.date)} is short ${money(-(milestone.difference ?? 0))}.`,
      tone: 'danger',
    };
  }
  const spare = (project.projected ?? 0) - project.target;
  return { text: spare > 0 ? `On track, with ${money(spare)} to spare.` : 'On track.', tone: 'ok' };
}

// "By 30 Aug 2027 · 11 months left" (this month counted, R-PRJ-07).
export function deadlineLine(project: Project): string {
  const by = `By ${formatDate(project.deadline)}`;
  if (project.status === 'done') {
    return by;
  }
  if (project.deadlinePassed) {
    return `${by} · passed`;
  }
  return project.monthsLeft <= 1 ? `${by} · this month` : `${by} · ${project.monthsLeft} months left`;
}

// R-PRJ-06: "SAR 800 behind" or "SAR 300 ahead".
export function behindText(behind: number, currency: string): { text: string; tone: Tone } {
  const amount = formatMoney(toMinor(Math.abs(behind)), currency);
  if (behind > 0) {
    return { text: `${amount} behind`, tone: 'danger' };
  }
  return behind < 0 ? { text: `${amount} ahead`, tone: 'ok' } : { text: 'On plan', tone: 'neutral' };
}

export type ProjectDetails = ReturnType<typeof projectDetails>;

// PRJ-03, DSH-08: everything a project's page says, so Dashboard says the same.
export function projectDetails(project: Project, accounts: ProjectAccount[], currency: string) {
  const money = (amount: number) => formatMoney(toMinor(amount), currency);
  const accountName = (id: string) => accounts.find((account) => account.id === id)?.name ?? 'An account';
  const done = project.status === 'done';
  const thisMonth = project.months.at(-1);
  const current = !done && thisMonth && project.currentMonth && thisMonth.period.key === project.currentMonth.key ? thisMonth : null;
  const monthsToCome = project.monthsToCome;
  return {
    project,
    chip: stateChip(project.state),
    bar: projectBar(project, currency),
    progressLine: progressLine(project, currency),
    verdict: verdict(project, currency),
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
          before: behindText(project.behindBefore, currency),
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
        bar: milestoneBar(project, milestone, currency),
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
    // only what is planned. The project's page only (DSH-08 leaves it out).
    months: project.plan.map((month) => {
      const row = project.months.find((item) => item.period.key === month.period.key);
      return {
        key: month.period.key,
        label: month.period.label,
        planned: money(month.amount),
        actual: !row || row.actual === null ? '–' : money(row.actual),
        behind: !row || row.actual === null ? { text: '–', tone: 'neutral' as const } : behindText(row.behind, currency),
        current: row !== undefined && row === current,
        toCome: !row,
      };
    }),
  };
}
