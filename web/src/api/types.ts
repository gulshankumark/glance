// Mirrors docs/api.md (the API contract); src/api/types.test.ts checks the documented examples.

export type DeviceStatus = 'connected' | 'unsupported' | 'retrying';
export type Rotation = 0 | 90 | 180 | 270;

/** GET /api/devices item, PUT /api/devices/{key} response, `devices` event item. */
export interface DeviceView {
  key: string;
  driverId: string;
  name: string;
  status: DeviceStatus;
  width: number;
  height: number;
  modelCode: string;
  serial: string;
  message: string | null;
  /** ISO time or null. */
  nextRetry: string | null;
  rotation: Rotation;
  activeLayout: string | null;
  layoutMismatch: boolean;
  fps: number;
  framesSent: number;
}

/** GET /api/layouts item; a layout file that cannot be loaded has width/height 0 and `error` set. */
export interface LayoutSummary {
  name: string;
  width: number;
  height: number;
  error: string | null;
}

export interface Sensor {
  id: string;
  hardware: string;
  hardwareKind: 'cpu' | 'gpu' | 'memory' | 'storage' | 'motherboard' | 'other';
  name: string;
  kind:
    | 'temperature'
    | 'load'
    | 'clock'
    | 'power'
    | 'fan'
    | 'data'
    | 'smallData'
    | 'voltage'
    | 'control'
    | 'throughput'
    | 'other';
  unit: string | null;
  value: number | null;
  alias: string | null;
}

export interface SensorsResponse {
  sensors: Sensor[];
  roles: Record<string, string | null>;
  diagnostics: Array<{ code: string; message: string }>;
}

export interface AuthState {
  loopback: boolean;
  authenticated: boolean;
  networkAccess: boolean;
}

export interface Settings {
  unit: 'celsius' | 'fahrenheit';
  clock24h: boolean;
  maxOverlayAnimationFps: number | null;
  port: number;
  networkAccess: boolean;
  https: boolean;
  hasPassword: boolean;
}
