import { UrlMatchResult, UrlSegment } from '@angular/router';

// The Settings sections (PRD 5.7), each on its own page: /settings/<id>. Desktop lists them under
// Settings in the side bar; phones open /settings as a list (owner's choice, M8). Sign out (AUTH-04)
// is outside Settings, in the shell. Bank accounts are on Banks (BNK-02, 20 Oct 2026), and categories
// on Plan's Categories tab (owner note 9).
export const SETTINGS_SECTIONS = [
  { id: 'profile', label: 'Profile', icon: 'person' },
  { id: 'fiscal-year', label: 'Fiscal year', icon: 'event' },
  { id: 'income', label: 'Income', icon: 'payments' },
  { id: 'people', label: 'People', icon: 'group' },
] as const;

export type SettingsSectionId = (typeof SETTINGS_SECTIONS)[number]['id'];

// STG-07: People is shown to admins only (AUTH-07).
export function visibleSections(isAdmin: boolean): readonly (typeof SETTINGS_SECTIONS)[number][] {
  return SETTINGS_SECTIONS.filter((section) => isAdmin || section.id !== 'people');
}

// /settings and /settings/<section> are one route, so the page (and its one GET /api/settings)
// stays when you move between sections.
export function settingsMatcher(segments: UrlSegment[]): UrlMatchResult | null {
  if (segments[0]?.path !== 'settings' || segments.length > 2) {
    return null;
  }
  return { consumed: segments, posParams: segments[1] ? { section: segments[1] } : {} };
}
