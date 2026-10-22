import type { Page } from '@playwright/test';
import type { DeviceView } from '../src/api/types';

export interface MockApiOptions {
  authenticated?: boolean;
  networkAccess?: boolean;
  loopback?: boolean;
  password?: string;
  devices?: DeviceView[];
  layouts?: Array<{ name: string; width: number; height: number }>;
  /** The mock event stream fails (error instead of open) on every connection attempt. */
  eventsFail?: boolean;
}

export interface MockApiHandle {
  calls: Array<{ method: string; path: string; body?: unknown }>;
  sendEvent: (event: string, data: unknown) => Promise<void>;
  closeEvents: () => Promise<void>;
  options: MockApiOptions;
  devices: DeviceView[];
}

// 1x1 black JPEG
const dummyJpeg = Buffer.from([
  0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10, 0x4a, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x01, 0x00, 0x48,
  0x00, 0x48, 0x00, 0x00, 0xff, 0xdb, 0x00, 0x43, 0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08,
  0x07, 0x07, 0x07, 0x09, 0x09, 0x08, 0x0a, 0x0c, 0x14, 0x0d, 0x0c, 0x0b, 0x0b, 0x0c, 0x19, 0x12,
  0x13, 0x0f, 0x14, 0x1d, 0x1a, 0x1f, 0x1e, 0x1d, 0x1a, 0x1c, 0x1c, 0x20, 0x24, 0x2e, 0x27, 0x20,
  0x22, 0x2c, 0x23, 0x1c, 0x1c, 0x28, 0x37, 0x29, 0x2c, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1f, 0x27,
  0x39, 0x3d, 0x38, 0x32, 0x3c, 0x2e, 0x33, 0x34, 0x32, 0xff, 0xc0, 0x00, 0x0b, 0x08, 0x00, 0x01,
  0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xff, 0xc4, 0x00, 0x1f, 0x00, 0x00, 0x01, 0x05, 0x01, 0x01,
  0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04,
  0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0xff, 0xda, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3f,
  0x00, 0xbf, 0x80, 0xff, 0xd9
]);

export async function mockApi(page: Page, options: MockApiOptions = {}): Promise<MockApiHandle> {
  const opts: MockApiOptions = {
    authenticated: true,
    networkAccess: true,
    loopback: true,
    password: 'password123',
    ...options,
  };

  const calls: Array<{ method: string; path: string; body?: unknown }> = [];

  const devices: DeviceView[] = options.devices !== undefined ? [...options.devices] : [
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

  const layouts = options.layouts !== undefined ? [...options.layouts] : [
    { name: 'Default', width: 1280, height: 480 },
    { name: 'Gaming', width: 1280, height: 480 },
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

  // A mock EventSource behaving like the real one: fires open (or error, with eventsFail) shortly after construction.
  // window.__eventSourceCount counts connection attempts; the latest instance is window.__mockEventSource.
  await page.addInitScript(
    ({ fail }) => {
      class MockEventSource extends EventTarget {
        url: string;
        readyState = 0; // CONNECTING
        onopen: ((ev: Event) => unknown) | null = null;
        onerror: ((ev: Event) => unknown) | null = null;
        onmessage: ((ev: MessageEvent) => unknown) | null = null;

        constructor(url: string) {
          super();
          this.url = url;
          const w = window as unknown as { __mockEventSource: MockEventSource; __eventSourceCount?: number };
          w.__mockEventSource = this;
          w.__eventSourceCount = (w.__eventSourceCount ?? 0) + 1;
          setTimeout(() => {
            if (this.readyState === 2) return;
            const ev = new Event(fail ? 'error' : 'open');
            this.readyState = fail ? 2 : 1;
            if (fail) this.onerror?.(ev);
            else this.onopen?.(ev);
            this.dispatchEvent(ev);
          }, 10);
        }

        close() {
          this.readyState = 2; // CLOSED
        }
      }

      (window as unknown as { EventSource: typeof MockEventSource }).EventSource = MockEventSource;
    },
    { fail: !!opts.eventsFail },
  );

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

    if (path.startsWith('/api/preview/')) {
      await route.fulfill({
        status: 200,
        contentType: 'image/jpeg',
        headers: {
          'Cache-Control': 'no-store',
        },
        body: dummyJpeg,
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
        body: JSON.stringify(devices),
      });
      return;
    }

    if (path.startsWith('/api/devices/') && method === 'PUT') {
      const key = decodeURIComponent(path.replace('/api/devices/', ''));
      const dev = devices.find((d) => d.key === key);
      if (!dev) {
        await route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ error: 'unknown device' }) });
        return;
      }
      const patch = body as { rotation?: 0 | 90 | 180 | 270; activeLayout?: string | null };
      if (patch.rotation !== undefined) dev.rotation = patch.rotation;
      if (patch.activeLayout !== undefined) dev.activeLayout = patch.activeLayout;

      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(dev),
      });
      return;
    }

    if (path === '/api/layouts') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(layouts),
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
    devices,
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
