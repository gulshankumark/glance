/**
 * Lets a page with unsaved work hold back hash navigation it did not start (Back, a typed hash). The app's router
 * asks the blocker about each hash change; when it returns true the router restores the previous hash and stays.
 */
type Blocker = (nextHash: string) => boolean;

let blocker: Blocker | null = null;

export function setNavBlocker(fn: Blocker | null): void {
  blocker = fn;
}

/** True when the change to `nextHash` must be held back (the blocker then asks the user). */
export function navBlocked(nextHash: string): boolean {
  return blocker?.(nextHash) ?? false;
}
