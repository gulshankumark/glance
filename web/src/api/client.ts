export class ApiError extends Error {
  status: number;
  errors: string[];

  constructor(status: number, errors: string[]) {
    super(errors.join(', ') || `API error ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.errors = errors;
  }
}

type UnauthorizedHandler = (currentHash: string) => void;

export interface ApiOptions {
  /** A 401 is the answer itself (e.g. a wrong password), not "log in first": do not redirect to the login page. */
  skipAuthRedirect?: boolean;
  /** Aborts the request (fetch rejects with an AbortError). */
  signal?: AbortSignal;
}

const defaultUnauthorizedHandler: UnauthorizedHandler = (currentHash: string) => {
  const current = currentHash.startsWith('#') ? currentHash : `#${currentHash}`;
  // Already on the login page: stay there instead of nesting it inside its own return value.
  if (current.startsWith('#login')) return;
  window.location.hash = `#login?return=${encodeURIComponent(current)}`;
};

let unauthorizedHandler: UnauthorizedHandler = defaultUnauthorizedHandler;

export function setUnauthorizedHandler(fn: UnauthorizedHandler): void {
  unauthorizedHandler = fn;
}

export function resetUnauthorizedHandler(): void {
  unauthorizedHandler = defaultUnauthorizedHandler;
}

/** Sends the user to the login page (via the unauthorized handler), remembering the current hash. */
export function notifyUnauthorized(): void {
  unauthorizedHandler(window.location.hash || '#display');
}

async function handleResponse<T>(res: Response, options: ApiOptions = {}): Promise<T> {
  if (res.status === 401 && !options.skipAuthRedirect) {
    notifyUnauthorized();
  }

  if (!res.ok) {
    let errors: string[] = [];
    try {
      const data = await res.json();
      if (Array.isArray(data.errors)) {
        errors = data.errors.map(String);
      } else if (typeof data.error === 'string') {
        errors = [data.error];
      }
    } catch {
      // Body not JSON
    }
    if (errors.length === 0) {
      errors = [`Request failed with status ${res.status}`];
    }
    throw new ApiError(res.status, errors);
  }

  if (res.status === 204) {
    return undefined as unknown as T;
  }

  const contentType = res.headers.get('content-type') || '';
  if (contentType.includes('application/json')) {
    return (await res.json()) as T;
  }
  return (await res.text()) as unknown as T;
}

export async function api<T>(method: string, path: string, body?: unknown, options?: ApiOptions): Promise<T> {
  const headers: Record<string, string> = {};
  let bodyPayload: BodyInit | undefined;

  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
    bodyPayload = JSON.stringify(body);
  }

  const res = await fetch(path, {
    method,
    headers,
    body: bodyPayload,
    signal: options?.signal,
  });

  return handleResponse<T>(res, options);
}

export async function upload<T>(path: string, form: FormData): Promise<T> {
  const res = await fetch(path, {
    method: 'POST',
    body: form,
  });

  return handleResponse<T>(res);
}

/** GET a binary resource (e.g. a preview JPEG) through the same 401 handling as api(); throws ApiError if not ok. */
export async function getBlob(path: string): Promise<Blob> {
  const res = await fetch(path, { cache: 'no-store' });
  if (res.status === 401) notifyUnauthorized();
  if (!res.ok) throw new ApiError(res.status, [`Request failed with status ${res.status}`]);
  return res.blob();
}

/** POST a JSON body and return the binary answer (e.g. POST /api/render's JPEG); errors as for api(). */
export async function postBlob(path: string, body: unknown, options: ApiOptions = {}): Promise<Blob> {
  const res = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal: options.signal,
  });
  if (!res.ok) return handleResponse<never>(res, options);
  return res.blob();
}
