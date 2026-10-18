import { UrlMatchResult, UrlSegment } from '@angular/router';

// The Settings sections (PRD 5.7), each on its own page: /settings/<id>. Desktop lists them under
// Settings in the side bar; phones open /settings as a list (owner's choice, M8). Sign out (AUTH-04)
// is outside Settings, in the shell.
export const SETTINGS_SECTIONS = [
  { id: 'profile', label: 'Profile', icon: 'person' },
  { id: 'fiscal-year', label: 'Fiscal year', icon: 'event' },
  { id: 'income', label: 'Income', icon: 'payments' },
  { id: 'bank-accounts', label: 'Bank accounts', icon: 'account_balance' },
  { id: 'categories', label: 'Categories', icon: 'category' },
] as const;

export type SettingsSectionId = (typeof SETTINGS_SECTIONS)[number]['id'];

// /settings and /settings/<section> are one route, so the page (and its one GET /api/settings)
// stays when you move between sections.
export function settingsMatcher(segments: UrlSegment[]): UrlMatchResult | null {
  if (segments[0]?.path !== 'settings' || segments.length > 2) {
    return null;
  }
  return { consumed: segments, posParams: segments[1] ? { section: segments[1] } : {} };
}
