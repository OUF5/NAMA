import { formatMoney } from './money';

// The plan status rules, in a file of their own (no Angular), so the shell's top bar can use them
// without the indicator component in the initial bundle.
export type PlanStatus = 'ok' | 'warn' | 'danger';

// ENT-01 colors, in exact minor-unit math: green below 80%, amber from 80% to 100%, red above 100%.
// R-PLN-05: "available" is the month's plan plus what carried in from the month before (it can be
// below 0 when an overspend carried in).
export function planStatus(available: number, spent: number): PlanStatus {
  if (spent * 100 < available * 80) {
    return 'ok';
  }
  return spent <= available ? 'warn' : 'danger';
}

// Months (M7): null when nothing is available and nothing is spent. Spending without a plan is
// over plan, because a missing plan counts as 0 (R-PLN-01, owner's choice).
export function spendStatus(available: number, spent: number): PlanStatus | null {
  return available === 0 && spent === 0 ? null : planStatus(available, spent);
}

// docs/DESIGN.md chips: always a text label, never color alone.
export function planStatusLabel(status: PlanStatus, available: number, spent: number, currency: string): string {
  switch (status) {
    case 'ok':
      return 'Within plan';
    case 'warn':
      return 'Near limit';
    default:
      return `Over by ${formatMoney(spent - available, currency)}`;
  }
}
