import type { Layout } from '../api/types';

/**
 * The unsaved draft of the open layout, kept in sessionStorage so it survives leaving the editor without a prompt —
 * above all a 401 that sends the user to the login page and back (it is restored when the Layouts tab opens again).
 */
export interface DraftStash {
  /** The stored layout the draft edits; null for a new layout that was never saved. */
  layoutName: string | null;
  draft: Layout;
  /** JSON of the stored version ('' for a new layout), to tell whether the draft has unsaved changes. */
  savedVersion: string;
}

const KEY = 'glance.layoutDraft';

export function writeStash(stash: DraftStash): void {
  try {
    sessionStorage.setItem(KEY, JSON.stringify(stash));
  } catch {
    // storage full or blocked: the draft is only kept in memory
  }
}

export function readStash(): DraftStash | null {
  try {
    const raw = sessionStorage.getItem(KEY);
    if (!raw) return null;
    const s = JSON.parse(raw) as Partial<DraftStash>;
    const d = s.draft as Partial<Layout> | undefined;
    const ok =
      (s.layoutName === null || typeof s.layoutName === 'string') &&
      typeof s.savedVersion === 'string' &&
      typeof d?.name === 'string' &&
      typeof d.width === 'number' &&
      typeof d.height === 'number' &&
      typeof d.background === 'object' &&
      Array.isArray(d.items);
    return ok ? (s as DraftStash) : null;
  } catch {
    return null;
  }
}

export function clearStash(): void {
  try {
    sessionStorage.removeItem(KEY);
  } catch {
    // nothing to clear
  }
}
