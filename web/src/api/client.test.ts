import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { api, ApiError, getBlob, resetUnauthorizedHandler, setUnauthorizedHandler } from './client';

describe('API client', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it('api throws ApiError with errors[] on 400', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 400,
      headers: new Headers({ 'content-type': 'application/json' }),
      json: async () => ({ errors: ['name is required', 'invalid size'] }),
    } as Response);

    await expect(api('POST', '/api/layouts', {})).rejects.toThrow(ApiError);

    try {
      await api('POST', '/api/layouts', {});
    } catch (err) {
      expect(err).toBeInstanceOf(ApiError);
      const apiErr = err as ApiError;
      expect(apiErr.status).toBe(400);
      expect(apiErr.errors).toEqual(['name is required', 'invalid size']);
    }
  });

  it('api invokes unauthorized handler with current hash on 401', async () => {
    const onUnauthorized = vi.fn();
    setUnauthorizedHandler(onUnauthorized);
    window.location.hash = '#sensors';

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 401,
      headers: new Headers({ 'content-type': 'application/json' }),
      json: async () => ({ error: 'login required' }),
    } as Response);

    await expect(api('GET', '/api/sensors')).rejects.toThrow(ApiError);
    expect(onUnauthorized).toHaveBeenCalledWith('#sensors');
  });

  it('api sends JSON content type', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers({ 'content-type': 'application/json' }),
      json: async () => ({ ok: true }),
    } as Response);
    globalThis.fetch = fetchMock;

    await api('POST', '/api/settings', { unit: 'celsius' });

    expect(fetchMock).toHaveBeenCalledWith('/api/settings', expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({
        'Content-Type': 'application/json',
      }),
      body: JSON.stringify({ unit: 'celsius' }),
    }));
  });
});

describe('API client auth redirect', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
    resetUnauthorizedHandler();
  });

  const respond401 = (error: string) =>
    vi.fn().mockResolvedValue({
      ok: false,
      status: 401,
      headers: new Headers({ 'content-type': 'application/json' }),
      json: async () => ({ error }),
    } as Response);

  it('skipAuthRedirect keeps a 401 out of the unauthorized handler', async () => {
    const onUnauthorized = vi.fn();
    setUnauthorizedHandler(onUnauthorized);
    globalThis.fetch = respond401('wrong password');

    const err = await api('POST', '/api/auth/login', { password: 'x' }, { skipAuthRedirect: true }).catch(
      (e: unknown) => e,
    );

    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).errors).toEqual(['wrong password']);
    expect(onUnauthorized).not.toHaveBeenCalled();
  });

  it('default handler sends the user to login with the encoded current hash', async () => {
    window.location.hash = '#sensors';
    globalThis.fetch = respond401('login required');

    await api('GET', '/api/sensors').catch(() => undefined);

    expect(window.location.hash).toBe('#login?return=%23sensors');
  });

  it('default handler never nests the login page inside its own return', async () => {
    window.location.hash = '#login?return=%23sensors';
    globalThis.fetch = respond401('login required');

    await api('GET', '/api/sensors').catch(() => undefined);

    expect(window.location.hash).toBe('#login?return=%23sensors');
  });
});

describe('getBlob', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
    resetUnauthorizedHandler();
  });

  it('returns the body of a 200 as a Blob', async () => {
    const blob = new Blob(['jpeg']);
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, blob: async () => blob } as Response);

    await expect(getBlob('/api/preview/a.jpg')).resolves.toBe(blob);
  });

  it('throws ApiError with the status on 404 without redirecting', async () => {
    const onUnauthorized = vi.fn();
    setUnauthorizedHandler(onUnauthorized);
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: false, status: 404 } as Response);

    const err = await getBlob('/api/preview/a.jpg').catch((e: unknown) => e);

    expect((err as ApiError).status).toBe(404);
    expect(onUnauthorized).not.toHaveBeenCalled();
  });
});
