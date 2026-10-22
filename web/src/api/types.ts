export interface DeviceView {
  key: string;
  driver: string;
  serial: string;
  name: string;
  width: number;
  height: number;
  rotation: 0 | 90 | 180 | 270;
  status: 'connected' | 'unsupported' | 'retrying';
  message: string | null;
  nextRetry: string | null;
  activeLayout: string | null;
  layoutMismatch: boolean;
  fps: number;
  framesSent: number;
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
