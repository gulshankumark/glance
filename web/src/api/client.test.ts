import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { api, ApiError, setUnauthorizedHandler } from './client';

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
