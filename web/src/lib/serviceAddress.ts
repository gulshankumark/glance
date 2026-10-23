/**
 * The Settings tab's address after a network change: same host (`location.hostname`, an IPv6 literal keeps its
 * brackets; empty → localhost), scheme from `https`, the given port.
 */
export function serviceAddress(hostname: string, https: boolean, port: number): string {
  return `${https ? 'https' : 'http'}://${hostname || 'localhost'}:${port}/#settings`;
}

/** Whether a `location.hostname` names this computer (localhost, 127.0.0.1, ::1 with or without brackets). */
export function isLoopbackHost(hostname: string): boolean {
  return ['localhost', '127.0.0.1', '[::1]', '::1'].includes(hostname.toLowerCase());
}
