import type { Page } from '@playwright/test';
import type { Asset, DeviceView, Item, Layout, LayoutSummary, SensorsResponse } from '../src/api/types';

export interface MockApiOptions {
  authenticated?: boolean;
  networkAccess?: boolean;
  loopback?: boolean;
  password?: string;
  devices?: DeviceView[];
  /** Full layout documents (the summaries of GET /api/layouts are derived from them). */
  layoutDocs?: Layout[];
  /** While set, PUT /api/layouts/{name} answers 400 {errors: layoutSaveErrors}; may be changed while a test runs. */
  layoutSaveErrors?: string[];
  /** Delay of GET /api/layouts/{name} answers in ms, by layout name. */
  layoutGetDelayMs?: Record<string, number>;
  /** Delay of each POST /api/render answer in ms (default 0). */
  renderDelayMs?: number;
  /** The mock event stream fails (error instead of open) on every connection attempt. */
  eventsFail?: boolean;
  /** HTTP status of GET /api/preview/{key}.jpg (default 200 with a tiny JPEG); may be changed while a test runs. */
  previewStatus?: 200 | 404;
  /** GET /api/sensors answer (default: three sensors and two roles, see defaultSensors). */
  sensors?: SensorsResponse;
  /** While set, PUT /api/sensors/alias and PUT /api/roles/{role} answer 400 {error}; may be changed while a test runs. */
  sensorWriteError?: string;
  /** Stored assets (default: seedAssets()). */
  assets?: Asset[];
  /** While set, POST /api/assets answers this status with {error}; may be changed while a test runs. */
  assetUploadStatus?: 400 | 413;
  /** While set, POST /api/assets fails at the network level (the connection is dropped). */
  assetUploadFail?: boolean;
  /** Delay of each POST /api/assets answer in ms (default 0). */
  assetUploadDelayMs?: number;
}

/** A POST /api/assets request as the mock received it. */
export interface AssetUpload {
  contentType: string;
  fileName: string | null;
  kind: string | null;
  ztRotation: string | null;
}

export interface MockApiHandle {
  calls: Array<{ method: string; path: string; body?: unknown }>;
  /** Number of GET /api/preview requests so far. */
  previewRequests: () => number;
  sendEvent: (event: string, data: unknown) => Promise<void>;
  closeEvents: () => Promise<void>;
  options: MockApiOptions;
  devices: DeviceView[];
  /** The stored layouts by name. */
  layouts: Map<string, Layout>;
  /** Bodies of the POST /api/render requests so far. */
  renders: () => Layout[];
  /** The stored assets (uploads are appended). */
  assets: Asset[];
  /** Every POST /api/assets so far. */
  uploads: AssetUpload[];
  /** The highest number of POST /api/render fetches the page had pending (not answered, not aborted) at once. */
  maxRendersInFlight: () => Promise<number>;
}

const mockFont = (size: number) => ({ family: 'Inter', size, weight: 400 });

/** Seed layouts: "Default" (a text, a sensor readout and a bar) and "Gaming" (a clock), both 1280×480. */
export function seedLayouts(): Layout[] {
  return [
    {
      name: 'Default', width: 1280, height: 480, background: { type: 'color', color: '#101820' },
      items: [
        { type: 'text', id: 'title', x: 40, y: 40, text: 'Glance', font: mockFont(48), color: '#FFFFFF', align: 'left' },
        {
          type: 'sensor', id: 'cpu', x: 40, y: 200, sensor: 'role:cpu.temperature', label: 'CPU', showUnit: true,
          decimals: 0, font: mockFont(32), color: '#FFFFFF', align: 'left',
        },
        {
          type: 'bar', id: 'bar1', x: 600, y: 300, width: 400, height: 30, sensor: 'cpu/t', color: '#4AAFFF',
          trackColor: '#33FFFFFF', thresholds: [], showValue: false, font: mockFont(16), vertical: false, rounded: true,
        },
      ],
    },
    {
      name: 'Gaming', width: 1280, height: 480, background: { type: 'color', color: '#000000' },
      items: [{ type: 'clock', id: 'clock', x: 500, y: 200, timeZone: 'local', font: mockFont(64), color: '#FFFFFF', align: 'left' }],
    },
  ];
}

/** Seed assets: an image "img1" (320×240) and a GIF animation "anim1". */
export function seedAssets(): Asset[] {
  return [
    { id: 'img1', kind: 'image', width: 320, height: 240, frameCount: 1 },
    { id: 'anim1', kind: 'gif', width: 320, height: 240, frameCount: 12 },
  ];
}

/** Bounds like the server's: text-like items get a rough ink box (0×0 for an empty string), others their box. */
function mockBounds(layout: Layout): Record<string, { x: number; y: number; width: number; height: number }> {
  const result: Record<string, { x: number; y: number; width: number; height: number }> = {};
  for (const item of layout.items as Item[]) {
    if (item.type === 'text' || item.type === 'sensor' || item.type === 'clock') {
      const text = item.type === 'text' ? item.text : item.type === 'clock' ? '12:34' : `${item.label ?? ''} 55°C`;
      const w = text.length * item.font.size * 0.5;
      result[item.id] = { x: item.x, y: item.y + 4, width: w, height: text ? item.font.size * 0.8 : 0 };
    } else {
      result[item.id] = { x: item.x, y: item.y, width: item.width, height: item.height };
    }
  }
  return result;
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

/** The seed device (a connected 1280×480 screen showing "Default"), with optional overrides. */
export function seedDevice(overrides: Partial<DeviceView> = {}): DeviceView {
  return {
    key: 'thermalright-hid-5302_SER1',
    driverId: 'thermalright-hid-5302',
    name: 'Thermalright 1280x480 LCD',
    status: 'connected',
    width: 1280,
    height: 480,
    modelCode: '5302',
    serial: 'SER1',
    message: null,
    nextRetry: null,
    rotation: 0,
    activeLayout: 'Default',
    layoutMismatch: false,
    fps: 10.0,
    framesSent: 1234,
    ...overrides,
  };
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

  const devices: DeviceView[] = options.devices !== undefined ? [...options.devices] : [seedDevice()];

  const layouts = new Map<string, Layout>((options.layoutDocs ?? seedLayouts()).map((l) => [l.name, l]));
  const summaries = (): LayoutSummary[] =>
    [...layouts.values()]
      .map((l) => ({ name: l.name, width: l.width, height: l.height, error: null }))
      .sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }));
  const renders: Layout[] = [];
  const assets: Asset[] = options.assets ? [...options.assets] : seedAssets();
  const uploads: AssetUpload[] = [];

  const defaultSensors: SensorsResponse = options.sensors ?? {
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

  // Pins made through PUT /api/roles/{role}; an unpinned role resolves to its initial (automatic) sensor.
  const automaticRoles = { ...defaultSensors.roles };

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

      // Count the page's pending POST /api/render fetches (an abort settles a fetch at once).
      const w = window as unknown as { __rendersInFlight: number; __maxRendersInFlight: number };
      w.__rendersInFlight = 0;
      w.__maxRendersInFlight = 0;
      const realFetch = window.fetch.bind(window);
      window.fetch = (input: RequestInfo | URL, init?: RequestInit) => {
        const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
        if (!/\/api\/render(\?|$)/.test(url)) return realFetch(input, init);
        w.__rendersInFlight++;
        w.__maxRendersInFlight = Math.max(w.__maxRendersInFlight, w.__rendersInFlight);
        return realFetch(input, init).finally(() => {
          w.__rendersInFlight--;
        });
      };
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
      if (opts.previewStatus === 404) {
        await route.fulfill({ status: 404, body: '' });
        return;
      }
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
        body: JSON.stringify(summaries()),
      });
      return;
    }

    const layoutMatch = /^\/api\/layouts\/([^/]+)(\/duplicate)?$/.exec(path);
    if (layoutMatch) {
      const name = decodeURIComponent(layoutMatch[1]);
      const json = (status: number, data?: unknown) =>
        route.fulfill({ status, contentType: 'application/json', body: data === undefined ? '' : JSON.stringify(data) });
      if (layoutMatch[2] && method === 'POST') {
        const newName = (body as { newName?: string })?.newName ?? '';
        const src = layouts.get(name);
        if (!src) return json(404);
        if (!/^[A-Za-z0-9 _-]{1,64}$/.test(newName) || newName.endsWith(' ')) {
          return json(400, { errors: ['invalid layout name'] });
        }
        if (layouts.has(newName)) return json(409, { errors: ['a layout with that name already exists'] });
        layouts.set(newName, { ...structuredClone(src), name: newName });
        return json(200);
      }
      if (method === 'GET') {
        const delay = opts.layoutGetDelayMs?.[name];
        if (delay) await new Promise((r) => setTimeout(r, delay));
        const doc = layouts.get(name);
        return doc ? json(200, doc) : json(404);
      }
      if (method === 'PUT') {
        const doc = body as Layout;
        if (opts.layoutSaveErrors?.length) return json(400, { errors: opts.layoutSaveErrors });
        if (doc?.name !== name) return json(400, { errors: ['name must match the layout name in the path'] });
        layouts.set(name, doc);
        return json(200);
      }
      if (method === 'DELETE') {
        if (!layouts.delete(name)) return json(404);
        return route.fulfill({ status: 204, body: '' });
      }
    }

    if (path === '/api/render' && method === 'POST') {
      renders.push(body as Layout);
      try {
        if (opts.renderDelayMs) await new Promise((r) => setTimeout(r, opts.renderDelayMs));
        await route.fulfill({ status: 200, contentType: 'image/jpeg', body: dummyJpeg });
      } catch {
        // the page aborted the request
      }
      return;
    }

    if (path === '/api/render/bounds' && method === 'POST') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(mockBounds(body as Layout)) });
      return;
    }

    if (path === '/api/sensors/alias' && method === 'PUT') {
      const { id, alias } = body as { id: string; alias?: string | null };
      const sensor = defaultSensors.sensors.find((x) => x.id === id);
      if (opts.sensorWriteError) {
        await route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({ error: opts.sensorWriteError }) });
      } else if (!sensor) {
        await route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ error: 'unknown sensor' }) });
      } else {
        sensor.alias = alias?.trim() || null;
        await route.fulfill({ status: 204 });
      }
      return;
    }

    const rolePath = /^\/api\/roles\/(.+)$/.exec(path);
    if (rolePath && method === 'PUT') {
      const role = decodeURIComponent(rolePath[1]);
      const { sensorId } = body as { sensorId: string | null };
      if (opts.sensorWriteError) {
        await route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({ error: opts.sensorWriteError }) });
      } else if (!(role in defaultSensors.roles)) {
        await route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ error: 'unknown role' }) });
      } else {
        defaultSensors.roles[role] = sensorId || automaticRoles[role] || null;
        await route.fulfill({ status: 204 });
      }
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

    if (path === '/api/fonts') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ default: 'Inter', families: ['Inter', 'Arial', 'Bahnschrift'] }),
      });
      return;
    }

    if (path === '/api/timezones') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          { id: 'local', label: 'Local time' },
          { id: 'Europe/London', label: '(UTC+00:00) Dublin, Edinburgh, Lisbon, London' },
          { id: 'Asia/Tokyo', label: '(UTC+09:00) Osaka, Sapporo, Tokyo' },
        ]),
      });
      return;
    }

    if (path === '/api/assets' && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(assets) });
      return;
    }

    if (path === '/api/assets' && method === 'POST') {
      const raw = req.postDataBuffer()?.toString('latin1') ?? '';
      const part = (name: string) => new RegExp(`name="${name}"\r\n\r\n([^\r]*)`).exec(raw)?.[1] ?? null;
      const upload: AssetUpload = {
        contentType: req.headers()['content-type'] ?? '',
        fileName: /name="file"; filename="([^"]*)"/.exec(raw)?.[1] ?? null,
        kind: part('kind'),
        ztRotation: part('ztRotation'),
      };
      uploads.push(upload);
      if (opts.assetUploadFail) {
        await route.abort('failed');
        return;
      }
      if (opts.assetUploadDelayMs) await new Promise((r) => setTimeout(r, opts.assetUploadDelayMs));
      const status = opts.assetUploadStatus;
      if (status) {
        const error = status === 413 ? 'file is too large' : 'invalid media';
        await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify({ error }) });
        return;
      }
      const kind = (upload.kind ?? 'image') as Asset['kind'];
      const asset: Asset = { id: `up${uploads.length}`, kind, width: 320, height: 240, frameCount: kind === 'image' ? 1 : 30 };
      assets.push(asset);
      try {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(asset) });
      } catch {
        // the page aborted the upload
      }
      return;
    }

    const thumbMatch = /^\/api\/assets\/([^/]+)\/thumbnail\.jpg$/.exec(path);
    if (thumbMatch && method === 'GET') {
      const known = assets.some((a) => a.id === decodeURIComponent(thumbMatch[1]));
      await route.fulfill(known ? { status: 200, contentType: 'image/jpeg', body: dummyJpeg } : { status: 404, body: '' });
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
    previewRequests: () => calls.filter((c) => c.path.startsWith('/api/preview/')).length,
    options: opts,
    devices,
    layouts,
    assets,
    uploads,
    renders: () => [...renders],
    maxRendersInFlight: () =>
      page.evaluate(() => (window as unknown as { __maxRendersInFlight: number }).__maxRendersInFlight),
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
