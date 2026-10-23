/**
 * The hash to return to after logging in, from a `#login?return=<encoded hash>` route. Only an in-app hash is
 * accepted (starts with "#", is not the login page itself); anything else returns to `#display`.
 */
export function returnHashFrom(loginHash: string): string {
  const queryIdx = loginHash.indexOf('?');
  // URLSearchParams decodes the value once; it must not be decoded again.
  const value = queryIdx === -1 ? null : new URLSearchParams(loginHash.slice(queryIdx + 1)).get('return');
  if (!value || !value.startsWith('#') || value.startsWith('#login')) return '#display';
  return value;
}
