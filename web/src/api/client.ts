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

let defaultUnauthorizedHandler: UnauthorizedHandler = (currentHash: string) => {
  const current = currentHash.startsWith('#') ? currentHash : `#${currentHash}`;
  window.location.hash = `#login?return=${encodeURIComponent(current)}`;
};

let unauthorizedHandler: UnauthorizedHandler = defaultUnauthorizedHandler;

export function setUnauthorizedHandler(fn: UnauthorizedHandler): void {
  unauthorizedHandler = fn;
}

export function resetUnauthorizedHandler(): void {
  unauthorizedHandler = defaultUnauthorizedHandler;
}

async function handleResponse<T>(res: Response): Promise<T> {
  if (res.status === 401) {
    const currentHash = window.location.hash || '#display';
    unauthorizedHandler(currentHash);
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

export async function api<T>(method: string, path: string, body?: unknown): Promise<T> {
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
  });

  return handleResponse<T>(res);
}

export async function upload<T>(path: string, form: FormData): Promise<T> {
  const res = await fetch(path, {
    method: 'POST',
    body: form,
  });

  return handleResponse<T>(res);
}
