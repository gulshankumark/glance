import type { Page } from '@playwright/test';

export interface MockApiOptions {
  authenticated?: boolean;
  networkAccess?: boolean;
  loopback?: boolean;
  password?: string;
}

export interface MockApiHandle {
  calls: Array<{ method: string; path: string; body?: unknown }>;
  sendEvent: (event: string, data: unknown) => Promise<void>;
  closeEvents: () => Promise<void>;
  options: MockApiOptions;
}

export async function mockApi(page: Page, options: MockApiOptions = {}): Promise<MockApiHandle> {
  const opts: MockApiOptions = {
    authenticated: true,
    networkAccess: true,
    loopback: true,
    password: 'password123',
    ...options,
  };

  const calls: Array<{ method: string; path: string; body?: unknown }> = [];

  const defaultDevices = [
    {
      key: 'thermalright-hid-5302_SER1',
      driver: 'thermalright-hid-5302',
      serial: 'SER1',
      name: 'Thermalright 1280x480 LCD',
      width: 1280,
      height: 480,
      rotation: 0,
      status: 'connected',
      message: null,
      nextRetry: null,
      activeLayout: 'Default',
      layoutMismatch: false,
      fps: 10.0,
      framesSent: 1234,
    },
  ];

  const defaultSensors = {
    sensors: [
      {
        id: 'cpu/t',
        hardware: 'AMD Ryzen 9 9950X',
        hardwareKind: 'cpu',
        name: 'CPU Package',
        kind: 'temperature',
        unit: '°C',
        value: 55.0,
        alias: 'CPU',
      },
      {
        id: '/amdcpu/0/temperature/2',
        hardware: 'AMD Ryzen 9 9950X',
        hardwareKind: 'cpu',
        name: 'Tctl',
        kind: 'temperature',
        unit: '°C',
        value: 61.0,
        alias: 'CPU Die',
      },
      {
        id: 'gpu/t',
        hardware: 'Intel Arc Pro B70',
        hardwareKind: 'gpu',
        name: 'GPU Core',
        kind: 'temperature',
        unit: '°C',
        value: 48.0,
        alias: 'GPU',
      },
    ],
    roles: {
      'cpu.temperature': 'cpu/t',
      'gpu.temperature': 'gpu/t',
    },
    diagnostics: [],
  };

  // Add mock EventSource in browser so SSE stays open without disconnecting
  await page.addInitScript(() => {
    class MockEventSource extends EventTarget {
      url: string;
      readyState: number = 0; // CONNECTING
      onopen: ((ev: Event) => unknown) | null = null;
      onerror: ((ev: Event) => unknown) | null = null;
      onmessage: ((ev: MessageEvent) => unknown) | null = null;

      constructor(url: string) {
        super();
        this.url = url;
        (window as unknown as { __mockEventSource: MockEventSource }).__mockEventSource = this;
        setTimeout(() => {
          this.readyState = 1; // OPEN
          const openEvent = new Event('open');
          this.onopen?.(openEvent);
          this.dispatchEvent(openEvent);
        }, 10);
      }

      close() {
        this.readyState = 2; // CLOSED
      }
    }

    (window as unknown as { EventSource: typeof MockEventSource }).EventSource = MockEventSource;
  });

  await page.route('**/api/**', async (route) => {
    const req = route.request();
    const url = new URL(req.url());
    const path = url.pathname;
    const method = req.method();

    let body: unknown;
    try {
      const text = req.postData();
      if (text) body = JSON.parse(text);
    } catch {
      // ignore
    }

    calls.push({ method, path, body });

    // Auth gate check
    if (!opts.loopback && opts.networkAccess && !opts.authenticated) {
      if (
        path !== '/api/auth/login' &&
        path !== '/api/auth/state' &&
        path !== '/api/health'
      ) {
        await route.fulfill({
          status: 401,
          contentType: 'application/json',
          body: JSON.stringify({ error: 'login required' }),
        });
        return;
      }
    }

    if (path === '/api/events') {
      await route.fulfill({
        status: 200,
        contentType: 'text/event-stream',
        headers: {
          'Cache-Control': 'no-store',
        },
        body: '',
      });
      return;
    }

    if (path === '/api/auth/state') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          loopback: opts.loopback,
          authenticated: opts.authenticated,
          networkAccess: opts.networkAccess,
        }),
      });
      return;
    }

    if (path === '/api/auth/login' && method === 'POST') {
      const parsed = body as { password?: string };
      if (parsed?.password === opts.password) {
        opts.authenticated = true;
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ authenticated: true }),
        });
      } else {
        await route.fulfill({
          status: 401,
          contentType: 'application/json',
          body: JSON.stringify({ error: 'wrong password' }),
        });
      }
      return;
    }

    if (path === '/api/devices') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(defaultDevices),
      });
      return;
    }

    if (path === '/api/sensors') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(defaultSensors),
      });
      return;
    }

    if (path === '/api/health') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ status: 'ok', version: '0.1.0' }),
      });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({}),
    });
  });

  return {
    calls,
    options: opts,
    sendEvent: async (event: string, data: unknown) => {
      await page.evaluate(
        ({ ev, d }) => {
          const es = (window as unknown as { __mockEventSource?: EventTarget }).__mockEventSource;
          if (es) {
            es.dispatchEvent(new MessageEvent(ev, { data: JSON.stringify(d) }));
          }
        },
        { ev: event, d: data }
      );
    },
    closeEvents: async () => {
      await page.evaluate(() => {
        const es = (window as unknown as { __mockEventSource?: { onerror?: (ev: Event) => void; dispatchEvent: (ev: Event) => void } }).__mockEventSource;
        if (es) {
          const errEv = new Event('error');
          es.onerror?.(errEv);
          es.dispatchEvent(errEv);
        }
      });
    },
  };
}
