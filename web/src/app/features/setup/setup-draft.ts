// SET-03: typed wizard values survive a page refresh. One draft per Google account, removed on
// Finish and on sign-out. Storage can be missing or blocked, so every call is guarded.
export interface SetupDraft {
  projectName: string;
  displayName: string;
  currency: string;
  fiscalStartMonth: number | null;
  paydayDay: number | null;
  salary: string;
  additional: { label: string; amount: string; type: 'once' | 'monthly' }[];
  bankAccounts: string[];
  categories: string[];
}

const PREFIX = 'nama.setup.';

export function loadDraft(uid: string): Partial<SetupDraft> | null {
  try {
    const text = localStorage.getItem(PREFIX + uid);
    return text ? (JSON.parse(text) as Partial<SetupDraft>) : null;
  } catch {
    return null;
  }
}

export function saveDraft(uid: string, draft: SetupDraft): void {
  try {
    localStorage.setItem(PREFIX + uid, JSON.stringify(draft));
  } catch {
    // Without storage the wizard still works; only the refresh survival is lost.
  }
}

export function clearDraft(uid: string): void {
  try {
    localStorage.removeItem(PREFIX + uid);
  } catch {
    // Nothing to clear.
  }
}

// On sign-out: no draft stays behind on a shared device.
export function clearAllDrafts(): void {
  try {
    Object.keys(localStorage)
      .filter((key) => key.startsWith(PREFIX))
      .forEach((key) => localStorage.removeItem(key));
  } catch {
    // Nothing to clear.
  }
}
