import { useMemo } from 'react';
import type { Layout } from '../api/types';

/**
 * True when `draft` differs from the stored version (`saved`, its JSON) or was never saved. The draft is serialized
 * only when it changes, not on every render.
 */
export function useDirty(draft: Layout | null, saved: string, neverSaved: boolean): boolean {
  return useMemo(() => draft !== null && (neverSaved || JSON.stringify(draft) !== saved), [draft, saved, neverSaved]);
}
