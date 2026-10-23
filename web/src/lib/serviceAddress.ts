/**
 * The Settings tab's address after a network change: same host (`location.hostname`, an IPv6 literal keeps its
 * brackets; empty → localhost), scheme from `https`, the given port.
 */
export function serviceAddress(hostname: string, https: boolean, port: number): string {
  return `${https ? 'https' : 'http'}://${hostname || 'localhost'}:${port}/#settings`;
}
