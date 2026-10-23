import { describe, it, expect } from 'vitest';
import { isLoopbackHost, serviceAddress } from './serviceAddress';

describe('serviceAddress', () => {
  it('keeps a remote IPv4 host and applies scheme and port', () => {
    expect(serviceAddress('192.168.1.20', false, 9000)).toBe('http://192.168.1.20:9000/#settings');
    expect(serviceAddress('192.168.1.20', true, 8443)).toBe('https://192.168.1.20:8443/#settings');
  });

  it('keeps an IPv6 literal in brackets', () => {
    expect(serviceAddress('[::1]', true, 8735)).toBe('https://[::1]:8735/#settings');
    expect(serviceAddress('[fe80::1]', false, 1024)).toBe('http://[fe80::1]:1024/#settings');
  });

  it('keeps a host name', () => {
    expect(serviceAddress('my-pc.local', false, 65535)).toBe('http://my-pc.local:65535/#settings');
  });

  it('falls back to localhost for an empty host name', () => {
    expect(serviceAddress('', false, 8735)).toBe('http://localhost:8735/#settings');
  });
});

describe('isLoopbackHost', () => {
  it('accepts localhost, 127.0.0.1 and ::1 with or without brackets', () => {
    for (const host of ['localhost', 'LOCALHOST', '127.0.0.1', '[::1]', '::1']) expect(isLoopbackHost(host)).toBe(true);
  });

  it('rejects other hosts', () => {
    for (const host of ['192.168.1.20', 'my-pc.local', '[fe80::1]', 'glance.test', '']) {
      expect(isLoopbackHost(host)).toBe(false);
    }
  });
});
