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
  /** Persisted role pins (role to sensor id); absent roles are automatic. */
  pins?: Record<string, string>;
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

// ---- Layout JSON (docs/api.md "Layout JSON"); sensor refs are sensor ids or "role:<role>" ----

export interface FontSpec {
  family: string;
  size: number;
  weight: number;
}
export type TextAlign = 'left' | 'center' | 'right';

export type Background =
  | { type: 'color'; color: string }
  | { type: 'image'; assetId: string }
  | { type: 'animation'; assetId: string };

export interface Threshold {
  from: number;
  color: string;
}

interface ItemBase {
  id: string;
  x: number;
  y: number;
}
export interface TextItem extends ItemBase {
  type: 'text';
  text: string;
  font: FontSpec;
  color: string;
  align: TextAlign;
}
export interface SensorItem extends ItemBase {
  type: 'sensor';
  sensor: string;
  label?: string | null;
  showUnit: boolean;
  decimals: number;
  font: FontSpec;
  color: string;
  align: TextAlign;
}
export interface ClockItem extends ItemBase {
  type: 'clock';
  format?: string | null;
  timeZone: string;
  label?: string | null;
  font: FontSpec;
  color: string;
  align: TextAlign;
}
export interface ImageItem extends ItemBase {
  type: 'image';
  assetId: string;
  width: number;
  height: number;
}
interface GaugeBase extends ItemBase {
  sensor: string;
  width: number;
  height: number;
  min?: number | null;
  max?: number | null;
  color: string;
  trackColor: string;
  thresholds: Threshold[];
  showValue: boolean;
  label?: string | null;
  font: FontSpec;
}
export interface BarItem extends GaugeBase {
  type: 'bar';
  vertical: boolean;
  rounded: boolean;
}
export interface SegmentedBarItem extends GaugeBase {
  type: 'segmentedBar';
  segments: number;
  gap: number;
  vertical: boolean;
}
export interface ArcGaugeItem extends GaugeBase {
  type: 'arcGauge';
  startAngle: number;
  sweepAngle: number;
  thickness: number;
}
export interface DialItem extends GaugeBase {
  type: 'dial';
  startAngle: number;
  sweepAngle: number;
  ticks: number;
  needleColor: string;
}
export interface HistoryGraphItem extends GaugeBase {
  type: 'historyGraph';
  windowSeconds: number;
  area: boolean;
  grid: boolean;
}
export interface SparklineItem extends GaugeBase {
  type: 'sparkline';
  windowSeconds: number;
}

export type Item =
  | TextItem
  | SensorItem
  | ClockItem
  | ImageItem
  | BarItem
  | SegmentedBarItem
  | ArcGaugeItem
  | DialItem
  | HistoryGraphItem
  | SparklineItem;
export type ItemType = Item['type'];

export interface Layout {
  name: string;
  width: number;
  height: number;
  background: Background;
  items: Item[];
}

/** GET /api/assets item, POST /api/assets answer. `image` for image items/backgrounds, `gif`/`zt` for animations. */
export interface Asset {
  id: string;
  kind: 'image' | 'gif' | 'zt';
  width: number;
  height: number;
  frameCount: number;
}

/** GET /api/fonts. */
export interface FontList {
  default: string;
  families: string[];
}

/** GET /api/timezones item; `local` comes first. */
export interface TimeZone {
  id: string;
  label: string;
}
