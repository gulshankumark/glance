import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { api, ApiError, getBlob, postBlob, resetUnauthorizedHandler, setUnauthorizedHandler, upload } from './client';

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
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ ok: true }), { headers: { 'content-type': 'application/json' } }),
    );
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

describe('postBlob', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it('POSTs JSON with the abort signal and returns the body as a Blob', async () => {
    const blob = new Blob(['jpeg']);
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 200, blob: async () => blob } as Response);
    globalThis.fetch = fetchMock;
    const ctrl = new AbortController();

    await expect(postBlob('/api/render', { name: 'A' }, { signal: ctrl.signal })).resolves.toBe(blob);
    expect(fetchMock).toHaveBeenCalledWith('/api/render', expect.objectContaining({
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: 'A' }),
      signal: ctrl.signal,
    }));
  });

  it('throws ApiError with the server errors on 400', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 400,
      headers: new Headers({ 'content-type': 'application/json' }),
      json: async () => ({ errors: ["item 'a': sensor is required"] }),
    } as Response);

    const err = await postBlob('/api/render', {}).catch((e: unknown) => e);
    expect((err as ApiError).errors).toEqual(["item 'a': sensor is required"]);
  });
});

describe('api signal', () => {
  const originalFetch = globalThis.fetch;
  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it('passes the abort signal to fetch', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204, headers: new Headers() } as Response);
    globalThis.fetch = fetchMock;
    const ctrl = new AbortController();
    await api('POST', '/api/render/bounds', {}, { signal: ctrl.signal });
    expect(fetchMock).toHaveBeenCalledWith('/api/render/bounds', expect.objectContaining({ signal: ctrl.signal }));
  });
});

describe('empty answers', () => {
  const originalFetch = globalThis.fetch;
  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it('a 200 with an empty JSON-typed body resolves to undefined', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(
      new Response('', { status: 200, headers: { 'content-type': 'application/json' } }),
    );
    await expect(api('PUT', '/api/layouts/A', {})).resolves.toBeUndefined();
  });
});

describe('upload with progress', () => {
  const OriginalXhr = globalThis.XMLHttpRequest;
  afterEach(() => {
    globalThis.XMLHttpRequest = OriginalXhr;
    resetUnauthorizedHandler();
  });

  /** A fake XMLHttpRequest answering `status` with a JSON `body` after reporting half the upload. */
  function fakeXhr(status: number, body: unknown, outcome: 'load' | 'error' | 'timeout' | 'hang' = 'load') {
    const sent: Array<{ method: string; url: string; body: unknown }> = [];
    class FakeXhr {
      status = 0;
      responseText = '';
      upload: { onprogress: ((e: { lengthComputable: boolean; loaded: number; total: number }) => void) | null } = {
        onprogress: null,
      };
      onload: (() => void) | null = null;
      onerror: (() => void) | null = null;
      ontimeout: (() => void) | null = null;
      onabort: (() => void) | null = null;
      aborted = false;
      abort() {
        this.aborted = true;
        setTimeout(() => this.onabort?.(), 0);
      }
      private method = '';
      private url = '';
      open(method: string, url: string) {
        this.method = method;
        this.url = url;
      }
      getResponseHeader(name: string) {
        return name.toLowerCase() === 'content-type' ? 'application/json' : null;
      }
      send(data: unknown) {
        sent.push({ method: this.method, url: this.url, body: data });
        this.upload.onprogress?.({ lengthComputable: true, loaded: 50, total: 100 });
        if (outcome === 'hang') return;
        this.status = outcome === 'load' ? status : 0;
        this.responseText = outcome === 'load' ? JSON.stringify(body) : '';
        const done = outcome === 'load' ? this.onload : outcome === 'error' ? this.onerror : this.ontimeout;
        setTimeout(() => done?.(), 0);
      }
    }
    globalThis.XMLHttpRequest = FakeXhr as unknown as typeof XMLHttpRequest;
    return sent;
  }

  it('posts the form, reports progress and resolves to the JSON answer', async () => {
    const sent = fakeXhr(200, { id: 'a1', kind: 'zt', width: 1, height: 1, frameCount: 3 });
    const form = new FormData();
    const progress = vi.fn();
    await expect(upload('/api/assets', form, { onProgress: progress })).resolves.toMatchObject({ id: 'a1' });
    expect(sent).toEqual([{ method: 'POST', url: '/api/assets', body: form }]);
    expect(progress).toHaveBeenCalledWith(0.5);
  });

  it('rejects with the status and error of a failed upload', async () => {
    fakeXhr(413, { error: 'file is too large' });
    const err = await upload('/api/assets', new FormData()).catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).status).toBe(413);
    expect((err as ApiError).errors).toEqual(['file is too large']);
  });

  it('sends a 401 to the login page', async () => {
    fakeXhr(401, { error: 'login required' });
    const handler = vi.fn();
    setUnauthorizedHandler(handler);
    await upload('/api/assets', new FormData()).catch(() => undefined);
    expect(handler).toHaveBeenCalled();
  });

  it('rejects when the network fails', async () => {
    fakeXhr(0, null, 'error');
    await expect(upload('/api/assets', new FormData())).rejects.toThrow(TypeError);
  });

  it('rejects with a TimeoutError when the upload times out', async () => {
    fakeXhr(0, null, 'timeout');
    const err = await upload('/api/assets', new FormData()).catch((e: unknown) => e);
    expect((err as DOMException).name).toBe('TimeoutError');
  });

  it('aborts the request when the signal fires, rejecting with an AbortError', async () => {
    fakeXhr(0, null, 'hang');
    const ctrl = new AbortController();
    const pending = upload('/api/assets', new FormData(), { signal: ctrl.signal }).catch((e: unknown) => e);
    ctrl.abort();
    expect(((await pending) as DOMException).name).toBe('AbortError');
  });

  it('does not send when the signal has already fired', async () => {
    const sent = fakeXhr(200, {});
    const ctrl = new AbortController();
    ctrl.abort();
    const err = await upload('/api/assets', new FormData(), { signal: ctrl.signal }).catch((e: unknown) => e);
    expect((err as DOMException).name).toBe('AbortError');
    expect(sent).toHaveLength(0);
  });
});
