import { describe, expect, expectTypeOf, it } from 'vitest';
import fs from 'node:fs';
import path from 'node:path';
import type { DeviceView, Sensor, Settings } from './types';

const apiDoc = fs.readFileSync(path.resolve(process.cwd(), '..', 'docs', 'api.md'), 'utf-8');

/** The first JSON value that follows `marker` in docs/api.md. */
function docExample(marker: string, open: '[' | '{' = '{'): unknown {
  const start = apiDoc.indexOf(open, apiDoc.indexOf(marker));
  const close = open === '[' ? ']' : '}';
  let depth = 0;
  for (let i = start; i < apiDoc.length; i++) {
    if (apiDoc[i] === open) depth++;
    if (apiDoc[i] === close && --depth === 0) return JSON.parse(apiDoc.slice(start, i + 1));
  }
  throw new Error(`no example after ${marker}`);
}

// Object literals with every property: tsc (npm run build) rejects a missing required or an unknown property.
const device: DeviceView = {
  key: 'k', driverId: 'd', name: 'n', status: 'connected', width: 1, height: 1, modelCode: 'm', serial: 's',
  message: null, nextRetry: null, rotation: 0, activeLayout: null, layoutMismatch: false, fps: 0, framesSent: 0,
};
const sensor: Sensor = {
  id: 'i', hardware: 'h', hardwareKind: 'cpu', name: 'n', kind: 'load', unit: null, value: null, alias: null,
};
const settings: Settings = {
  unit: 'celsius', clock24h: true, maxOverlayAnimationFps: null, port: 1, networkAccess: false, https: false,
  hasPassword: false,
};

const keys = (o: unknown) => Object.keys(o as object).sort();

describe('API types mirror docs/api.md', () => {
  it('DeviceView has exactly the documented properties, all required', () => {
    const [example] = docExample('### GET /api/devices', '[') as unknown[];
    expect(keys(device)).toEqual(keys(example));
    expectTypeOf<DeviceView['fps']>().toEqualTypeOf<number>();
    expectTypeOf<DeviceView['framesSent']>().toEqualTypeOf<number>();
    expectTypeOf<DeviceView['layoutMismatch']>().toEqualTypeOf<boolean>();
    expectTypeOf<DeviceView['driverId']>().toEqualTypeOf<string>();
    expectTypeOf<DeviceView['serial']>().toEqualTypeOf<string>();
    expectTypeOf<DeviceView['modelCode']>().toEqualTypeOf<string>();
  });

  it('Sensor has exactly the documented properties', () => {
    const example = docExample('### GET /api/sensors') as { sensors: unknown[] };
    expect(keys(sensor)).toEqual(keys(example.sensors[0]));
  });

  it('Settings has exactly the documented properties', () => {
    expect(keys(settings)).toEqual(keys(docExample('### GET /api/settings')));
  });
});
